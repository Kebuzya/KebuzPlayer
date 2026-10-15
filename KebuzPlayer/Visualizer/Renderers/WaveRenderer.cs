using System;
using Avalonia;
using Avalonia.Media;
using KebuzPlayer.Services;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class WaveRenderer : IVizRenderer
{
    public bool IsAnimated => false;

    public void Reset() { }

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        int   count  = SpectrumProcessor.BarCount;
        var   smooth = frame.Processor.Smooth;
        float amp    = frame.Amp;

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            double step = bounds.Width / (count - 1);
            double pad  = bounds.Height * 0.06;
            double span = bounds.Height - pad * 2;
            g.BeginFigure(PointAt(0), false);
            for (int i = 1; i < count; i++)
                g.LineTo(PointAt(i));
            g.EndFigure(false);

            Point PointAt(int i)
            {
                float level = Math.Min(1f, smooth[i] * amp);
                return new Point(i * step, pad + span * (1 - level));
            }
        }
        ctx.DrawGeometry(null, frame.Palette.LinePen, geometry);
    }
}
