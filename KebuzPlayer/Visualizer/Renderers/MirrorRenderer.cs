using System;
using Avalonia;
using Avalonia.Media;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class MirrorRenderer : IVizRenderer
{
    public bool IsAnimated => false;

    public void Reset() { }

    private static IBrush BrushFor(in VizFrame frame, int index, int count, float level) =>
        frame.Palette.UsesLevelColor
            ? frame.Palette.LevelBrush(level)
            : frame.Palette.BarBrush(index, count);

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        int    count  = Math.Max(1, frame.Settings.BarCount);
        double width  = bounds.Width / count;
        double mid    = bounds.Height / 2;
        var    smooth = frame.Processor.Smooth;
        var    peaks  = frame.Processor.Peaks;
        float  amp    = frame.Amp;
        bool   runners = frame.Settings.ShowPeakRunners;

        double w = width * 0.7;

        for (int i = 0; i < count; i++)
        {
            float  level = Math.Min(1f, BarResampler.Level(smooth, i, count) * amp);
            double h     = mid * level;
            if (h < 0.5) h = 0.5;
            ctx.FillRectangle(BrushFor(in frame, i, count, level), new Rect(i * width, mid - h, w, h));

            if (!runners) continue;
            float pv = Math.Min(1f, BarResampler.Level(peaks, i, count) * amp);
            if (pv < 0.02f) continue;
            double py = mid - mid * pv - 2;
            if (py < 0) py = 0;
            ctx.FillRectangle(frame.Palette.Peak, new Rect(i * width, py, w, 2));
            double pyBottom = Math.Min(mid + mid * pv, bounds.Height - 2);
            ctx.FillRectangle(frame.Palette.Peak, new Rect(i * width, pyBottom, w, 2));
        }

        using (ctx.PushOpacity(0.45))
            for (int i = 0; i < count; i++)
            {
                float  level = Math.Min(1f, BarResampler.Level(smooth, i, count) * amp);
                double h     = mid * level;
                if (h < 0.5) h = 0.5;
                ctx.FillRectangle(BrushFor(in frame, i, count, level), new Rect(i * width, mid, w, h));
            }
    }
}
