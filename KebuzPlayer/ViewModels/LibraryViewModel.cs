using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using KebuzPlayer.Lang;

namespace KebuzPlayer.ViewModels;

public partial class LibraryViewModel : ViewModelBase, IDisposable
{
    private void OnFoldersChanged() => _ = LoadAsync();

    private void OnVizSettingsChanged(object? sender, EventArgs e)
    {
        if (Visualizer.VisualizerHub.Instance is { } hub)
            VizStripVisible = hub.Settings.Library.Enabled;
    }

    public void Dispose()
    {
        Infrastructure.LibraryNotifier.FoldersChanged -= OnFoldersChanged;
        if (Visualizer.VisualizerHub.Instance is { } hub)
            hub.Settings.Changed -= OnVizSettingsChanged;
    }

    private readonly LibraryService _library;
    private readonly AudioService _audio;
    private readonly CoverThumbCache _thumbs;
    private readonly JsonSettingsService _settings;
    private List<Track> _all = [];

    public List<Track> SelectedTracks { get; set; } = [];

    public ObservableCollection<TrackRow> Tracks { get; } = [];

    public CoverPreviewViewModel Preview { get; }

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _vizStripVisible;

    [ObservableProperty]
    private TrackViewMode _viewMode;

    [ObservableProperty]
    private TrackSortOption _sortOption;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private bool _showHidden;

    public IReadOnlyList<TrackSortOption> SortOptions => TrackSortOptions.Library;

    public bool IsTableMode   => ViewMode == TrackViewMode.Table;
    public bool IsGridMode    => ViewMode == TrackViewMode.Grid;
    public bool IsCompactMode => ViewMode == TrackViewMode.Compact;

    public LibraryViewModel(LibraryService library, AudioService audio, JsonSettingsService settings,
                            CoverThumbCache thumbs)
    {
        _library = library;
        _audio = audio;
        _thumbs = thumbs;
        _settings = settings;
        _viewMode = settings.Current.LibraryViewMode;
        _sortDescending = settings.Current.LibrarySortDescending;
        _sortOption = TrackSortOptions.Library.FirstOrDefault(o => o.Mode == settings.Current.LibrarySortMode)
                      ?? TrackSortOptions.Library[0];
        Preview = new CoverPreviewViewModel(library, settings);
        Preview.CoverSaved += (_, track) =>
        {
            _thumbs.InvalidateTrack(track.Id);
            Infrastructure.LibraryNotifier.RaiseCoverChanged(track.Id);
        };
        if (Visualizer.VisualizerHub.Instance is { } hub)
        {
            _vizStripVisible = hub.Settings.Library.Enabled;
            hub.Settings.Changed += OnVizSettingsChanged;
        }
        Infrastructure.LibraryNotifier.FoldersChanged += OnFoldersChanged;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var (tracks, detached) = await _library.GetValidTracksAsync();
            _all = tracks;
            ApplyFilter();
            StatusText = detached > 0
                ? Loc.F("Library.StatusDetached", _all.Count, detached)
                : Loc.F("Library.StatusCount", _all.Count);
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.LoadErrorWith", ex.Message);
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnViewModeChanged(TrackViewMode value)
    {
        OnPropertyChanged(nameof(IsTableMode));
        OnPropertyChanged(nameof(IsGridMode));
        OnPropertyChanged(nameof(IsCompactMode));
        _settings.Current.LibraryViewMode = value;
        _settings.Save();
    }

    [RelayCommand]
    private void SetViewMode(string mode) => ViewMode = Enum.Parse<TrackViewMode>(mode);

    partial void OnSortOptionChanged(TrackSortOption value)
    {
        _settings.Current.LibrarySortMode = value.Mode;
        _settings.Save();
        ApplyFilter();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        _settings.Current.LibrarySortDescending = value;
        _settings.Save();
        ApplyFilter();
    }

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

    partial void OnShowHiddenChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private void ToggleShowHidden() => ShowHidden = !ShowHidden;

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        IEnumerable<Track> source = ShowHidden ? _all : _all.Where(t => !t.IsHidden);
        var filtered = query.Length == 0
            ? source
            : source.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Album.Contains(query, StringComparison.OrdinalIgnoreCase));

        var sorted = TrackSorter.Sort(filtered, SortOption.Mode, SortDescending);

        Tracks.Clear();
        int number = 1;
        foreach (var t in sorted)
            Tracks.Add(new TrackRow(t, number++, _thumbs));
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (IsScanning) return;

        var window = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window == null) return;

        var picked = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.T("Folder.PickerTitle"),
            AllowMultiple = false,
        });
        var folder = picked.FirstOrDefault()?.TryGetLocalPath();
        if (folder == null) return;

        await _library.AddWatchedFolderAsync(folder);
        await ScanAsync([folder]);
        Infrastructure.LibraryNotifier.RaiseFoldersChanged();
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        if (IsScanning) return;
        var folders = await _library.GetWatchedFoldersAsync();
        if (folders.Count == 0)
        {
            StatusText = Loc.T("Library.NoFolders");
            return;
        }
        await ScanAsync(folders);
    }

    private async Task ScanAsync(IReadOnlyList<string> folders)
    {
        IsScanning = true;
        try
        {
            foreach (var folder in folders)
            {
                var progress = new Progress<(int done, int total)>(p =>
                    StatusText = Loc.F("Library.Scanning", p.done, p.total));
                await _library.ScanFolderAsync(folder, progress);
            }
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Library.ScanError", ex.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool _coversRunning;

    [RelayCommand]
    private async Task GenerateCoversAsync()
    {
        if (_coversRunning) return;
        _coversRunning = true;
        try
        {
            var progress = new Progress<(int done, int total)>(p =>
                StatusText = Loc.F("Library.CoversProgress", _all.Count, p.done, p.total));
            int generated = await _library.GenerateMissingCoversAsync(progress);
            if (generated > 0) await ReloadCoversAsync();
            StatusText = generated > 0
                ? Loc.F("Library.CoversDone", _all.Count, generated)
                : Loc.F("Library.CoversAllPresent", _all.Count);
        }
        catch { }
        finally
        {
            _coversRunning = false;
        }
    }

    private async Task ReloadCoversAsync()
    {
        foreach (var track in _all) _thumbs.InvalidateTrack(track.Id);
        await LoadAsync();
    }

    private bool _regenerateSelectedRunning;

    [RelayCommand]
    private async Task RegenerateSelectedAsync()
    {
        if (_regenerateSelectedRunning) return;
        if (SelectedTracks.Count == 0) { StatusText = Loc.T("Status.NothingSelected"); return; }
        _regenerateSelectedRunning = true;
        try
        {
            var ids = SelectedTracks.Select(t => t.Id).ToList();
            var progress = new Progress<(int done, int total)>(p =>
                StatusText = Loc.F("Library.Regenerating", p.done, p.total));
            int regenerated = await _library.GenerateCoversForAsync(ids, forceRegenerate: true, progress);
            foreach (int id in ids) _thumbs.InvalidateTrack(id);
            await LoadAsync();
            StatusText = Loc.F("Library.Regenerated", regenerated);
        }
        catch { }
        finally
        {
            _regenerateSelectedRunning = false;
        }
    }

    public async Task EditTrackAsync(Track track, string fileName, string title, string artist, string album)
    {
        try
        {
            var current = System.IO.Path.GetFileName(track.Path);
            if (!string.IsNullOrWhiteSpace(fileName) && fileName != current)
                await _library.RenameTrackFileAsync(track, fileName);

            if (title != track.Title || artist != track.Artist || album != track.Album)
                await _library.UpdateTrackMetadataAsync(track, title, artist, album);

            ApplyFilter();
            StatusText = Loc.T("Status.TrackUpdated");
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public async Task BatchEditTagsAsync(IReadOnlyList<Track> tracks, string artist, string album)
    {
        try
        {
            int changed = await _library.UpdateTracksTagsAsync(tracks, artist, album);
            ApplyFilter();
            StatusText = changed > 0 ? Loc.F("Status.TracksUpdated", changed) : Loc.T("Status.NoChanges");
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public async Task SetTracksHiddenAsync(IReadOnlyList<Track> tracks, bool hidden)
    {
        try
        {
            foreach (var t in tracks)
            {
                await _library.SetTrackHiddenAsync(t.Id, hidden);
                t.IsHidden = hidden;
            }
            ApplyFilter();
            StatusText = Loc.F(hidden ? "Status.TracksHidden" : "Status.TracksShown", tracks.Count);
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public bool DefaultMove => _settings.Current.FileTransferMode == FileTransferMode.Move;

    public Task<List<string>> GetWatchedFoldersAsync() => _library.GetWatchedFoldersAsync();

    public async Task DropFilesAsync(IReadOnlyList<string> files, string targetFolder, bool move)
    {
        if (string.IsNullOrEmpty(targetFolder)) return;
        int done = 0;
        foreach (var src in files)
        {
            try
            {
                await _library.TransferFileAsync(src, targetFolder, move);
                var destPath = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(src));
                if (await _library.GetTrackByPathAsync(destPath) == null)
                    await _library.AddFileAsync(destPath);
                done++;
            }
            catch (Exception ex)
            {
                StatusText = Loc.F("Common.ErrorWith", ex.Message);
            }
        }
        if (done > 0)
        {
            await LoadAsync();
            StatusText = Loc.F(move ? "Library.MovedIn" : "Library.AddedIn", done);
        }
    }

    public async Task DeleteTracksAsync(IReadOnlyList<Track> tracks)
    {
        try
        {
            foreach (var t in tracks)
                await _library.DeleteTrackFileAsync(t);
            _all = _all.Where(t => !tracks.Contains(t)).ToList();
            ApplyFilter();
            StatusText = Loc.F("Status.Deleted", tracks.Count);
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Library.DeleteError", ex.Message);
        }
    }

    public async void AdjustTrackGain(Track track, float delta)
    {
        _audio.AdjustTrackGain(track, delta);
        try { await _library.UpdateTrackGainAsync(track.Id, track.UserGainOffset); }
        catch { }
    }

    public Task<List<Playlist>> GetPlaylistsAsync() => _library.GetPlaylistsAsync();

    public async Task AddToPlaylistAsync(int playlistId, IReadOnlyList<Track> tracks)
    {
        try
        {
            int added = await _library.AddTracksToPlaylistAsync(playlistId, tracks.Select(t => t.Id));
            StatusText = added > 0 ? Loc.F("Library.AddedToPlaylist", added) : Loc.T("Library.AlreadyInPlaylist");
            if (added > 0)
                _ = RegenPlaylistCoverAsync(playlistId);
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public async Task CreatePlaylistWithTracksAsync(string name, IReadOnlyList<Track> tracks)
    {
        try
        {
            int id = await _library.CreatePlaylistAsync(name);
            int added = await _library.AddTracksToPlaylistAsync(id, tracks.Select(t => t.Id));
            StatusText = Loc.F("Library.NewPlaylistAdded", name.Trim(), added);
            _ = RegenPlaylistCoverAsync(id);
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
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
    private void PlayTrack(TrackRow? row)
    {
        if (row == null) return;
        var queue = Tracks.Select(r => r.Track).ToList();
        var index = queue.IndexOf(row.Track);
        if (index < 0) return;
        _audio.SetQueue(queue, index);
        _audio.Play(row.Track);
    }
}
