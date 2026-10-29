using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FlyoutBase = Avalonia.Controls.Primitives.FlyoutBase;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using KebuzPlayer.ViewModels;
using KebuzPlayer.Lang;

namespace KebuzPlayer.Views;

public partial class LibraryView : UserControl
{
    private IDataTemplate? _tableTemplate;
    private LibraryViewModel? _vm;

    public LibraryView()
    {
        InitializeComponent();
        _tableTemplate = TracksList.ItemTemplate;
        Controls.TableHeaderSync.Attach(TracksList, HeaderRow, 20, 20);
        DataContextChanged += (_, _) => HookViewMode();

        DragDrop.SetAllowDrop(TracksList, true);
        TracksList.AddHandler(DragDrop.DragOverEvent, TracksList_DragOver);
        TracksList.AddHandler(DragDrop.DropEvent, TracksList_Drop);
    }

    private void TracksList_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? EffectFor(e) : DragDropEffects.None;
        e.Handled = true;
    }

    private async void TracksList_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not LibraryViewModel vm) return;

        var dropped = e.DataTransfer.TryGetFiles();
        if (dropped == null) return;
        var files = dropped
            .Select(i => i.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p) && LibraryService.IsSupportedAudio(p!))
            .Select(p => p!)
            .ToList();
        if (files.Count == 0) return;

        bool move = EffectFor(e) == DragDropEffects.Move;
        var folders = await vm.GetWatchedFoldersAsync();
        if (folders.Count == 0)
        {
            vm.StatusText = Loc.T("Library.AddFolderFirst");
            return;
        }
        if (folders.Count == 1)
        {
            await vm.DropFilesAsync(files, folders[0], move);
            return;
        }

        var menu = new MenuFlyout();
        foreach (var folder in folders)
        {
            var leaf = System.IO.Path.GetFileName(folder.TrimEnd('\\', '/'));
            var item = new MenuItem { Header = string.IsNullOrEmpty(leaf) ? folder : leaf };
            ToolTip.SetTip(item, folder);
            var target = folder;
            item.Click += async (_, _) => await vm.DropFilesAsync(files, target, move);
            menu.Items.Add(item);
        }
        menu.ShowAt(TracksList, showAtPointer: true);
    }

    private DragDropEffects EffectFor(DragEventArgs e)
    {
        bool move = DataContext is LibraryViewModel vm && vm.DefaultMove;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            move = !move;
        return move ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    private void HookViewMode()
    {
        if (_vm != null)
            _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm = DataContext as LibraryViewModel;
        if (_vm == null) return;
        _vm.PropertyChanged += Vm_PropertyChanged;
        ApplyViewMode(_vm.ViewMode);
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.ViewMode) && _vm != null)
            ApplyViewMode(_vm.ViewMode);
    }

    private void ApplyViewMode(TrackViewMode mode)
    {
        TracksList.ItemsPanel = (ITemplate<Panel?>)TracksList.Resources[
            mode == TrackViewMode.Grid ? "GridItemsPanel" : "ListItemsPanel"]!;
        TracksList.ItemTemplate = mode switch
        {
            TrackViewMode.Grid    => (IDataTemplate)TracksList.Resources["GridRowTemplate"]!,
            TrackViewMode.Compact => (IDataTemplate)TracksList.Resources["CompactRowTemplate"]!,
            _                     => _tableTemplate,
        };
    }

    private void Rows_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: TrackRow row } &&
            DataContext is LibraryViewModel vm)
        {
            vm.PlayTrackCommand.Execute(row);
        }
    }

    private void PreviewCover_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: TrackRow row } &&
            Controls.ContextMenuOwner.Find(this, sender) is { } anchor &&
            DataContext is LibraryViewModel vm)
        {
            vm.Preview.LoadCommand.Execute(row.Track);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => FlyoutBase.ShowAttachedFlyout(anchor));
        }
    }

    private async void WriteCover_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: TrackRow row } && DataContext is LibraryViewModel vm)
            await vm.WriteCoversToFilesAsync(TargetTracks(vm, row));
    }

    private async void EditMetadata_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TrackRow row } ||
            DataContext is not LibraryViewModel vm)
            return;

        var edit = await Controls.MetadataFlyout.ShowAsync(TracksList, row.Track);
        if (edit != null)
            await vm.EditTrackAsync(row.Track, edit.FileName, edit.Title, edit.Artist, edit.Album);
    }

    private async void BatchEditMetadata_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TrackRow row } ||
            DataContext is not LibraryViewModel vm)
            return;

        var targets = TargetTracks(vm, row);
        if (targets.Count < 2)
        {
            vm.StatusText = Loc.T("Library.SelectSeveral");
            return;
        }
        var result = await Controls.MetadataFlyout.ShowBatchAsync(TracksList, targets.Count);
        if (result != null)
            await vm.BatchEditTagsAsync(targets, result.Value.Artist, result.Value.Album);
    }

    private async void HideTrack_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: TrackRow row } && DataContext is LibraryViewModel vm)
            await vm.SetTracksHiddenAsync(TargetTracks(vm, row), hidden: true);
    }

    private async void UnhideTrack_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: TrackRow row } && DataContext is LibraryViewModel vm)
            await vm.SetTracksHiddenAsync(TargetTracks(vm, row), hidden: false);
    }

    private async void DeleteTrack_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TrackRow row } ||
            DataContext is not LibraryViewModel vm)
            return;

        var targets = TargetTracks(vm, row);
        var message = targets.Count == 1
            ? Loc.F("Confirm.DeleteOne", targets[0].Title)
            : Loc.F("Confirm.DeleteMany", targets.Count);
        if (await Controls.PromptFlyout.ConfirmAsync(TracksList, message))
            await vm.DeleteTracksAsync(targets);
    }

    private void Gain_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is Control { DataContext: TrackRow row } &&
            DataContext is LibraryViewModel vm)
        {
            vm.AdjustTrackGain(row.Track, e.Delta.Y > 0 ? 0.5f : -0.5f);
            e.Handled = true;
        }
    }

    private void Tracks_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && DataContext is LibraryViewModel vm)
            vm.SelectedTracks = listBox.SelectedItems!.Cast<TrackRow>().Select(r => r.Track).ToList();
    }

    private static List<Track> TargetTracks(LibraryViewModel vm, TrackRow row) =>
        vm.SelectedTracks.Contains(row.Track) ? vm.SelectedTracks.ToList() : [row.Track];

    private async void AddToPlaylist_SubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TrackRow row } parent ||
            DataContext is not LibraryViewModel vm)
            return;

        var playlists = await vm.GetPlaylistsAsync();
        parent.Items.Clear();
        foreach (var p in playlists)
        {
            var item = new MenuItem { Header = p.Name };
            item.Click += async (_, _) => await vm.AddToPlaylistAsync(p.Id, TargetTracks(vm, row));
            parent.Items.Add(item);
        }
        if (playlists.Count > 0)
            parent.Items.Add(new Separator());
        var create = new MenuItem { Header = Loc.T("Playlist.NewMenu") };
        create.Click += async (_, _) =>
        {
            var name = await Controls.PromptFlyout.ShowAsync(TracksList, Loc.T("Playlist.NamePrompt"));
            if (name != null)
                await vm.CreatePlaylistWithTracksAsync(name, TargetTracks(vm, row));
        };
        parent.Items.Add(create);
    }
}
