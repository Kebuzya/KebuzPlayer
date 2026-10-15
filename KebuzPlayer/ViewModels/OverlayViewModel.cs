using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Services;

namespace KebuzPlayer.ViewModels;

public partial class OverlayViewModel : ViewModelBase
{
    private readonly JsonSettingsService _settings;

    public PlayerViewModel Player { get; }

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private bool _isVivid;

    [ObservableProperty]
    private bool _showVisualizer;

    public event EventHandler? CloseRequested;

    public OverlayViewModel(PlayerViewModel player, JsonSettingsService settings)
    {
        Player = player;
        _settings = settings;
        _isPinned = settings.Current.OverlayPinned;
        _isVivid = settings.Current.OverlayStyle == OverlayStyle.Vivid;
        _showVisualizer = Visualizer.VisualizerHub.Instance?.Settings.Overlay.Enabled
            ?? settings.Current.OverlayVisualizerEnabled;
        settings.Saved += (_, _) => RefreshFromSettings();
        if (Visualizer.VisualizerHub.Instance is { } hub)
            hub.Settings.Changed += (_, _) => ShowVisualizer = hub.Settings.Overlay.Enabled;
    }

    public void RefreshFromSettings()
    {
        IsVivid = _settings.Current.OverlayStyle == OverlayStyle.Vivid;
        if (Visualizer.VisualizerHub.Instance is { } hub)
            ShowVisualizer = hub.Settings.Overlay.Enabled;
    }

    public (int X, int Y)? SavedPosition =>
        _settings.Current is { OverlayPosX: int x, OverlayPosY: int y } ? (x, y) : null;

    public void SavePosition(int x, int y)
    {
        _settings.Current.OverlayPosX = x;
        _settings.Current.OverlayPosY = y;
        _settings.Save();
    }

    [RelayCommand]
    private void TogglePin()
    {
        IsPinned = !IsPinned;
        _settings.Current.OverlayPinned = IsPinned;
        _settings.Save();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
