using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using KebuzPlayer.Services;
using KebuzPlayer.Visualizer;
using KebuzPlayer.Visualizer.Renderers;

namespace KebuzPlayer.Controls;

public class VisualizerPanel : Control, IVizFrameSink
{
    public static readonly StyledProperty<VizPlacement> PlacementProperty =
        AvaloniaProperty.Register<VisualizerPanel, VizPlacement>(nameof(Placement));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<VisualizerPanel, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> Accent2BrushProperty =
        AvaloniaProperty.Register<VisualizerPanel, IBrush?>(nameof(Accent2Brush));

    public static readonly StyledProperty<VizFormFactors?> FormFactorOverrideProperty =
        AvaloniaProperty.Register<VisualizerPanel, VizFormFactors?>(nameof(FormFactorOverride));

    public static readonly StyledProperty<PanelVisualizerSettings?> SettingsOverrideProperty =
        AvaloniaProperty.Register<VisualizerPanel, PanelVisualizerSettings?>(nameof(SettingsOverride));

    public VizFormFactors? FormFactorOverride
    {
        get => GetValue(FormFactorOverrideProperty);
        set => SetValue(FormFactorOverrideProperty, value);
    }

    public PanelVisualizerSettings? SettingsOverride
    {
        get => GetValue(SettingsOverrideProperty);
        set => SetValue(SettingsOverrideProperty, value);
    }

    public VizPlacement Placement
    {
        get => GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? Accent2Brush
    {
        get => GetValue(Accent2BrushProperty);
        set => SetValue(Accent2BrushProperty, value);
    }

    private readonly SpectrumProcessor _processor = new();
    private readonly float[]           _raw       = new float[1024];

    private VisualizerHub?           _hub;
    private PanelVisualizerSettings? _panel;
    private IVizRenderer?            _renderer;
    private VisualizerStyle          _rendererStyle;
    private VizPalette?              _palette;
    private VizColorMode             _paletteMode;
    private bool                     _rawValid;
    private long                     _frameStamp;
    private long                     _renderStamp;

    static VisualizerPanel()
    {
        AffectsRender<VisualizerPanel>(AccentBrushProperty, Accent2BrushProperty);
    }

    bool IVizFrameSink.IsActive =>
        IsEffectivelyVisible && _panel is { Enabled: true }
        && TopLevel.GetTopLevel(this) is not Window { WindowState: WindowState.Minimized };

    bool IVizFrameSink.NeedsRawSamples =>
        _panel != null && VizStyleCatalog.Get(EffectiveStyle()).NeedsRawSamples;

    void IVizFrameSink.OnFrame(float[] spectrum, float[] raw, bool rawValid)
    {
        _processor.Update(spectrum, Elapsed(ref _frameStamp), _hub?.Frames.BinHz ?? SpectrumProcessor.ReferenceBinHz);
        bool needRaw = ((IVizFrameSink)this).NeedsRawSamples;
        _rawValid = rawValid && needRaw;
        if (_rawValid)
            raw.AsSpan(0, _raw.Length).CopyTo(_raw);
        if (_processor.HasChanged || _rawValid || (_renderer?.IsAnimated ?? false))
            InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _hub = VisualizerHub.Instance;
        if (_hub == null) return;
        _panel = ResolvePanel();
        _hub.Settings.Changed += OnSettingsChanged;
        _hub.Frames.Register(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_hub != null)
        {
            _hub.Settings.Changed -= OnSettingsChanged;
            _hub.Frames.Unregister(this);
        }
        (_renderer as IDisposable)?.Dispose();
        _renderer = null;
        _hub = null;
        _panel = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AccentBrushProperty || change.Property == Accent2BrushProperty)
            _palette = null;
        else if (change.Property == FormFactorOverrideProperty)
            InvalidateVisual();
        else if ((change.Property == PlacementProperty || change.Property == SettingsOverrideProperty)
                 && _hub != null)
        {
            _panel = ResolvePanel();
            _palette = null;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_panel == null) return;
        if (AccentBrush is not ISolidColorBrush accent) return;
        var accent2 = (Accent2Brush as ISolidColorBrush)?.Color ?? accent.Color;

        if (_palette == null || _paletteMode != _panel.ColorMode)
        {
            _palette = VizPalette.Build(_panel.ColorMode, accent.Color, accent2);
            _paletteMode = _panel.ColorMode;
        }

        var style = EffectiveStyle();
        if (_renderer == null || _rendererStyle != style)
        {
            (_renderer as IDisposable)?.Dispose();
            _renderer = CreateRenderer(style);
            _rendererStyle = style;
        }

        var frame = new VizFrame(_processor, _raw, _rawValid, _panel, _palette,
            Elapsed(ref _renderStamp));
        _renderer.Render(context, new Rect(Bounds.Size), in frame);
    }

    private static float Elapsed(ref long stamp)
    {
        long now = Stopwatch.GetTimestamp();
        float dt = stamp == 0
            ? 1f / 30f
            : (float)((now - stamp) / (double)Stopwatch.Frequency);
        stamp = now;
        return dt;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_hub == null) return;
        _panel = ResolvePanel();
        _palette = null;
        InvalidateVisual();
    }

    private PanelVisualizerSettings? ResolvePanel() =>
        SettingsOverride ?? (_hub != null ? PanelFor(_hub.Settings) : null);

    private PanelVisualizerSettings PanelFor(VisualizerSettings settings) => Placement switch
    {
        VizPlacement.Library    => settings.Library,
        VizPlacement.Overlay    => settings.Overlay,
        VizPlacement.HomeWidget => settings.Home,
        _                       => settings.Tab,
    };

    private VisualizerStyle EffectiveStyle()
    {
        if (_panel == null) return VisualizerStyle.Bars;
        var factor = FormFactorOverride ?? VizPlacements.FormFactorOf(Placement);
        return (VizStyleCatalog.Get(_panel.Style).FormFactors & factor) != 0
            ? _panel.Style
            : VisualizerStyle.Bars;
    }

    private static IVizRenderer CreateRenderer(VisualizerStyle style) => style switch
    {
        VisualizerStyle.Mirror       => new MirrorRenderer(),
        VisualizerStyle.Wave         => new WaveRenderer(),
        VisualizerStyle.Spectrum     => new SpectrumRenderer(),
        VisualizerStyle.Oscilloscope => new OscilloscopeRenderer(),
        VisualizerStyle.RadialBars   => new RadialBarsRenderer(),
        VisualizerStyle.RadialWave   => new RadialWaveRenderer(),
        VisualizerStyle.Waterfall    => new WaterfallRenderer(),
        VisualizerStyle.LedMatrix    => new LedMatrixRenderer(),
        VisualizerStyle.Particles    => new ParticlesRenderer(),
        VisualizerStyle.Dots         => new DotsRenderer(),
        VisualizerStyle.Segments     => new SegmentsRenderer(),
        VisualizerStyle.Pulse        => new PulseRenderer(),
        VisualizerStyle.RadialBarsWide => new RadialBarsWideRenderer(),
        _                            => new BarsRenderer(),
    };
}
