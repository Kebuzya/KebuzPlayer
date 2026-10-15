using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using NAudio.Wave;
using KebuzPlayer.Lang;

namespace KebuzPlayer.ViewModels;

public partial class PlayerViewModel : ViewModelBase
{
    private const string QueueSettingKey = "LastQueue";
    private readonly AudioService _audio;
    private readonly LibraryService _library;
    private readonly JsonSettingsService _settings;
    private readonly Infrastructure.CoverThumbCache _thumbs;
    private bool _updatingFromPlayback;
    private bool _restoringQueue;
    private DispatcherTimer? _errorTimer;

    [ObservableProperty]
    private string _title = Loc.T("Player.NoTrack");

    [ObservableProperty]
    private string _artist = "KebuzPlayer";

    [ObservableProperty]
    private bool _hasTrack;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private int _volumePercent;

    [ObservableProperty]
    private bool _shuffleActive;

    [ObservableProperty]
    private bool _repeatActive;

    [ObservableProperty]
    private bool _repeatIsOne;

    [ObservableProperty]
    private string _gainText = Loc.F("Player.Gain", 0f.ToString("+0.0;-0.0;+0.0"));

    [ObservableProperty]
    private Bitmap? _coverImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanHideTrack))]
    private bool _isTrackInLibrary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanHideTrack))]
    private bool _isTrackHidden;

    public bool CanHideTrack => IsTrackInLibrary && !IsTrackHidden;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorText;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public CoverPreviewViewModel Preview { get; }

    public string PositionText => FormatTime(PositionSeconds);
    public string DurationText => FormatTime(DurationSeconds);

    public PlayerViewModel(AudioService audio, LibraryService library, JsonSettingsService settings,
                           Infrastructure.CoverThumbCache thumbs)
    {
        _audio = audio;
        _library = library;
        _settings = settings;
        _thumbs = thumbs;
        Preview = new CoverPreviewViewModel(library, settings);

        _volumePercent = (int)Math.Round(Math.Clamp(settings.Current.MasterVolume, 0f, 1f) * 100);
        _audio.SetMasterVolume(settings.Current.MasterVolume);
        _audio.SetShuffle(settings.Current.IsShuffleOn);
        _audio.SetRepeatMode(settings.Current.RepeatMode);
        _shuffleActive = settings.Current.IsShuffleOn;
        UpdateRepeatDisplay();

        _audio.TrackChanged += OnTrackChanged;
        _audio.PlaybackStateChanged += OnPlaybackStateChanged;
        _audio.PositionChanged += OnPositionChanged;
        _audio.GainChanged += (_, _) => UpdateGainDisplay();
        _audio.TrackOpenFailed += OnTrackOpenFailed;
        _audio.QueueChanged += (_, _) => SaveQueue();
        Preview.CoverSaved += (_, track) =>
        {
            _thumbs.InvalidateTrack(track.Id);
            if (_audio.CurrentTrack?.Id == track.Id)
                LoadCover(track.Id);
        };

        RestoreLastTrack();
    }

    private async void RestoreLastTrack()
    {
        if (_settings.Current.LastTrackId is not int id) return;
        try
        {
            var track = await _library.GetTrackByIdAsync(id);
            if (track == null || _audio.CurrentTrack != null) return;

            List<int>? savedIds = null;
            if (await _library.GetSettingAsync(QueueSettingKey) is { Length: > 0 } json)
            {
                try { savedIds = JsonSerializer.Deserialize<List<int>>(json); }
                catch (JsonException) { }
            }
            var queue = await _library.GetQueueTracksAsync(savedIds);
            var index = queue.FindIndex(t => t.Id == id);
            if (index >= 0)
            {
                track = queue[index];
                _restoringQueue = true;
                _audio.SetQueue(queue, index);
                _restoringQueue = false;
            }

            _audio.RestoreTrackState(track, TimeSpan.FromSeconds(Math.Max(0, _settings.Current.LastPositionSeconds)));
            ShowTrack(track);
        }
        catch { }
    }

    private void SaveQueue()
    {
        if (_restoringQueue) return;
        var ids = new List<int>();
        foreach (var t in _audio.Queue)
            if (t.Id > 0) ids.Add(t.Id);
        _ = SaveQueueAsync(JsonSerializer.Serialize(ids));
    }

    private async Task SaveQueueAsync(string json)
    {
        try { await _library.SetSettingAsync(QueueSettingKey, json); }
        catch { }
    }

    private void OnTrackChanged(object? sender, Track track)
    {
        ShowTrack(track);
        if (track.Id > 0)
        {
            _settings.Current.LastTrackId = track.Id;
            _ = _library.IncrementPlayCountAsync(track.Id);
            _ = _library.AddPlayHistoryAsync(track.Id);
        }
    }

    private void OnTrackOpenFailed(object? sender, Track track) =>
        ShowError(Loc.F("Player.OpenFailed", Path.GetFileName(track.Path)));

    public void ShowError(string text)
    {
        ErrorText = text;
        _errorTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _errorTimer.Tick -= ClearError;
        _errorTimer.Tick += ClearError;
        _errorTimer.Stop();
        _errorTimer.Start();
    }

    private void ClearError(object? sender, EventArgs e)
    {
        _errorTimer?.Stop();
        ErrorText = null;
    }

    private void ShowTrack(Track track)
    {
        Title = track.Title;
        Artist = track.Artist;
        HasTrack = true;
        _updatingFromPlayback = true;
        PositionSeconds = 0;
        DurationSeconds = track.DurationSeconds;
        _updatingFromPlayback = false;
        OnPropertyChanged(nameof(DurationText));
        UpdateGainDisplay();
        IsTrackInLibrary = track.Id > 0;
        IsTrackHidden = track.IsHidden;
        LoadCover(track.Id);
    }

    private async void LoadCover(int trackId)
    {
        try
        {
            var bytes = await _library.GetCoverArtAsync(trackId);
            if (_audio.CurrentTrack?.Id != trackId) return;

            var old = CoverImage;
            if (bytes is { Length: > 0 })
            {
                using var ms = new MemoryStream(bytes);
                CoverImage = new Bitmap(ms);
            }
            else
            {
                CoverImage = null;
            }
            old?.Dispose();
        }
        catch
        {
            CoverImage = null;
        }
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackState state)
    {
        IsPlaying = state == PlaybackState.Playing;
        if (state == PlaybackState.Paused && _audio.CurrentTrack != null)
            _settings.Current.LastPositionSeconds = _audio.Position.TotalSeconds;
        if (_audio.Duration > TimeSpan.Zero)
        {
            _updatingFromPlayback = true;
            DurationSeconds = _audio.Duration.TotalSeconds;
            _updatingFromPlayback = false;
            OnPropertyChanged(nameof(DurationText));
        }
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        _updatingFromPlayback = true;
        PositionSeconds = position.TotalSeconds;
        _updatingFromPlayback = false;
    }

    partial void OnPositionSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(PositionText));
        if (!_updatingFromPlayback && HasTrack)
            _audio.SeekTo(TimeSpan.FromSeconds(value));
    }

    partial void OnVolumePercentChanged(int value)
    {
        var volume = Math.Clamp(value, 0, 100) / 100f;
        _audio.SetMasterVolume(volume);
        _settings.Current.MasterVolume = volume;
    }

    public Track? CurrentTrack => _audio.CurrentTrack;

    public async Task EditCurrentTrackAsync(string fileName, string title, string artist, string album)
    {
        var track = _audio.CurrentTrack;
        if (track == null) return;

        try
        {
            var current = Path.GetFileName(track.Path);
            if (!string.IsNullOrWhiteSpace(fileName) && fileName != current)
                await _library.RenameTrackFileAsync(track, fileName);
            if (title != track.Title || artist != track.Artist || album != track.Album)
                await _library.UpdateTrackMetadataAsync(track, title, artist, album);
        }
        catch (Exception ex)
        {
            ShowError(Loc.F("Player.EditFailed", ex.Message));
        }

        ShowTrack(track);
        Infrastructure.LibraryNotifier.RaiseFoldersChanged();
    }

    public async Task SetCurrentTrackHiddenAsync(bool hidden)
    {
        var track = _audio.CurrentTrack;
        if (track == null || track.Id <= 0) return;
        await _library.SetTrackHiddenAsync(track.Id, hidden);
        track.IsHidden = hidden;
        IsTrackHidden = hidden;
        Infrastructure.LibraryNotifier.RaiseFoldersChanged();
    }

    public async Task AddCurrentTrackToLibraryAsync()
    {
        var track = _audio.CurrentTrack;
        if (track == null || track.Id > 0) return;

        var added = await _library.AddFileAsync(track.Path);
        if (added is not { Id: > 0 }) return;

        track.Id = added.Id;
        track.IsHidden = added.IsHidden;
        track.ReplayGainTarget = added.ReplayGainTarget;
        track.UserGainOffset = added.UserGainOffset;
        _settings.Current.LastTrackId = track.Id;
        IsTrackInLibrary = true;
        IsTrackHidden = track.IsHidden;

        await _library.IncrementPlayCountAsync(track.Id);
        await _library.AddPlayHistoryAsync(track.Id);
        LoadCover(track.Id);
        Infrastructure.LibraryNotifier.RaiseFoldersChanged();
    }

    public void ShowCurrentTrackInFolder()
    {
        var track = _audio.CurrentTrack;
        if (track == null) return;
        try
        {
            if (System.IO.File.Exists(track.Path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", $"/select,\"{track.Path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    public async Task DeleteCurrentTrackAsync()
    {
        var track = _audio.CurrentTrack;
        if (track == null) return;
        try
        {
            await _library.DeleteTrackFileAsync(track);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return;
        }
        Infrastructure.LibraryNotifier.RaiseFoldersChanged();
    }

    public Task<System.Collections.Generic.List<Playlist>> GetPlaylistsAsync() =>
        _library.GetPlaylistsAsync();

    public async Task AddCurrentTrackToPlaylistAsync(int playlistId)
    {
        var track = _audio.CurrentTrack;
        if (track == null) return;
        int added = await _library.AddTracksToPlaylistAsync(playlistId, [track.Id]);
        if (added > 0) await RegenPlaylistCoverAsync(playlistId);
    }

    public async Task CreatePlaylistWithCurrentTrackAsync(string name)
    {
        var track = _audio.CurrentTrack;
        if (track == null) return;
        int id = await _library.CreatePlaylistAsync(name);
        await _library.AddTracksToPlaylistAsync(id, [track.Id]);
        await RegenPlaylistCoverAsync(id);
    }

    private async Task RegenPlaylistCoverAsync(int playlistId)
    {
        try
        {
            await _library.RegeneratePlaylistCoverAsync(playlistId);
            _thumbs.InvalidatePlaylist(playlistId);
        }
        catch { }
    }

    [RelayCommand]
    private void OpenPreview() => Preview.LoadCommand.Execute(_audio.CurrentTrack);

    [RelayCommand]
    private void PlayPause() => _audio.PlayPause();

    [RelayCommand]
    private void Next() => _audio.Next();

    [RelayCommand]
    private void Previous() => _audio.Previous();

    [RelayCommand]
    private void ToggleShuffle()
    {
        _audio.ToggleShuffle();
        ShuffleActive = _audio.IsShuffleOn;
        _settings.Current.IsShuffleOn = _audio.IsShuffleOn;
    }

    [RelayCommand]
    private void CycleRepeat()
    {
        _audio.CycleRepeat();
        _settings.Current.RepeatMode = _audio.RepeatMode;
        UpdateRepeatDisplay();
    }

    [RelayCommand]
    private void GainUp() => AdjustGain(0.5f);

    [RelayCommand]
    private void GainDown() => AdjustGain(-0.5f);

    private async void AdjustGain(float delta)
    {
        var track = _audio.CurrentTrack;
        if (track == null) return;

        _audio.AdjustTrackGain(track, delta);
        try { await _library.UpdateTrackGainAsync(track.Id, track.UserGainOffset); }
        catch { }
    }

    private void UpdateGainDisplay()
    {
        var gain = _audio.CurrentTrack?.UserGainOffset ?? 0f;
        GainText = Loc.F("Player.Gain", gain.ToString("+0.0;-0.0;+0.0"));
    }

    public void RefreshTexts()
    {
        if (!HasTrack) Title = Loc.T("Player.NoTrack");
        UpdateGainDisplay();
    }

    private void UpdateRepeatDisplay()
    {
        RepeatActive = _audio.RepeatMode != RepeatMode.None;
        RepeatIsOne = _audio.RepeatMode == RepeatMode.RepeatOne;
    }

    private static string FormatTime(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}
