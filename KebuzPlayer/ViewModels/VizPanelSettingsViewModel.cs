using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Services;
using KebuzPlayer.Visualizer;
using KebuzPlayer.Lang;

namespace KebuzPlayer.ViewModels;

public sealed record VizStyleOption(VisualizerStyle Value, string Key)
{
    public string DisplayName => Loc.T(Key);
}
public sealed record VizColorModeOption(VizColorMode Value, string Key)
{
    public string DisplayName => Loc.T(Key);
}

public partial class VizPanelSettingsViewModel : ViewModelBase
{
    private readonly VisualizerHub           _hub;
    private readonly PanelVisualizerSettings _panel;
    private readonly Action?                 _onUserChange;
    private readonly EventHandler            _syncHandler;
    private bool _syncing;

    public string Title { get; }

    public VizStyleOption[] StyleOptions { get; }

    public VizColorModeOption[] ColorModeOptions { get; } =
    [
        new(VizColorMode.Solid,           "VizColor.Solid"),
        new(VizColorMode.Monochrome,      "VizColor.Monochrome"),
        new(VizColorMode.Duotone,         "VizColor.Duotone"),
        new(VizColorMode.Rainbow,         "VizColor.Rainbow"),
        new(VizColorMode.Neon,            "VizColor.Neon"),
        new(VizColorMode.ClassicSpectrum, "VizColor.ClassicSpectrum"),
        new(VizColorMode.Fire,            "VizColor.Fire"),
        new(VizColorMode.Ocean,           "VizColor.Ocean"),
    ];

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private VizStyleOption _selectedStyle;

    [ObservableProperty]
    private VizColorModeOption _selectedColorMode;

    [ObservableProperty]
    private int _intensityPercent;

    [ObservableProperty]
    private bool _showPeakRunners;

    [ObservableProperty]
    private bool _peaksSupported;

    public int[] BarCountOptions { get; } = [16, 24, 32, 48, 64, 96, 128];

    [ObservableProperty]
    private int _selectedBarCount;

    [ObservableProperty]
    private bool _barCountSupported;

    [ObservableProperty]
    private bool _isExpanded;

    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    public VizPanelSettingsViewModel(VisualizerHub hub, PanelVisualizerSettings panel,
                                     VizFormFactors formFactor, string title,
                                     Action? onUserChange = null)
    {
        _hub   = hub;
        _panel = panel;
        _onUserChange = onUserChange;
        Title  = title;
        StyleOptions = VizStyleCatalog.ForFormFactor(formFactor)
            .Select(i => new VizStyleOption(i.Style, "VizStyle." + i.Style))
            .ToArray();

        _enabled           = panel.Enabled;
        _selectedStyle     = StyleOptions.FirstOrDefault(o => o.Value == panel.Style) ?? StyleOptions[0];
        _selectedColorMode = ColorModeOptions.First(o => o.Value == panel.ColorMode);
        _intensityPercent  = (int)Math.Round(panel.Intensity * 100);
        _showPeakRunners   = panel.ShowPeakRunners;
        _peaksSupported    = VizStyleCatalog.Get(_selectedStyle.Value).SupportsPeaks;
        _selectedBarCount  = BarCountOptions.Contains(panel.BarCount) ? panel.BarCount : 64;
        _barCountSupported = VizStyleCatalog.Get(_selectedStyle.Value).SupportsBarCount;

        _syncHandler = (_, _) => SyncFromModel();
        hub.Settings.Changed += _syncHandler;
    }

    public void Detach() => _hub.Settings.Changed -= _syncHandler;

    private void SyncFromModel()
    {
        _syncing = true;
        Enabled           = _panel.Enabled;
        SelectedStyle     = StyleOptions.FirstOrDefault(o => o.Value == _panel.Style) ?? StyleOptions[0];
        SelectedColorMode = ColorModeOptions.First(o => o.Value == _panel.ColorMode);
        IntensityPercent  = (int)Math.Round(_panel.Intensity * 100);
        ShowPeakRunners   = _panel.ShowPeakRunners;
        PeaksSupported    = VizStyleCatalog.Get(SelectedStyle.Value).SupportsPeaks;
        SelectedBarCount  = BarCountOptions.Contains(_panel.BarCount) ? _panel.BarCount : 64;
        BarCountSupported = VizStyleCatalog.Get(SelectedStyle.Value).SupportsBarCount;
        _syncing = false;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_syncing) return;
        _panel.Enabled = value;
        _hub.NotifyChanged();
        _onUserChange?.Invoke();
    }

    partial void OnSelectedStyleChanged(VizStyleOption value)
    {
        if (_syncing) return;
        _panel.Style = value.Value;
        var info = VizStyleCatalog.Get(value.Value);
        PeaksSupported = info.SupportsPeaks;
        BarCountSupported = info.SupportsBarCount;
        _hub.NotifyChanged();
        _onUserChange?.Invoke();
    }

    partial void OnSelectedColorModeChanged(VizColorModeOption value)
    {
        if (_syncing) return;
        _panel.ColorMode = value.Value;
        _hub.NotifyChanged();
        _onUserChange?.Invoke();
    }

    partial void OnIntensityPercentChanged(int value)
    {
        if (_syncing) return;
        _panel.Intensity = value / 100f;
        _hub.NotifyChanged();
        _onUserChange?.Invoke();
    }

    partial void OnShowPeakRunnersChanged(bool value)
    {
        if (_syncing) return;
        _panel.ShowPeakRunners = value;
        _hub.NotifyChanged();
        _onUserChange?.Invoke();
    }

    partial void OnSelectedBarCountChanged(int value)
    {
        if (_syncing) return;
        _panel.BarCount = value;
        _hub.NotifyChanged();
        _onUserChange?.Invoke();
    }
}
