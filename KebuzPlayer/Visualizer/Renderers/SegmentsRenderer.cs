using System;
using Avalonia;
using Avalonia.Media;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class SegmentsRenderer : IVizRenderer
{
    public bool IsAnimated => false;

    public void Reset() { }

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        int    count  = Math.Max(1, frame.Settings.BarCount);
        double width  = bounds.Width / count;
        double w      = width * 0.7;
        int    segs   = Math.Clamp((int)(bounds.Height / 8), 3, 28);
        double segH   = bounds.Height / segs;
        double gap    = Math.Min(segH * 0.25, 2.0);
        var    smooth = frame.Processor.Smooth;
        var    peaks  = frame.Processor.Peaks;
        float  amp    = frame.Amp;
        bool   runners = frame.Settings.ShowPeakRunners;

        for (int i = 0; i < count; i++)
        {
            float level = Math.Min(1f, BarResampler.Level(smooth, i, count) * amp);
            int   lit   = (int)Math.Round(level * segs);
            for (int s = 0; s < lit; s++)
            {
                var brush = frame.Palette.UsesLevelColor
                    ? frame.Palette.LevelBrush((s + 0.5f) / segs)
                    : frame.Palette.BarBrush(i, count);
                double y = bounds.Height - (s + 1) * segH;
                ctx.FillRectangle(brush, new Rect(i * width, y + gap / 2, w, segH - gap));
            }

            if (!runners) continue;
            float pv = Math.Min(1f, BarResampler.Level(peaks, i, count) * amp);
            int   ps = (int)Math.Round(pv * segs);
            if (ps <= lit || ps < 1) continue;
            double py = bounds.Height - ps * segH;
            ctx.FillRectangle(frame.Palette.Peak, new Rect(i * width, py + gap / 2, w, segH - gap));
        }
    }
}
