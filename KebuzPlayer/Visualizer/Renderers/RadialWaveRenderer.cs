using System;
using Avalonia;
using Avalonia.Media;
using KebuzPlayer.Services;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class RadialWaveRenderer : IVizRenderer
{
    private const int Points = SpectrumProcessor.BarCount * 2;

    private static readonly float[] Cos = BuildTable(MathF.Cos);
    private static readonly float[] Sin = BuildTable(MathF.Sin);

    public bool IsAnimated => false;

    public void Reset() { }

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        int   count  = SpectrumProcessor.BarCount;
        var   smooth = frame.Processor.Smooth;
        float amp    = frame.Amp;

        var    center = bounds.Center;
        double minHalf = Math.Min(bounds.Width, bounds.Height) / 2;
        double r0   = minHalf * 0.38;
        double span = minHalf * 0.52;

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(PointAt(0), false);
            for (int i = 1; i < Points; i++)
                g.LineTo(PointAt(i));
            g.EndFigure(true);

            Point PointAt(int i)
            {
                int bar = i < count ? i : Points - 1 - i;
                float level = Math.Min(1f, smooth[bar] * amp);
                double r = r0 + span * level;
                return new Point(center.X + Cos[i] * r, center.Y + Sin[i] * r);
            }
        }
        ctx.DrawGeometry(null, frame.Palette.LinePen, geometry);
    }

    private static float[] BuildTable(Func<float, float> f)
    {
        var t = new float[Points];
        for (int i = 0; i < Points; i++)
            t[i] = f((float)(2 * Math.PI * i / Points - Math.PI / 2));
        return t;
    }
}
