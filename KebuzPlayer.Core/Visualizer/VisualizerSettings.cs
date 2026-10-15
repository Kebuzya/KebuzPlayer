namespace KebuzPlayer.Services;

public enum VisualizerStyle { Bars, Wave, Mirror, RadialBars, RadialWave, Spectrum, Oscilloscope, Waterfall, LedMatrix, Particles, Dots, Segments, Pulse, RadialBarsWide }

public enum VizColorMode { Solid, Monochrome, Rainbow, ClassicSpectrum, Duotone, Fire, Ocean, Neon }

public sealed class PanelVisualizerSettings
{
    public bool            Enabled         { get; set; } = true;
    public VisualizerStyle Style           { get; set; } = VisualizerStyle.Bars;
    public float           Intensity       { get; set; } = 0.75f;
    public VizColorMode    ColorMode       { get; set; } = VizColorMode.Solid;
    public bool            ShowPeakRunners { get; set; } = true;
    public int             BarCount        { get; set; } = 64;

    public PanelVisualizerSettings Clone() => new()
    {
        Enabled         = Enabled,
        Style           = Style,
        Intensity       = Intensity,
        ColorMode       = ColorMode,
        ShowPeakRunners = ShowPeakRunners,
        BarCount        = BarCount,
    };
}

public sealed class VisualizerSettings
{
    public PanelVisualizerSettings Library { get; } = new();
    public PanelVisualizerSettings Tab     { get; } = new();
    public PanelVisualizerSettings Overlay { get; } = new();
    public PanelVisualizerSettings Home    { get; } = new();

    public int FrameRate { get; set; } = 30;

    public event EventHandler? Changed;
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
