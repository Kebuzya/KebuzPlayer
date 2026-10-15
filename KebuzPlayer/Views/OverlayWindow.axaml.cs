using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using KebuzPlayer.Interop;
using KebuzPlayer.ViewModels;

namespace KebuzPlayer.Views;

public partial class OverlayWindow : Window
{
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(3.5);
    private static readonly TimeSpan TopmostRecheckInterval = TimeSpan.FromSeconds(2);
    private const int ScreenMarginPx = 24;

    private readonly DispatcherTimer _autoHideTimer;
    private readonly DispatcherTimer _topmostTimer;

    private bool _moved;
    private bool _pointerOver;

    private OverlayViewModel Vm => (OverlayViewModel)DataContext!;

    public OverlayWindow()
    {
        InitializeComponent();

        _autoHideTimer = new DispatcherTimer { Interval = AutoHideDelay };
        _autoHideTimer.Tick += (_, _) => HideOverlay();

        _topmostTimer = new DispatcherTimer { Interval = TopmostRecheckInterval };
        _topmostTimer.Tick += (_, _) => ToolWindowStyle.AssertTopmost(this);

        Root.PointerEntered += (_, _) =>
        {
            _pointerOver = true;
            _autoHideTimer.Stop();
        };
        Root.PointerExited += (_, _) =>
        {
            _pointerOver = false;
            if (!Vm.IsPinned) RestartAutoHide();
        };
        Root.PointerPressed += OnRootPointerPressed;

        PositionChanged += (_, _) =>
        {
            if (IsVisible) _moved = true;
        };
        Opened += (_, _) => ToolWindowStyle.Apply(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is OverlayViewModel vm)
            vm.CloseRequested += (_, _) => HideOverlay();
    }

    public void ToggleVisible()
    {
        if (IsVisible)
        {
            HideOverlay();
            return;
        }
        ShowOverlay();
    }

    public void ShowTransient()
    {
        if (IsVisible)
        {
            ToolWindowStyle.AssertTopmost(this);
            if (!Vm.IsPinned && !_pointerOver) RestartAutoHide();
            return;
        }
        ShowOverlay();
    }

    private void ShowOverlay()
    {
        Vm.RefreshFromSettings();

        void OnLayoutUpdated(object? s, EventArgs e)
        {
            LayoutUpdated -= OnLayoutUpdated;
            PositionWindow();
        }
        LayoutUpdated += OnLayoutUpdated;

        Show();
        ToolWindowStyle.AssertTopmost(this);
        _topmostTimer.Start();
        if (!Vm.IsPinned) RestartAutoHide();
    }

    private void HideOverlay()
    {
        SavePositionIfMoved();
        Hide();
        _autoHideTimer.Stop();
        _topmostTimer.Stop();
    }

    private void SavePositionIfMoved()
    {
        if (!_moved) return;
        _moved = false;
        Vm.SavePosition(Position.X, Position.Y);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        SavePositionIfMoved();
        base.OnClosing(e);
    }

    private void RestartAutoHide()
    {
        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            ActivateMainWindow();
            return;
        }
        BeginMoveDrag(e);
    }

    internal static void ActivateMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } main })
            return;

        main.Show();
        if (main.WindowState == WindowState.Minimized)
            main.WindowState = main is MainWindow shell ? shell.RestoreState : WindowState.Normal;
        main.Activate();
    }

    private void Volume_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        int step = e.Delta.Y > 0 ? 2 : -2;
        Vm.Player.VolumePercent = Math.Clamp(Vm.Player.VolumePercent + step, 0, 100);
        e.Handled = true;
    }

    private void Gain_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y > 0)
            Vm.Player.GainUpCommand.Execute(null);
        else
            Vm.Player.GainDownCommand.Execute(null);
        e.Handled = true;
    }

    private void PositionWindow()
    {
        int wPx = Math.Max((int)(Bounds.Width * RenderScaling), 1);
        int hPx = Math.Max((int)(Bounds.Height * RenderScaling), 1);

        var target = DefaultPosition();
        if (Vm.SavedPosition is { } saved)
        {
            var rect = new PixelRect(saved.X, saved.Y, wPx, hPx);
            foreach (var screen in Screens.All)
            {
                if (screen.WorkingArea.Intersects(rect))
                {
                    target = new PixelPoint(saved.X, saved.Y);
                    break;
                }
            }
        }

        if (target is { } pos) Position = pos;
        _moved = false;
    }

    private PixelPoint? DefaultPosition()
    {
        var area = Screens.Primary?.WorkingArea;
        if (area == null) return null;
        return new PixelPoint(area.Value.X + ScreenMarginPx, area.Value.Y + ScreenMarginPx);
    }
}
