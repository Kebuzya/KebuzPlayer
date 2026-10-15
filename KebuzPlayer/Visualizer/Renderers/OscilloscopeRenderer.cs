using System;
using Avalonia;
using Avalonia.Media;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class OscilloscopeRenderer : IVizRenderer
{
    public bool IsAnimated => false;

    public void Reset() { }

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        var   raw      = frame.Raw;
        float amp      = frame.Amp;
        bool  rawValid = frame.RawValid;
        double mid = bounds.Height / 2;

        int points = Math.Clamp((int)bounds.Width, 2, raw.Length);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            double step = bounds.Width / (points - 1);
            g.BeginFigure(PointAt(0), false);
            for (int i = 1; i < points; i++)
                g.LineTo(PointAt(i));
            g.EndFigure(false);

            Point PointAt(int i)
            {
                int idx = i * (raw.Length - 1) / (points - 1);
                float v = rawValid ? Math.Clamp(raw[idx] * amp * 1.6f, -1f, 1f) : 0f;
                return new Point(i * step, mid - v * (mid - 2));
            }
        }
        ctx.DrawGeometry(null, frame.Palette.LinePen, geometry);
    }
}
