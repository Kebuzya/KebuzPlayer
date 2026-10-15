using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KebuzPlayer.Services;
using KebuzPlayer.ViewModels;

namespace KebuzPlayer.Views;

public partial class PlaylistDetailView : UserControl
{
    private readonly IDataTemplate? _tableTemplate;
    private PlaylistDetailViewModel? _vm;
    private bool _dragArmed;
    private bool _dragging;
    private Point _dragStart;
    private int _dragIndex = -1;

    public PlaylistDetailView()
    {
        InitializeComponent();
        _tableTemplate = RowsList.ItemTemplate;
        Controls.TableHeaderSync.Attach(RowsList, HeaderRow, 20, 20);
        DataContextChanged += (_, _) => HookViewMode();
        RowsList.AddHandler(PointerPressedEvent, List_PointerPressed, RoutingStrategies.Tunnel);
        RowsList.AddHandler(PointerMovedEvent, List_PointerMoved, RoutingStrategies.Tunnel);
        RowsList.AddHandler(PointerReleasedEvent, List_PointerReleased, RoutingStrategies.Tunnel);
    }

    private void List_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || !_vm.IsManualSort) return;
        if (!e.GetCurrentPoint(RowsList).Properties.IsLeftButtonPressed) return;
        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (item == null) return;
        _dragIndex = RowsList.IndexFromContainer(item);
        if (_dragIndex < 0) return;
        _dragArmed = true;
        _dragStart = e.GetPosition(RowsList);
    }

    private void List_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragArmed || _vm == null) return;
        var pos = e.GetPosition(RowsList);
        if (!_dragging)
        {
            if (Math.Abs(pos.Y - _dragStart.Y) < 5 && Math.Abs(pos.X - _dragStart.X) < 5) return;
            _dragging = true;
            e.Pointer.Capture(RowsList);
        }

        if (_vm.IsGridMode)
        {
            DropIndicator.IsVisible = false;
            var target = GridInsertTargetAt(pos);
            if (target == null)
                DropIndicatorV.IsVisible = false;
            else
            {
                DropIndicatorV.IsVisible = true;
                double x = target.Value.InsertAfter ? target.Value.Bounds.Right : target.Value.Bounds.Left;
                DropIndicatorV.Margin = new Thickness(Math.Max(0, x - 1), target.Value.Bounds.Top, 0, 0);
                DropIndicatorV.Height = target.Value.Bounds.Height;
            }
        }
        else
        {
            DropIndicatorV.IsVisible = false;
            var (_, y) = InsertTargetAt(pos);
            if (double.IsNaN(y))
                DropIndicator.IsVisible = false;
            else
            {
                DropIndicator.IsVisible = true;
                DropIndicator.Margin = new Thickness(0, Math.Max(0, y - 1), 0, 0);
            }
        }
        AutoScroll(pos);
    }

    private void List_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragArmed) return;
        bool wasDragging = _dragging;
        bool wasGrid = _vm?.IsGridMode ?? false;
        _dragArmed = false;
        _dragging = false;
        DropIndicator.IsVisible = false;
        DropIndicatorV.IsVisible = false;
        e.Pointer.Capture(null);
        if (!wasDragging || _vm == null || _dragIndex < 0) return;
        var pos = e.GetPosition(RowsList);
        int insert = wasGrid
            ? GridInsertTargetAt(pos)?.Insert ?? -1
            : InsertTargetAt(pos).Insert;
        int from = _dragIndex;
        _dragIndex = -1;
        if (insert < 0) return;
        int to = insert > from ? insert - 1 : insert;
        if (to == from) return;
        _vm.MoveRow(from, to);
        _ = _vm.CommitOrderAsync();
    }

    private (int Insert, double Y) InsertTargetAt(Point pos)
    {
        if (_vm == null || _vm.Rows.Count == 0) return (-1, double.NaN);
        int insert = -1;
        double y = double.NaN;
        for (int i = 0; i < _vm.Rows.Count; i++)
        {
            if (RowsList.ContainerFromIndex(i) is not ListBoxItem c || !c.IsVisible) continue;
            var top = c.TranslatePoint(new Point(0, 0), RowsList);
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

    private readonly record struct GridDropTarget(int Insert, Rect Bounds, bool InsertAfter);

    private GridDropTarget? GridInsertTargetAt(Point pos)
    {
        if (_vm == null || _vm.Rows.Count == 0) return null;
        int best = -1;
        double bestDistSq = double.MaxValue;
        Rect bestBounds = default;
        for (int i = 0; i < _vm.Rows.Count; i++)
        {
            if (RowsList.ContainerFromIndex(i) is not ListBoxItem c || !c.IsVisible) continue;
            var topLeft = c.TranslatePoint(new Point(0, 0), RowsList);
            if (topLeft == null) continue;
            var bounds = new Rect(topLeft.Value, c.Bounds.Size);
            var center = bounds.Center;
            double dx = center.X - pos.X;
            double dy = center.Y - pos.Y;
            double distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = i;
                bestBounds = bounds;
            }
        }
        if (best < 0) return null;
        bool after = pos.X > bestBounds.Center.X;
        return new GridDropTarget(after ? best + 1 : best, bestBounds, after);
    }

    private void AutoScroll(Point pos)
    {
        if (RowsList.Scroll is not ScrollViewer scroll) return;
        const double zone = 28;
        const double step = 24;
        if (pos.Y < zone)
            scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, scroll.Offset.Y - step));
        else if (pos.Y > RowsList.Bounds.Height - zone)
            scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + step);
    }

    private void HookViewMode()
    {
        if (_vm != null)
            _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm = DataContext as PlaylistDetailViewModel;
        if (_vm == null) return;
        _vm.PropertyChanged += Vm_PropertyChanged;
        ApplyViewMode(_vm.ViewMode);
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistDetailViewModel.ViewMode) && _vm != null)
            ApplyViewMode(_vm.ViewMode);
    }

    private void ApplyViewMode(TrackViewMode mode)
    {
        RowsList.ItemsPanel = (ITemplate<Panel?>)RowsList.Resources[
            mode == TrackViewMode.Grid ? "GridItemsPanel" : "ListItemsPanel"]!;
        RowsList.ItemTemplate = mode switch
        {
            TrackViewMode.Grid    => (IDataTemplate)RowsList.Resources["GridRowTemplate"]!,
            TrackViewMode.Compact => (IDataTemplate)RowsList.Resources["CompactRowTemplate"]!,
            _                     => _tableTemplate,
        };
    }

    private void Rows_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: TrackRow row } &&
            DataContext is PlaylistDetailViewModel vm)
        {
            vm.PlayTrackCommand.Execute(row);
        }
    }

    private void Rows_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && DataContext is PlaylistDetailViewModel vm)
            vm.SelectedTracks = listBox.SelectedItems!.Cast<TrackRow>().Select(r => r.Track).ToList();
    }

    private async void AddTracks_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistDetailViewModel vm)
            await vm.LoadPickerAsync();
    }

    private async void ConfirmPicker_Click(object? sender, RoutedEventArgs e)
    {
        AddTracksButton.Flyout?.Hide();
        if (DataContext is PlaylistDetailViewModel vm)
            await vm.ConfirmPickerCommand.ExecuteAsync(null);
    }
}
