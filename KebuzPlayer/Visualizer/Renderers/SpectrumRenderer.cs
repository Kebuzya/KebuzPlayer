using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using KebuzPlayer.Services;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class SpectrumRenderer : IVizRenderer
{
    private VizPalette? _cachedPalette;
    private ImmutableLinearGradientBrush? _fill;

    public bool IsAnimated => false;

    public void Reset() { }

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        int   count  = SpectrumProcessor.BarCount;
        var   smooth = frame.Processor.Smooth;
        float amp    = frame.Amp;

        if (!ReferenceEquals(_cachedPalette, frame.Palette))
        {
            _cachedPalette = frame.Palette;
            var top    = frame.Palette.LevelBrush(1f).Color;
            var bottom = frame.Palette.LevelBrush(0f).Color;
            _fill = new ImmutableLinearGradientBrush(
                [
                    new ImmutableGradientStop(0, top),
                    new ImmutableGradientStop(1, Color.FromArgb(0x38, bottom.R, bottom.G, bottom.B)),
                ],
                startPoint: new RelativePoint(0.5, 0, RelativeUnit.Relative),
                endPoint:   new RelativePoint(0.5, 1, RelativeUnit.Relative));
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            double step = bounds.Width / (count - 1);
            g.BeginFigure(new Point(0, bounds.Height), true);
            for (int i = 0; i < count; i++)
            {
                float level = Math.Min(1f, smooth[i] * amp);
                g.LineTo(new Point(i * step, bounds.Height * (1 - level)));
            }
            g.LineTo(new Point(bounds.Width, bounds.Height));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(_fill, frame.Palette.LinePen, geometry);
    }
}
