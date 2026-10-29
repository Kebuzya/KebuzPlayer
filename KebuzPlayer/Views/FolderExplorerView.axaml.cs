using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using KebuzPlayer.Services;
using KebuzPlayer.ViewModels;
using KebuzPlayer.Lang;

namespace KebuzPlayer.Views;

public partial class FolderExplorerView : UserControl
{
    private static readonly DataFormat<List<string>> PathsFormat =
        DataFormat.CreateInProcessFormat<List<string>>("kebuz-explorer-files");

    private ListBox? _pressList;
    private PointerPressedEventArgs? _pressArgs;
    private Point _pressPoint;
    private bool _dragging;

    private bool _folderArmed;
    private bool _folderDragging;
    private Point _folderStart;
    private int _folderIndex = -1;

    public FolderExplorerView()
    {
        InitializeComponent();
        WireList(LeftList);
        WireList(RightList);
        FoldersList.AddHandler(PointerPressedEvent, Folders_PointerPressed, RoutingStrategies.Tunnel);
        FoldersList.AddHandler(PointerMovedEvent, Folders_PointerMoved, RoutingStrategies.Tunnel);
        FoldersList.AddHandler(PointerReleasedEvent, Folders_PointerReleased, RoutingStrategies.Tunnel);
    }

    private void Folders_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(FoldersList).Properties.IsLeftButtonPressed) return;
        var src = e.Source as Visual;
        if (src?.FindAncestorOfType<Button>(includeSelf: true) != null ||
            src?.FindAncestorOfType<CheckBox>(includeSelf: true) != null) return;
        var item = src?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (item == null) return;
        _folderIndex = FoldersList.IndexFromContainer(item);
        if (_folderIndex < 0) return;
        _folderArmed = true;
        _folderStart = e.GetPosition(FoldersList);
    }

    private void Folders_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_folderArmed) return;
        var pos = e.GetPosition(FoldersList);
        if (!_folderDragging)
        {
            if (Math.Abs(pos.Y - _folderStart.Y) < 5 && Math.Abs(pos.X - _folderStart.X) < 5) return;
            _folderDragging = true;
            e.Pointer.Capture(FoldersList);
        }
        var (_, y) = FolderInsertAt(pos);
        if (double.IsNaN(y))
            FolderDropIndicator.IsVisible = false;
        else
        {
            FolderDropIndicator.IsVisible = true;
            FolderDropIndicator.Margin = new Thickness(0, Math.Max(0, y - 1), 0, 0);
        }
    }

    private void Folders_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_folderArmed) return;
        bool wasDragging = _folderDragging;
        _folderArmed = false;
        _folderDragging = false;
        FolderDropIndicator.IsVisible = false;
        e.Pointer.Capture(null);
        if (!wasDragging || _folderIndex < 0 || DataContext is not FolderExplorerViewModel vm) return;
        int insert = FolderInsertAt(e.GetPosition(FoldersList)).Insert;
        int from = _folderIndex;
        _folderIndex = -1;
        if (insert < 0) return;
        int to = insert > from ? insert - 1 : insert;
        vm.ReorderFolder(from, to);
    }

    private (int Insert, double Y) FolderInsertAt(Point pos)
    {
        if (DataContext is not FolderExplorerViewModel vm || vm.Folders.Count == 0) return (-1, double.NaN);
        int insert = -1;
        double y = double.NaN;
        for (int i = 0; i < vm.Folders.Count; i++)
        {
            if (FoldersList.ContainerFromIndex(i) is not ListBoxItem c || !c.IsVisible) continue;
            var top = c.TranslatePoint(new Point(0, 0), FoldersList);
            if (top == null) continue;
            double t = top.Value.Y;
            double h = c.Bounds.Height;
            if (pos.Y < t + h / 2)
                return (i, t);
            insert = i + 1;
            y = t + h;
        }
        return (insert, y);
    }

    private void WireList(ListBox list)
    {
        list.AddHandler(PointerPressedEvent, List_PointerPressed, RoutingStrategies.Tunnel);
        list.AddHandler(PointerMovedEvent, List_PointerMoved, RoutingStrategies.Tunnel);
        list.DoubleTapped += List_DoubleTapped;
        DragDrop.SetAllowDrop(list, true);
        list.AddHandler(DragDrop.DragOverEvent, List_DragOver);
        list.AddHandler(DragDrop.DropEvent, List_Drop);
    }

    private void List_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list) return;
        if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;
        _pressList = list;
        _pressArgs = e;
        _pressPoint = e.GetPosition(list);
        _dragging = false;
    }

    private async void List_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging || _pressList == null || _pressArgs == null || sender != _pressList) return;
        if (!e.GetCurrentPoint(_pressList).Properties.IsLeftButtonPressed) { Reset(); return; }
        var pos = e.GetPosition(_pressList);
        if (Math.Abs(pos.X - _pressPoint.X) < 5 && Math.Abs(pos.Y - _pressPoint.Y) < 5) return;

        var files = _pressList.SelectedItems!.Cast<ExplorerEntry>()
            .Where(x => !x.IsDirectory).Select(x => x.FullPath).ToList();
        if (files.Count == 0) { Reset(); return; }

        _dragging = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(PathsFormat, files));
        try
        {
            await DragDrop.DoDragDropAsync(_pressArgs, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch { }
        finally
        {
            Reset();
        }
    }

    private void Reset()
    {
        _dragging = false;
        _pressList = null;
        _pressArgs = null;
    }

    private void List_DragOver(object? sender, DragEventArgs e)
    {
        bool hasPayload = e.DataTransfer.Contains(PathsFormat) || e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasPayload ? EffectFor(e) : DragDropEffects.None;
        e.Handled = true;
    }

    private async void List_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is not ListBox { DataContext: ExplorerPaneViewModel target } ||
            DataContext is not FolderExplorerViewModel vm)
            return;

        bool move = EffectFor(e) == DragDropEffects.Move;

        if (e.DataTransfer.TryGetValue(PathsFormat) is { Count: > 0 } internalFiles)
        {
            await vm.TransferAsync(internalFiles, target, move);
            return;
        }

        var dropped = e.DataTransfer.TryGetFiles();
        if (dropped == null) return;
        var external = dropped
            .Select(i => i.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p) && LibraryService.IsSupportedAudio(p!))
            .Select(p => p!)
            .ToList();
        if (external.Count > 0)
            await vm.TransferAsync(external, target, move);
    }

    private DragDropEffects EffectFor(DragEventArgs e)
    {
        bool move = DataContext is FolderExplorerViewModel vm && vm.DefaultMove;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            move = !move;
        return move ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    private async void RescanFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: FolderRowViewModel folder } ||
            DataContext is not FolderExplorerViewModel vm)
            return;
        await vm.RescanFolderAsync(folder);
    }

    private void Folder_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FolderRowViewModel folder } &&
            DataContext is FolderExplorerViewModel vm)
            vm.OpenFolderInPane(folder.Path);
    }

    private void OpenFolderInPane_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FolderRowViewModel folder } &&
            DataContext is FolderExplorerViewModel vm)
            vm.OpenFolderInPane(folder.Path);
    }

    private void OpenFolderInExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FolderRowViewModel folder } &&
            DataContext is FolderExplorerViewModel vm)
            vm.ShowInFolder(folder.Path);
    }

    private async void RelocateFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: FolderRowViewModel folder } ||
            DataContext is not FolderExplorerViewModel vm)
            return;
        await vm.RelocateFolderAsync(folder);
    }

    private async void RemoveFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: FolderRowViewModel folder } button ||
            DataContext is not FolderExplorerViewModel vm)
            return;

        var ok = await Controls.PromptFlyout.ConfirmAsync(button,
            Loc.T("Explorer.RemoveConfirm"), Loc.T("Common.Remove"));
        if (ok)
            await vm.RemoveFolderAsync(folder);
    }

    private async void DeleteFile_Click(object? sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender) is not { } t || DataContext is not FolderExplorerViewModel vm) return;
        var files = SelectedFiles(t.entry, t.list);
        if (files.Count == 0) return;
        var message = files.Count == 1
            ? Loc.F("Confirm.DeleteOne", System.IO.Path.GetFileName(files[0]))
            : Loc.F("Confirm.DeleteMany", files.Count);
        if (await Controls.PromptFlyout.ConfirmAsync(t.list, message, Loc.T("Common.Delete")))
            await vm.DeleteFilesAsync(files);
    }

    private void ShowInFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: ExplorerEntry entry } &&
            DataContext is FolderExplorerViewModel vm)
            vm.ShowInFolder(entry.FullPath);
    }

    private (ExplorerEntry entry, ListBox list)? MenuTarget(object? sender)
    {
        if (sender is MenuItem { DataContext: ExplorerEntry entry } && Controls.ContextMenuOwner.Find(this, sender) is { } anchor)
        {
            var list = anchor.FindAncestorOfType<ListBox>() ?? anchor as ListBox;
            if (list != null) return (entry, list);
        }
        return null;
    }

    private static List<string> SelectedFiles(ExplorerEntry entry, ListBox list) =>
        list.SelectedItems is { Count: > 1 } sel && sel.Contains(entry)
            ? sel.Cast<ExplorerEntry>().Where(x => !x.IsDirectory).Select(x => x.FullPath).ToList()
            : entry.IsDirectory ? [] : [entry.FullPath];

    private async void EditMetadata_Click(object? sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender) is not { } t || DataContext is not FolderExplorerViewModel vm) return;
        if (t.entry.IsDirectory) return;
        var track = (await vm.ResolveTracksAsync([t.entry.FullPath])).FirstOrDefault();
        if (track == null) { vm.StatusText = Loc.T("Explorer.FileNotInLibrary"); return; }
        var edit = await Controls.MetadataFlyout.ShowAsync(t.list, track);
        if (edit != null)
            await vm.EditTrackAsync(track, edit.FileName, edit.Title, edit.Artist, edit.Album);
    }

    private async void BatchEditMetadata_Click(object? sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender) is not { } t || DataContext is not FolderExplorerViewModel vm) return;
        var tracks = await vm.ResolveTracksAsync(SelectedFiles(t.entry, t.list));
        if (tracks.Count < 2) { vm.StatusText = Loc.T("Explorer.SelectSeveral"); return; }
        var result = await Controls.MetadataFlyout.ShowBatchAsync(t.list, tracks.Count);
        if (result != null)
            await vm.BatchEditTagsAsync(tracks, result.Value.Artist, result.Value.Album);
    }

    private async void WriteCover_Click(object? sender, RoutedEventArgs e)
    {
        if (MenuTarget(sender) is not { } t || DataContext is not FolderExplorerViewModel vm) return;
        var tracks = await vm.ResolveTracksAsync(SelectedFiles(t.entry, t.list));
        if (tracks.Count == 0) { vm.StatusText = Loc.T("Explorer.FilesNotInLibrary"); return; }
        await vm.WriteCoversToFilesAsync(tracks);
    }

    private async void HideTrack_Click(object? sender, RoutedEventArgs e) => await SetHidden(sender, true);

    private async void UnhideTrack_Click(object? sender, RoutedEventArgs e) => await SetHidden(sender, false);

    private async Task SetHidden(object? sender, bool hidden)
    {
        if (MenuTarget(sender) is not { } t || DataContext is not FolderExplorerViewModel vm) return;
        var tracks = await vm.ResolveTracksAsync(SelectedFiles(t.entry, t.list));
        if (tracks.Count == 0) { vm.StatusText = Loc.T("Explorer.FilesNotInLibrary"); return; }
        await vm.SetTracksHiddenAsync(tracks, hidden);
    }

    private void List_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: ExplorerEntry entry, DataContext: ExplorerPaneViewModel pane })
            return;

        if (entry.IsDirectory)
            pane.EnterDirectory(entry.FullPath);
        else if (DataContext is FolderExplorerViewModel vm)
            vm.PlayFile(entry.FullPath);
    }
}
