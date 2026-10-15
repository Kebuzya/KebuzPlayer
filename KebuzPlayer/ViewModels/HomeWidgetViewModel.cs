using System;
using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Home;
using KebuzPlayer.Services;
using KebuzPlayer.Visualizer;

namespace KebuzPlayer.ViewModels;

public partial class HomeWidgetViewModel : ViewModelBase
{
    public HomeWidgetType Type { get; }
    public string DisplayName { get; }
    public Geometry Icon { get; }
    public IReadOnlyList<WidgetSize> AllowedSizes { get; }
    public int? PlaylistId { get; set; }
    public int PeriodDays { get; set; } = 30;

    [ObservableProperty]
    private object? _content;

    [ObservableProperty]
    private int _cols;

    [ObservableProperty]
    private int _rows;

    [ObservableProperty]
    private int _row;

    [ObservableProperty]
    private int _col;

    [ObservableProperty]
    private bool _vizEnabled = true;

    [ObservableProperty]
    private PanelVisualizerSettings? _ownViz;

    public Action? Persist { get; set; }

    public string SizeLabel => $"{Cols}x{Rows}";
    public string TitleCaps => DisplayName.ToUpperInvariant();
    public bool IsVisualizer => Type == HomeWidgetType.Visualizer;
    public bool ShowPlaceholder => Content == null && !IsVisualizer;
    public VizFormFactors VizFormFactor =>
        Cols >= 3 || Rows >= 2 ? VizFormFactors.WideStrip : VizFormFactors.Compact;

    partial void OnContentChanged(object? value) => OnPropertyChanged(nameof(ShowPlaceholder));

    partial void OnColsChanged(int value)
    {
        OnPropertyChanged(nameof(SizeLabel));
        OnPropertyChanged(nameof(VizFormFactor));
    }
    partial void OnRowsChanged(int value)
    {
        OnPropertyChanged(nameof(SizeLabel));
        OnPropertyChanged(nameof(VizFormFactor));
    }

    public HomeWidgetViewModel(HomeWidgetType type, WidgetSize size)
    {
        var entry = WidgetCatalog.Get(type);
        Type = type;
        DisplayName = entry.DisplayName;
        Icon = entry.Icon;
        AllowedSizes = entry.AllowedSizes;
        _cols = size.Cols;
        _rows = size.Rows;

        if (IsVisualizer && VisualizerHub.Instance is { } hub)
        {
            _vizEnabled = EffectiveViz?.Enabled ?? true;
            _vizChangedHandler = (_, _) => VizEnabled = EffectiveViz?.Enabled ?? true;
            hub.Settings.Changed += _vizChangedHandler;
        }
    }

    private EventHandler? _vizChangedHandler;

    public PanelVisualizerSettings? EffectiveViz =>
        OwnViz ?? VisualizerHub.Instance?.Settings.Home;

    partial void OnOwnVizChanged(PanelVisualizerSettings? value) =>
        VizEnabled = EffectiveViz?.Enabled ?? true;

    public void EnsureOwnViz()
    {
        if (OwnViz != null) return;
        OwnViz = VisualizerHub.Instance?.Settings.Home.Clone() ?? new PanelVisualizerSettings();
    }

    public void Detach()
    {
        if (_vizChangedHandler != null && VisualizerHub.Instance is { } hub)
            hub.Settings.Changed -= _vizChangedHandler;
        _vizChangedHandler = null;
    }

    [RelayCommand]
    private void ToggleViz()
    {
        if (VisualizerHub.Instance is not { } hub) return;
        EnsureOwnViz();
        OwnViz!.Enabled = !OwnViz.Enabled;
        VizEnabled = OwnViz.Enabled;
        hub.NotifyChanged();
        Persist?.Invoke();
    }

    [RelayCommand]
    private void ResetViz()
    {
        if (OwnViz == null) return;
        OwnViz = null;
        VisualizerHub.Instance?.Settings.NotifyChanged();
        Persist?.Invoke();
    }
}
