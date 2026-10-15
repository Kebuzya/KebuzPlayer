using System;
using Avalonia;
using Avalonia.Media;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class DotsRenderer : IVizRenderer
{
    public bool IsAnimated => false;

    public void Reset() { }

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        int    count  = Math.Max(1, frame.Settings.BarCount);
        double width  = bounds.Width / count;
        double r      = Math.Clamp(width * 0.32, 1.5, 4.0);
        var    smooth = frame.Processor.Smooth;
        var    peaks  = frame.Processor.Peaks;
        float  amp    = frame.Amp;
        bool   runners = frame.Settings.ShowPeakRunners;
        double span   = bounds.Height - r * 2;

        for (int i = 0; i < count; i++)
        {
            float  level = Math.Min(1f, BarResampler.Level(smooth, i, count) * amp);
            double cx    = i * width + width / 2;
            double cy    = r + span * (1 - level);
            var brush = frame.Palette.UsesLevelColor
                ? frame.Palette.LevelBrush(level)
                : frame.Palette.BarBrush(i, count);
            ctx.DrawEllipse(brush, null, new Point(cx, cy), r, r);

            if (!runners) continue;
            float pv = Math.Min(1f, BarResampler.Level(peaks, i, count) * amp);
            if (pv < 0.02f || pv <= level + 0.01f) continue;
            double py = r + span * (1 - pv);
            ctx.DrawEllipse(frame.Palette.Peak, null, new Point(cx, py), r * 0.55, r * 0.55);
        }
    }
}
