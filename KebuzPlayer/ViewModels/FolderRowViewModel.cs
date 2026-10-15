using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using KebuzPlayer.Lang;

namespace KebuzPlayer.ViewModels;

public partial class FolderRowViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private bool _suppress;

    public string Path { get; }
    public int TrackCount { get; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _sizeText = "…";

    public string ScanText { get; }
    public bool IsMissing { get; }
    public string MissingText => Loc.T("Folder.Missing");
    public string CountText => Loc.Tracks(TrackCount);

    public FolderRowViewModel(LibraryService library, WatchedFolder folder, int trackCount)
    {
        _library = library;
        Path = folder.Path;
        TrackCount = trackCount;
        _suppress = true;
        _isActive = folder.IsActive;
        _suppress = false;
        IsMissing = !System.IO.Directory.Exists(folder.Path);
        ScanText = folder.LastScanned is { } dt
            ? Loc.F("Folder.Scanned", dt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))
            : Loc.T("Folder.NeverScanned");
        _ = LoadSizeAsync();
    }

    private async Task LoadSizeAsync()
    {
        var bytes = await LibraryService.GetFolderSizeAsync(Path);
        SizeText = FormatSize(bytes);
    }

    partial void OnIsActiveChanged(bool value)
    {
        if (_suppress) return;
        _ = ApplyActiveAsync(value);
    }

    private async Task ApplyActiveAsync(bool value)
    {
        await _library.SetWatchedFolderActiveAsync(Path, value);
        LibraryNotifier.RaiseFoldersChanged();
    }

    private static string FormatSize(long bytes)
    {
        string[] units = [Loc.T("Unit.B"), Loc.T("Unit.KB"), Loc.T("Unit.MB"), Loc.T("Unit.GB"), Loc.T("Unit.TB")];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} {units[0]}" : $"{size:0.#} {units[unit]}";
    }
}
