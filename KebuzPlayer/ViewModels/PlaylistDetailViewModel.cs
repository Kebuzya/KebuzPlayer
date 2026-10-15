using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using KebuzPlayer.Lang;

namespace KebuzPlayer.ViewModels;

public partial class PickerItem : ObservableObject
{
    private readonly Action _onToggle;

    public Track Track { get; }

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => _onToggle();

    public PickerItem(Track track, Action onToggle)
    {
        Track = track;
        _onToggle = onToggle;
    }
}

public partial class PlaylistDetailViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly AudioService _audio;
    private readonly JsonSettingsService _settings;
    private readonly CoverThumbCache _thumbs;
    private readonly Action _onClose;
    private bool _regenRunning;

    public int PlaylistId { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _countText = "";

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<TrackRow> Rows { get; } = [];

    public List<Track> SelectedTracks { get; set; } = [];

    [ObservableProperty]
    private TrackViewMode _viewMode;

    [ObservableProperty]
    private TrackSortOption _sortOption;

    [ObservableProperty]
    private bool _sortDescending;

    public IReadOnlyList<TrackSortOption> SortOptions => TrackSortOptions.Playlist;

    public bool IsManualSort => SortOption.Mode == TrackSortMode.Manual;

    public bool IsTableMode   => ViewMode == TrackViewMode.Table;
    public bool IsGridMode    => ViewMode == TrackViewMode.Grid;
    public bool IsCompactMode => ViewMode == TrackViewMode.Compact;

    public PlaylistDetailViewModel(LibraryService library, AudioService audio,
                                   JsonSettingsService settings, CoverThumbCache thumbs,
                                   PlaylistCardViewModel card, Action onClose)
    {
        _library = library;
        _audio = audio;
        _settings = settings;
        _thumbs = thumbs;
        _onClose = onClose;
        PlaylistId = card.Id;
        _name = card.Name;
        _viewMode = settings.Current.PlaylistViewModes.TryGetValue(card.Id, out var mode)
            ? mode
            : TrackViewMode.Table;
        _sortDescending = settings.Current.PlaylistSortDescending.TryGetValue(card.Id, out var desc) && desc;
        var sortMode = settings.Current.PlaylistSortModes.TryGetValue(card.Id, out var sm)
            ? sm
            : TrackSortMode.Manual;
        _sortOption = TrackSortOptions.Playlist.FirstOrDefault(o => o.Mode == sortMode)
                      ?? TrackSortOptions.Playlist[0];
        _ = ReloadAsync();
    }

    partial void OnViewModeChanged(TrackViewMode value)
    {
        OnPropertyChanged(nameof(IsTableMode));
        OnPropertyChanged(nameof(IsGridMode));
        OnPropertyChanged(nameof(IsCompactMode));
        _settings.Current.PlaylistViewModes[PlaylistId] = value;
        _settings.Save();
    }

    partial void OnSortOptionChanged(TrackSortOption value)
    {
        OnPropertyChanged(nameof(IsManualSort));
        _settings.Current.PlaylistSortModes[PlaylistId] = value.Mode;
        _settings.Save();
        _ = ReloadAsync();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        _settings.Current.PlaylistSortDescending[PlaylistId] = value;
        _settings.Save();
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

    [RelayCommand]
    private void SetViewMode(string mode) => ViewMode = Enum.Parse<TrackViewMode>(mode);

    public async Task ReloadAsync()
    {
        try
        {
            var tracks = await _library.GetPlaylistTracksAsync(PlaylistId);
            if (SortOption.Mode != TrackSortMode.Manual)
                tracks = TrackSorter.Sort(tracks, SortOption.Mode, SortDescending).ToList();
            Rows.Clear();
            int number = 1;
            foreach (var t in tracks)
                Rows.Add(new TrackRow(t, number++, _thumbs));
            CountText = Loc.Tracks(Rows.Count);
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.LoadErrorWith", ex.Message);
        }
    }

    [RelayCommand]
    private void Back() => _onClose();

    [RelayCommand]
    private void PlayTrack(TrackRow? row)
    {
        if (row == null) return;
        var queue = Rows.Select(r => r.Track).ToList();
        var index = queue.IndexOf(row.Track);
        if (index < 0) return;
        _audio.SetQueue(queue, index);
        _audio.Play(row.Track);
    }

    [RelayCommand]
    private void PlayAll()
    {
        if (Rows.Count > 0)
            PlayTrack(Rows[0]);
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        if (SelectedTracks.Count == 0)
        {
            StatusText = Loc.T("Status.NothingSelected");
            return;
        }
        try
        {
            foreach (var t in SelectedTracks.ToList())
                await _library.RemoveTrackFromPlaylistAsync(PlaylistId, t.Id);
            StatusText = Loc.F("Playlist.Removed", SelectedTracks.Count);
            await ReloadAsync();
            RegenerateCover();
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public ObservableCollection<PickerItem> PickerItems { get; } = [];
    private List<PickerItem> _pickerAll = [];

    [ObservableProperty]
    private string _pickerSearch = "";

    [ObservableProperty]
    private int _pickerCheckedCount;

    public string PickerAddText => Loc.F("Playlist.PickerAdd", PickerCheckedCount);

    partial void OnPickerCheckedCountChanged(int value) => OnPropertyChanged(nameof(PickerAddText));

    partial void OnPickerSearchChanged(string value) => ApplyPickerFilter();

    public async Task LoadPickerAsync()
    {
        try
        {
            var all = await _library.GetAllTracksAsync();
            var inList = Rows.Select(r => r.Track.Id).ToHashSet();
            _pickerAll = all.Where(t => !inList.Contains(t.Id))
                            .Select(t => new PickerItem(t, UpdatePickerCount))
                            .ToList();
            PickerSearch = "";
            PickerCheckedCount = 0;
            ApplyPickerFilter();
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.LoadErrorWith", ex.Message);
        }
    }

    private void ApplyPickerFilter()
    {
        var query = PickerSearch.Trim();
        var filtered = query.Length == 0
            ? _pickerAll
            : _pickerAll.Where(p =>
                p.Track.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Track.Artist.Contains(query, StringComparison.OrdinalIgnoreCase));
        PickerItems.Clear();
        foreach (var p in filtered)
            PickerItems.Add(p);
    }

    private void UpdatePickerCount() => PickerCheckedCount = _pickerAll.Count(p => p.IsChecked);

    [RelayCommand]
    private async Task ConfirmPickerAsync()
    {
        var chosen = _pickerAll.Where(p => p.IsChecked).Select(p => p.Track.Id).ToList();
        if (chosen.Count == 0) return;
        try
        {
            int added = await _library.AddTracksToPlaylistAsync(PlaylistId, chosen);
            StatusText = Loc.F("Playlist.Added", added);
            await ReloadAsync();
            RegenerateCover();
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public void MoveRow(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Rows.Count || to >= Rows.Count) return;
        Rows.Move(from, to);
        for (int i = 0; i < Rows.Count; i++)
            Rows[i].Number = i + 1;
    }

    public async Task CommitOrderAsync()
    {
        try
        {
            await _library.SavePlaylistOrderAsync(PlaylistId, Rows.Select(r => r.Track.Id).ToList());
            RegenerateCover();
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Playlist.OrderSaveError", ex.Message);
        }
    }

    public void RegenerateCover() => _ = RegenerateCoverAsync();

    private async Task RegenerateCoverAsync()
    {
        if (_regenRunning) return;
        _regenRunning = true;
        try
        {
            await _library.RegeneratePlaylistCoverAsync(PlaylistId);
            _thumbs.InvalidatePlaylist(PlaylistId);
        }
        catch { }
        finally
        {
            _regenRunning = false;
        }
    }
}
