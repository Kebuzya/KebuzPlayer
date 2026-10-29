using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Services;
using KebuzPlayer.Lang;

namespace KebuzPlayer.ViewModels;

public sealed record ExplorerEntry(bool IsDirectory, string Name, string FullPath);

public partial class ExplorerPaneViewModel : ObservableObject
{
    public ObservableCollection<string> Roots { get; } = [];
    public ObservableCollection<ExplorerEntry> Entries { get; } = [];

    [ObservableProperty]
    private string? _selectedRoot;

    [ObservableProperty]
    private string _currentPath = "";

    [ObservableProperty]
    private bool _canGoUp;

    public void SetRoots(IEnumerable<string> roots)
    {
        var previous = SelectedRoot;
        Roots.Clear();
        foreach (var r in roots)
            Roots.Add(r);
        if (previous != null && Roots.Contains(previous))
            SelectedRoot = previous;
        else
            SelectedRoot = Roots.FirstOrDefault();
    }

    partial void OnSelectedRootChanged(string? value)
    {
        if (value != null)
            Navigate(value);
    }

    [RelayCommand]
    private void GoUp()
    {
        if (SelectedRoot == null) return;
        var parent = Path.GetDirectoryName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar));
        if (parent == null) return;
        if (CurrentPath.TrimEnd(Path.DirectorySeparatorChar)
                .Equals(SelectedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return;
        Navigate(parent);
    }

    public void EnterDirectory(string path) => Navigate(path);

    public void OpenRoot(string path)
    {
        if (SelectedRoot == path)
            Navigate(path);
        else
            SelectedRoot = path;
    }

    public void Refresh()
    {
        if (!string.IsNullOrEmpty(CurrentPath))
            Navigate(CurrentPath);
    }

    private void Navigate(string path)
    {
        CurrentPath = path;
        CanGoUp = SelectedRoot != null &&
            !path.TrimEnd(Path.DirectorySeparatorChar)
                 .Equals(SelectedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        Entries.Clear();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase))
                Entries.Add(new ExplorerEntry(true, Path.GetFileName(dir), dir));
            foreach (var file in Directory.EnumerateFiles(path)
                         .Where(LibraryService.IsSupportedAudio)
                         .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
                Entries.Add(new ExplorerEntry(false, Path.GetFileName(file), file));
        }
        catch { }
    }
}

public partial class FolderExplorerViewModel : ViewModelBase, IDisposable
{
    private void OnFoldersChanged()
    {
        _ = LoadRootsAsync();
        _ = LoadFoldersAsync();
    }

    public void Dispose() => LibraryNotifier.FoldersChanged -= OnFoldersChanged;

    private readonly LibraryService _library;
    private readonly AudioService _audio;
    private readonly JsonSettingsService _settings;

    public ExplorerPaneViewModel Left { get; } = new();
    public ExplorerPaneViewModel Right { get; } = new();

    public ObservableCollection<FolderRowViewModel> Folders { get; } = [];

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "";

    public FolderExplorerViewModel(LibraryService library, AudioService audio, JsonSettingsService settings)
    {
        _library = library;
        _audio = audio;
        _settings = settings;
        LibraryNotifier.FoldersChanged += OnFoldersChanged;
        _ = LoadRootsAsync();
        _ = LoadFoldersAsync();
    }

    public async Task LoadRootsAsync()
    {
        try
        {
            var folders = ApplyOrder(await _library.GetWatchedFoldersAsync());
            Left.SetRoots(folders);
            Right.SetRoots(folders);
            if (folders.Count == 0)
                StatusText = Loc.T("Explorer.NoFolders");
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Explorer.LoadFoldersError", ex.Message);
        }
    }

    private async Task LoadFoldersAsync()
    {
        try
        {
            var details = await _library.GetWatchedFolderDetailsAsync();
            var tracks = await _library.GetAllTracksAsync();
            Folders.Clear();
            foreach (var path in ApplyOrder(details.Select(d => d.Path)))
            {
                var f = details.First(d => d.Path == path);
                int count = tracks.Count(t => LibraryService.IsUnderFolder(f.Path, t.Path));
                Folders.Add(new FolderRowViewModel(_library, f, count));
            }
        }
        catch { }
    }

    private List<string> ApplyOrder(IEnumerable<string> paths)
    {
        var order = _settings.Current.FolderOrder;
        return paths
            .OrderBy(p =>
            {
                int i = order.FindIndex(o => string.Equals(o, p, StringComparison.OrdinalIgnoreCase));
                return i < 0 ? int.MaxValue : i;
            })
            .ThenBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void ReorderFolder(int from, int to)
    {
        if (from < 0 || from >= Folders.Count || from == to) return;
        var order = Folders.Select(f => f.Path).ToList();
        var moved = order[from];
        order.RemoveAt(from);
        order.Insert(Math.Clamp(to, 0, order.Count), moved);
        _settings.Current.FolderOrder = order;
        _settings.Save();
        LibraryNotifier.RaiseFoldersChanged();
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

        IsScanning = true;
        StatusText = Loc.T("Status.Scanning");
        try
        {
            await _library.AddWatchedFolderAsync(folder);
            await _library.ScanFolderAsync(folder);
            LibraryNotifier.RaiseFoldersChanged();
            StatusText = Loc.T("Explorer.FolderAdded");
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task RescanFolderAsync(FolderRowViewModel folder)
    {
        if (IsScanning) return;
        IsScanning = true;
        StatusText = Loc.T("Status.Scanning");
        try
        {
            await _library.ScanFolderAsync(folder.Path);
            LibraryNotifier.RaiseFoldersChanged();
            StatusText = Loc.T("Explorer.FolderRescanned");
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void OpenFolderInPane(string path) => Left.OpenRoot(path);

    public async Task RelocateFolderAsync(FolderRowViewModel folder)
    {
        var window = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window == null) return;

        var picked = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.F("Explorer.RelocateTitle", System.IO.Path.GetFileName(folder.Path)),
            AllowMultiple = false,
        });
        var target = picked.FirstOrDefault()?.TryGetLocalPath();
        if (target == null || target == folder.Path) return;

        try
        {
            int moved = await _library.RelocateWatchedFolderAsync(folder.Path, target);
            int at = _settings.Current.FolderOrder.FindIndex(
                p => string.Equals(p, folder.Path, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                _settings.Current.FolderOrder.RemoveAll(p => string.Equals(p, target, StringComparison.OrdinalIgnoreCase));
                at = _settings.Current.FolderOrder.FindIndex(
                    p => string.Equals(p, folder.Path, StringComparison.OrdinalIgnoreCase));
                _settings.Current.FolderOrder[at] = target;
                _settings.Save();
            }
            LibraryNotifier.RaiseFoldersChanged();
            StatusText = Loc.F("Explorer.Relocated", moved);
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public async Task RemoveFolderAsync(FolderRowViewModel folder)
    {
        try
        {
            int removed = await _library.RemoveWatchedFolderAsync(folder.Path, keepTrackData: true);
            LibraryNotifier.RaiseFoldersChanged();
            StatusText = Loc.F("Explorer.Removed", removed);
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public bool DefaultMove => _settings.Current.FileTransferMode == FileTransferMode.Move;

    public async Task TransferAsync(IReadOnlyList<string> sources, ExplorerPaneViewModel target, bool move)
    {
        if (string.IsNullOrEmpty(target.CurrentPath)) return;
        int done = 0;
        foreach (var src in sources)
        {
            try
            {
                await _library.TransferFileAsync(src, target.CurrentPath, move);
                done++;
            }
            catch (Exception ex)
            {
                StatusText = Loc.F("Common.ErrorWith", ex.Message);
            }
        }
        Left.Refresh();
        Right.Refresh();
        if (done > 0)
            StatusText = Loc.F(move ? "Explorer.Moved" : "Explorer.Copied", done);
    }

    public void PlayFile(string path)
    {
        var track = LibraryService.LoadAdHocTrack(path);
        if (track == null) return;
        _audio.SetQueue([track], 0);
        _audio.Play(track);
    }

    public async Task<List<Models.Track>> ResolveTracksAsync(IEnumerable<string> paths)
    {
        var tracks = new List<Models.Track>();
        foreach (var p in paths)
        {
            var t = await _library.GetTrackByPathAsync(p);
            if (t != null) tracks.Add(t);
        }
        return tracks;
    }

    public async Task EditTrackAsync(Models.Track track, string fileName, string title, string artist, string album)
    {
        try
        {
            var current = Path.GetFileName(track.Path);
            if (!string.IsNullOrWhiteSpace(fileName) && fileName != current)
                await _library.RenameTrackFileAsync(track, fileName);
            if (title != track.Title || artist != track.Artist || album != track.Album)
                await _library.UpdateTrackMetadataAsync(track, title, artist, album);
            Left.Refresh();
            Right.Refresh();
            LibraryNotifier.RaiseFoldersChanged();
            StatusText = Loc.T("Status.TrackUpdated");
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public async Task BatchEditTagsAsync(IReadOnlyList<Models.Track> tracks, string artist, string album)
    {
        try
        {
            int changed = await _library.UpdateTracksTagsAsync(tracks, artist, album);
            LibraryNotifier.RaiseFoldersChanged();
            StatusText = changed > 0 ? Loc.F("Status.TracksUpdated", changed) : Loc.T("Status.NoChanges");
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public async Task WriteCoversToFilesAsync(IReadOnlyList<Models.Track> tracks)
    {
        try
        {
            StatusText = LibraryViewModel.CoverWriteSummary(await _library.WriteCoversToFilesAsync(tracks));
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public async Task SetTracksHiddenAsync(IReadOnlyList<Models.Track> tracks, bool hidden)
    {
        try
        {
            foreach (var t in tracks)
            {
                await _library.SetTrackHiddenAsync(t.Id, hidden);
                t.IsHidden = hidden;
            }
            LibraryNotifier.RaiseFoldersChanged();
            StatusText = Loc.F(hidden ? "Status.TracksHidden" : "Status.TracksShown", tracks.Count);
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }

    public async Task DeleteFilesAsync(IReadOnlyList<string> paths)
    {
        int done = 0;
        foreach (var p in paths)
        {
            try
            {
                await _library.DeleteFileAsync(p);
                done++;
            }
            catch (Exception ex)
            {
                StatusText = Loc.F("Common.ErrorWith", ex.Message);
            }
        }
        Left.Refresh();
        Right.Refresh();
        if (done > 0)
        {
            LibraryNotifier.RaiseFoldersChanged();
            StatusText = Loc.F("Status.Deleted", done);
        }
    }

    public void ShowInFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("Common.ErrorWith", ex.Message);
        }
    }
}
