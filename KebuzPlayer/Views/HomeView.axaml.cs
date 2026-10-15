using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KebuzPlayer.Controls;
using KebuzPlayer.Home;
using KebuzPlayer.ViewModels;
using KebuzPlayer.Lang;

namespace KebuzPlayer.Views;

public partial class HomeView : UserControl
{
    private HomeViewModel? _vm;
    private WidgetCatalogEntry? _selectedEntry;
    private WidgetSize _pendingSize;
    private bool _dragArmed;
    private bool _dragging;
    private Point _dragStart;
    private Point _grabOffset;
    private HomeWidgetViewModel? _dragWidget;
    private Control? _dragContainer;
    private int _targetRow = -1;
    private int _targetCol = -1;
    private bool _targetValid;
    private Dictionary<HomeWidgetViewModel, (int Row, int Col)>? _shownCells;

    public HomeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as HomeViewModel;
        WidgetsList.ContainerPrepared += WidgetsList_ContainerPrepared;
        WidgetsList.AddHandler(PointerPressedEvent, List_PointerPressed, RoutingStrategies.Tunnel);
        WidgetsList.AddHandler(PointerMovedEvent, List_PointerMoved, RoutingStrategies.Tunnel);
        WidgetsList.AddHandler(PointerReleasedEvent, List_PointerReleased, RoutingStrategies.Tunnel);
        if (AddWidgetButton.Flyout is Flyout flyout)
            flyout.Closed += (_, _) => ResetCatalogPanels();
    }

    private int PanelColumns() =>
        WidgetsList.ItemsPanelRoot is WidgetGridPanel panel
            ? panel.CurrentColumns
            : WidgetGridPanel.ColumnsFor(WidgetsList.Bounds.Width);

    private void WidgetsList_ContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not Control container) return;
        container.Bind(WidgetGridPanel.ColumnSpanProperty, new Binding(nameof(HomeWidgetViewModel.Cols)));
        container.Bind(WidgetGridPanel.RowSpanProperty, new Binding(nameof(HomeWidgetViewModel.Rows)));
        container.Bind(WidgetGridPanel.RowProperty, new Binding(nameof(HomeWidgetViewModel.Row)));
        container.Bind(WidgetGridPanel.ColProperty, new Binding(nameof(HomeWidgetViewModel.Col)));
        if (container.DataContext is HomeWidgetViewModel widget)
            container.ContextMenu = BuildTileMenu(widget);
    }

    private ContextMenu BuildTileMenu(HomeWidgetViewModel widget)
    {
        var menu = new ContextMenu();
        if (widget.IsVisualizer)
        {
            var settings = new MenuItem { Header = Loc.T("Home.Menu.VizSettings") };
            settings.Click += (_, _) => OpenVizSettings(widget);
            menu.Items.Add(settings);

            var toggle = new MenuItem();
            void UpdateHeader() => toggle.Header = widget.VizEnabled ? Loc.T("Home.Menu.DisableRender") : Loc.T("Home.Menu.EnableRender");
            UpdateHeader();
            toggle.Click += (_, _) => { widget.ToggleVizCommand.Execute(null); UpdateHeader(); };
            menu.Items.Add(toggle);

            var reset = new MenuItem { Header = Loc.T("Home.Menu.ResetViz") };
            reset.Click += (_, _) => { widget.ResetVizCommand.Execute(null); UpdateHeader(); };
            menu.Items.Add(reset);
            menu.Opened += (_, _) =>
            {
                reset.IsVisible = widget.OwnViz != null;
                UpdateHeader();
            };
            menu.Items.Add(new Separator());
        }
        if (widget.Type == HomeWidgetType.Playlist)
        {
            var change = new MenuItem { Header = Loc.T("Home.Menu.ChangePlaylist") };
            change.Items.Add(new MenuItem { Header = "…", IsEnabled = false });
            change.SubmenuOpened += async (_, _) =>
            {
                if (_vm == null) return;
                await _vm.LoadPlaylistsAsync();
                change.Items.Clear();
                foreach (var pick in _vm.Playlists)
                {
                    var pid = pick.Id;
                    var item = new MenuItem { Header = pick.Name };
                    item.Click += (_, _) => _vm?.ChangePlaylist(widget, pid);
                    change.Items.Add(item);
                }
                if (change.Items.Count == 0)
                    change.Items.Add(new MenuItem { Header = Loc.T("Home.Menu.NoPlaylists"), IsEnabled = false });
            };
            menu.Items.Add(change);
            menu.Items.Add(new Separator());
        }
        var sizeMenu = new MenuItem { Header = Loc.T("Home.Menu.Size") };
        foreach (var size in widget.AllowedSizes)
        {
            var item = new MenuItem { Header = size.Label };
            item.Click += (_, _) => _vm?.ChangeSize(widget, size, PanelColumns());
            sizeMenu.Items.Add(item);
        }
        menu.Items.Add(sizeMenu);
        menu.Items.Add(new Separator());
        var remove = new MenuItem { Header = Loc.T("Common.Delete") };
        remove.Click += (_, _) => _vm?.RemoveWidget(widget);
        menu.Items.Add(remove);
        return menu;
    }

    private void CatalogEntry_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { DataContext: WidgetCatalogEntry entry }) return;
        _selectedEntry = entry;
        CatalogTitle.Text = entry.DisplayName.ToUpperInvariant();
        SizesList.ItemsSource = entry.AllowedSizes;
        TypesPanel.IsVisible = false;
        SizesPanel.IsVisible = true;
    }

    private void BackToTypes_Tapped(object? sender, TappedEventArgs e) => ResetCatalogPanels();

    private void BackToSizes_Tapped(object? sender, TappedEventArgs e)
    {
        if (_selectedEntry != null) CatalogTitle.Text = _selectedEntry.DisplayName.ToUpperInvariant();
        PlaylistsPanel.IsVisible = false;
        SizesPanel.IsVisible = true;
    }

    private void AddSize_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { DataContext: WidgetSize size } || _selectedEntry == null) return;
        if (_selectedEntry.Type == Home.HomeWidgetType.Playlist)
        {
            _pendingSize = size;
            CatalogTitle.Text = Loc.T("Home.PickPlaylist");
            SizesPanel.IsVisible = false;
            PlaylistsPanel.IsVisible = true;
            _ = _vm?.LoadPlaylistsAsync();
            return;
        }
        _vm?.AddWidget(_selectedEntry.Type, size, PanelColumns());
        AddWidgetButton.Flyout?.Hide();
    }

    private void PlaylistPick_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { DataContext: PlaylistPick pick } || _selectedEntry == null) return;
        _vm?.AddWidget(_selectedEntry.Type, _pendingSize, PanelColumns(), pick.Id);
        AddWidgetButton.Flyout?.Hide();
    }

    private void ResetCatalogPanels()
    {
        _selectedEntry = null;
        CatalogTitle.Text = Loc.T("Home.Widgets");
        SizesPanel.IsVisible = false;
        PlaylistsPanel.IsVisible = false;
        TypesPanel.IsVisible = true;
    }

    private void WidgetList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: TrackRow row, DataContext: TrackListWidgetContent content })
            content.PlayCommand.Execute(row);
    }

    private void OpenVizSettings(HomeWidgetViewModel widget)
    {
        if (Visualizer.VisualizerHub.Instance is not { } hub) return;

        bool created = widget.OwnViz == null;
        widget.EnsureOwnViz();
        bool dirty = false;

        var vm = new VizPanelSettingsViewModel(hub, widget.OwnViz!, widget.VizFormFactor,
            Loc.T("Home.ThisWidget"), () => dirty = true) { IsExpanded = true };
        var flyout = new Flyout
        {
            Content = new VizPanelSettingsView { DataContext = vm },
        };
        flyout.Closed += (_, _) =>
        {
            vm.Detach();
            if (created && !dirty)
            {
                widget.OwnViz = null;
                hub.Settings.NotifyChanged();
            }
            else if (dirty)
            {
                widget.Persist?.Invoke();
            }
        };

        var anchor = WidgetsList.ContainerFromItem(widget) as Control ?? (Control)this;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            flyout.ShowAt(anchor, true);
            if (TopLevel.GetTopLevel(this) is not { } top) return;

            void DismissOnPress(object? s, PointerPressedEventArgs e) => flyout.Hide();
            void Cleanup(object? s, EventArgs e)
            {
                top.RemoveHandler(PointerPressedEvent, DismissOnPress);
                flyout.Closed -= Cleanup;
            }
            top.AddHandler(PointerPressedEvent, DismissOnPress, RoutingStrategies.Tunnel);
            flyout.Closed += Cleanup;
        });
    }

    private void PlaylistOpen_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: PlaylistWidgetContent content })
        {
            content.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }

    private Control? ContainerAt(Point posInList)
    {
        if (_vm == null) return null;
        for (int i = 0; i < _vm.Widgets.Count; i++)
        {
            if (WidgetsList.ContainerFromIndex(i) is not Control c || !c.IsVisible) continue;
            var topLeft = c.TranslatePoint(new Point(0, 0), WidgetsList);
            if (topLeft == null) continue;
            if (new Rect(topLeft.Value, c.Bounds.Size).Contains(posInList)) return c;
        }
        return null;
    }

    private void List_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if (!e.GetCurrentPoint(WidgetsList).Properties.IsLeftButtonPressed) return;
        var container = ContainerAt(e.GetPosition(WidgetsList));
        if (container?.DataContext is not HomeWidgetViewModel widget) return;
        _dragWidget = widget;
        _dragContainer = container;
        _grabOffset = e.GetPosition(container);
        _dragArmed = true;
        _dragStart = e.GetPosition(WidgetsList);
    }

    private void List_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragArmed || _vm == null || _dragWidget == null) return;
        var pos = e.GetPosition(WidgetsList);
        if (!_dragging)
        {
            if (Math.Abs(pos.Y - _dragStart.Y) < 5 && Math.Abs(pos.X - _dragStart.X) < 5) return;
            _dragging = true;
            _shownCells = ShownCells();
            e.Pointer.Capture(WidgetsList);
            if (_dragContainer != null) _dragContainer.Opacity = 0.55;
        }
        UpdateGhost(e);
        AutoScroll(e.GetPosition(GridScroll));
    }

    private void UpdateGhost(PointerEventArgs e)
    {
        if (_vm == null || _dragWidget == null) return;
        if (WidgetsList.ItemsPanelRoot is not WidgetGridPanel panel) return;

        var panelPos = e.GetPosition(panel);
        double colUnit = panel.CurrentColumnWidth + WidgetGridPanel.Gap;
        double rowUnit = WidgetGridPanel.CellSize + WidgetGridPanel.Gap;
        int cols = panel.CurrentColumns;
        int col = (int)Math.Round((panelPos.X - _grabOffset.X) / colUnit);
        int row = (int)Math.Round((panelPos.Y - _grabOffset.Y) / rowUnit);
        col = Math.Clamp(col, 0, Math.Max(0, cols - _dragWidget.Cols));
        row = Math.Max(0, row);

        _targetRow = row;
        _targetCol = col;
        _targetValid = _vm.CanPlace(_dragWidget, row, col, cols, out _, _shownCells);

        var rect = panel.CellRect(row, col, _dragWidget.Cols, _dragWidget.Rows);
        var origin = panel.TranslatePoint(rect.TopLeft, GridHost);
        if (origin == null) return;
        DropGhost.Margin = new Thickness(origin.Value.X, origin.Value.Y, 0, 0);
        DropGhost.Width = rect.Width;
        DropGhost.Height = rect.Height;
        DropGhost.Classes.Set("valid", _targetValid);
        DropGhost.IsVisible = true;
    }

    private void List_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragArmed) return;
        bool wasDragging = _dragging;
        _dragArmed = false;
        _dragging = false;

        var widget = _dragWidget;
        var container = _dragContainer;
        _dragWidget = null;
        _dragContainer = null;
        int row = _targetRow, col = _targetCol;
        bool valid = _targetValid;
        var shown = _shownCells;
        _shownCells = null;
        _targetRow = _targetCol = -1;
        _targetValid = false;

        if (!wasDragging) return;

        DropGhost.IsVisible = false;
        if (container != null) container.Opacity = 1;
        e.Pointer.Capture(null);

        if (_vm == null || widget == null || !valid || row < 0) return;
        _vm.PlaceWidget(widget, row, col, PanelColumns(), shown);
    }

    private Dictionary<HomeWidgetViewModel, (int Row, int Col)>? ShownCells()
    {
        if (WidgetsList.ItemsPanelRoot is not WidgetGridPanel panel) return null;
        var cells = new Dictionary<HomeWidgetViewModel, (int Row, int Col)>();
        foreach (var child in panel.Children)
            if (child.DataContext is HomeWidgetViewModel w && panel.TryGetCell(child, out int r, out int c))
                cells[w] = (r, c);
        return cells;
    }

    private void AutoScroll(Point posInViewport)
    {
        const double zone = 28;
        const double step = 24;
        if (posInViewport.Y < zone)
            GridScroll.Offset = new Vector(GridScroll.Offset.X, Math.Max(0, GridScroll.Offset.Y - step));
        else if (posInViewport.Y > GridScroll.Bounds.Height - zone)
            GridScroll.Offset = new Vector(GridScroll.Offset.X, GridScroll.Offset.Y + step);
    }
}
