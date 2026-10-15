using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using NAudio.Wave;
using KebuzPlayer.Lang;

namespace KebuzPlayer.ViewModels;

public abstract partial class TrackListWidgetContent : ViewModelBase, IDisposable
{
    protected readonly LibraryService Library;
    protected readonly AudioService Audio;
    protected readonly CoverThumbCache Thumbs;

    public ObservableCollection<TrackRow> Tracks { get; } = [];

    [ObservableProperty]
    private bool _isEmpty;

    protected TrackListWidgetContent(LibraryService library, AudioService audio, CoverThumbCache thumbs)
    {
        Library = library;
        Audio = audio;
        Thumbs = thumbs;
    }

    protected abstract Task<System.Collections.Generic.List<Models.Track>> QueryAsync();

    public async Task LoadAsync()
    {
        try
        {
            var tracks = await QueryAsync();
            foreach (var row in Tracks) row.OnCardDetached();
            Tracks.Clear();
            int n = 1;
            foreach (var t in tracks)
            {
                var row = new TrackRow(t, n++, Thumbs);
                Tracks.Add(row);
                row.OnCardAttached();
            }
            IsEmpty = Tracks.Count == 0;
        }
        catch { IsEmpty = true; }
    }

    [RelayCommand]
    private void Play(TrackRow? row)
    {
        if (row == null) return;
        var queue = Tracks.Select(r => r.Track).ToList();
        int index = queue.FindIndex(t => t.Id == row.Track.Id);
        if (index < 0) return;
        Audio.SetQueue(queue, index);
        Audio.Play(row.Track);
    }

    public virtual void Dispose()
    {
        foreach (var row in Tracks) row.OnCardDetached();
    }
}

public sealed class RecentWidgetContent : TrackListWidgetContent
{
    public RecentWidgetContent(LibraryService library, AudioService audio, CoverThumbCache thumbs)
        : base(library, audio, thumbs)
    {
        Audio.TrackChanged += OnTrackChanged;
        _ = LoadAsync();
    }

    private void OnTrackChanged(object? sender, Track track) => _ = LoadAsync();

    protected override Task<System.Collections.Generic.List<Models.Track>> QueryAsync() =>
        Library.GetRecentlyPlayedTracksAsync(24);

    public override void Dispose()
    {
        Audio.TrackChanged -= OnTrackChanged;
        base.Dispose();
    }
}

public sealed partial class PlaylistWidgetContent : TrackListWidgetContent
{
    private readonly int _playlistId;
    private readonly Action<int> _openPlaylist;

    public bool CoverOnly { get; }

    [ObservableProperty]
    private string _playlistName = "";

    [ObservableProperty]
    private Bitmap? _cover;

    public PlaylistWidgetContent(LibraryService library, AudioService audio, CoverThumbCache thumbs,
                                 int playlistId, Action<int> openPlaylist, bool coverOnly = false)
        : base(library, audio, thumbs)
    {
        _playlistId = playlistId;
        _openPlaylist = openPlaylist;
        CoverOnly = coverOnly;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            var lists = await Library.GetPlaylistsAsync();
            PlaylistName = lists.Find(p => p.Id == _playlistId)?.Name ?? Loc.T("Home.PlaylistFallback");
        }
        catch { PlaylistName = Loc.T("Home.PlaylistFallback"); }

        if (CoverOnly)
        {
            try { Cover = await Thumbs.GetPlaylistCoverAsync(_playlistId); }
            catch { }
            IsEmpty = false;
            return;
        }
        await LoadAsync();
    }

    protected override Task<System.Collections.Generic.List<Models.Track>> QueryAsync() =>
        Library.GetPlaylistTracksAsync(_playlistId);

    [RelayCommand]
    private void Open() => _openPlaylist(_playlistId);
}

public sealed record WidgetPeriodOption(string Key, int Days)
{
    public string Label => Loc.T(Key);
}

public sealed partial class FrequentWidgetContent : TrackListWidgetContent
{
    private readonly Action<int> _onPeriodChanged;

    public WidgetPeriodOption[] Periods { get; } =
    [
        new("Period.Week", 7),
        new("Period.Month", 30),
        new("Period.Year", 365),
        new("Period.All", 0),
    ];

    [ObservableProperty]
    private WidgetPeriodOption _selectedPeriod;

    public FrequentWidgetContent(LibraryService library, AudioService audio, CoverThumbCache thumbs,
                                 int periodDays, Action<int> onPeriodChanged)
        : base(library, audio, thumbs)
    {
        _onPeriodChanged = onPeriodChanged;
        _selectedPeriod = Array.Find(Periods, p => p.Days == periodDays) ?? Periods[1];
        _ = LoadAsync();
    }

    partial void OnSelectedPeriodChanged(WidgetPeriodOption value)
    {
        _onPeriodChanged(value.Days);
        _ = LoadAsync();
    }

    protected override Task<System.Collections.Generic.List<Models.Track>> QueryAsync()
    {
        var since = SelectedPeriod.Days <= 0 ? DateTime.MinValue : DateTime.UtcNow.AddDays(-SelectedPeriod.Days);
        return Library.GetMostPlayedTracksAsync(24, since);
    }
}

public sealed partial class ResumeWidgetContent : ViewModelBase, IDisposable
{
    private readonly LibraryService _library;
    private readonly AudioService _audio;
    private readonly JsonSettingsService _settings;
    private readonly CoverThumbCache _thumbs;
    private Track? _track;
    private Bitmap? _ownedCover;

    public bool IsLarge { get; }

    [ObservableProperty]
    private bool _hasTrack;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _artist = "";

    [ObservableProperty]
    private Bitmap? _cover;

    [ObservableProperty]
    private string _positionText = "0:00";

    [ObservableProperty]
    private string _durationText = "0:00";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private Avalonia.Controls.GridLength _filled = new(0, Avalonia.Controls.GridUnitType.Star);

    [ObservableProperty]
    private Avalonia.Controls.GridLength _remain = new(1, Avalonia.Controls.GridUnitType.Star);

    [ObservableProperty]
    private bool _isPlaying;

    public ResumeWidgetContent(LibraryService library, AudioService audio,
                               JsonSettingsService settings, CoverThumbCache thumbs, bool isLarge)
    {
        _library = library;
        _audio = audio;
        _settings = settings;
        _thumbs = thumbs;
        IsLarge = isLarge;
        _audio.TrackChanged += OnTrackChanged;
        _audio.PositionChanged += OnPositionChanged;
        _audio.PlaybackStateChanged += OnStateChanged;
        _ = LoadAsync();
    }

    partial void OnProgressChanged(double value)
    {
        double f = Math.Clamp(value, 0, 1);
        Filled = new Avalonia.Controls.GridLength(f, Avalonia.Controls.GridUnitType.Star);
        Remain = new Avalonia.Controls.GridLength(1 - f, Avalonia.Controls.GridUnitType.Star);
    }

    private async Task LoadAsync()
    {
        var track = _audio.CurrentTrack;
        if (track == null && _settings.Current.LastTrackId is int id)
            track = await _library.GetTrackByIdAsync(id);
        if (track == null) { HasTrack = false; return; }

        double pos = _audio.State == PlaybackState.Stopped
            ? _settings.Current.LastPositionSeconds
            : _audio.Position.TotalSeconds;
        Apply(track, pos);
        await LoadCoverAsync(track);
    }

    private async Task LoadCoverAsync(Track track)
    {
        if (IsLarge)
        {
            var bmp = await _thumbs.GetTrackCoverAsync(track.Id, 400);
            var old = _ownedCover;
            _ownedCover = bmp;
            Cover = bmp;
            old?.Dispose();
        }
        else
        {
            Cover = await _thumbs.GetTrackThumbAsync(track.Id, default);
        }
    }

    private void Apply(Track track, double positionSeconds)
    {
        _track = track;
        HasTrack = true;
        Title = track.Title;
        Artist = track.Artist;
        DurationText = Format(track.DurationSeconds);
        PositionText = Format(positionSeconds);
        Progress = track.DurationSeconds > 0 ? Math.Clamp(positionSeconds / track.DurationSeconds, 0, 1) : 0;
        IsPlaying = _audio.State == PlaybackState.Playing;
    }

    private async void OnTrackChanged(object? sender, Track track)
    {
        Apply(track, _audio.Position.TotalSeconds);
        await LoadCoverAsync(track);
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (_track == null) return;
        PositionText = Format(position.TotalSeconds);
        Progress = _track.DurationSeconds > 0 ? Math.Clamp(position.TotalSeconds / _track.DurationSeconds, 0, 1) : 0;
    }

    private void OnStateChanged(object? sender, PlaybackState state) =>
        IsPlaying = state == PlaybackState.Playing;

    [RelayCommand]
    private async Task Resume()
    {
        if (_audio.State == PlaybackState.Playing || _audio.State == PlaybackState.Paused)
        {
            _audio.PlayPause();
            return;
        }
        if (_track == null) return;
        if (_audio.CurrentTrack?.Id == _track.Id)
        {
            _audio.PlayPause();
            return;
        }

        var queue = await _library.GetQueueTracksAsync(null);
        int index = queue.FindIndex(t => t.Id == _track.Id);
        if (index >= 0) _audio.SetQueue(queue, index);
        _audio.RestoreTrackState(_track, TimeSpan.FromSeconds(Math.Max(0, _settings.Current.LastPositionSeconds)));
        _audio.PlayPause();
    }

    private static string Format(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    public void Dispose()
    {
        _audio.TrackChanged -= OnTrackChanged;
        _audio.PositionChanged -= OnPositionChanged;
        _audio.PlaybackStateChanged -= OnStateChanged;
        _ownedCover?.Dispose();
        _ownedCover = null;
    }
}
