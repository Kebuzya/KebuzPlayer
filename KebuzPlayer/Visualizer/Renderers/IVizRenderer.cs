using System;
using Avalonia;
using Avalonia.Media;
using KebuzPlayer.Services;

namespace KebuzPlayer.Visualizer.Renderers;

public readonly struct VizFrame
{
    public SpectrumProcessor       Processor { get; }
    public float[]                 Raw       { get; }
    public bool                    RawValid  { get; }
    public PanelVisualizerSettings Settings  { get; }
    public VizPalette              Palette   { get; }

    public float Dt { get; }

    public VizFrame(SpectrumProcessor processor, float[] raw, bool rawValid,
                    PanelVisualizerSettings settings, VizPalette palette, float dt)
    {
        Processor = processor;
        Raw       = raw;
        RawValid  = rawValid;
        Settings  = settings;
        Palette   = palette;
        Dt        = dt;
    }

    public float Steps => Math.Clamp(Dt * 30f, 0.05f, 4f);

    public float Amp => Math.Clamp(0.25f + Settings.Intensity, 0.25f, 1.25f);
}

public interface IVizRenderer
{
    bool IsAnimated { get; }
    void Render(DrawingContext ctx, Rect bounds, in VizFrame frame);
    void Reset();
}
