using System;
using Avalonia;
using Avalonia.Media;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class BarsRenderer : IVizRenderer
{
    public bool IsAnimated => false;

    public void Reset() { }

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        int    count  = Math.Max(1, frame.Settings.BarCount);
        double width  = bounds.Width / count;
        var    smooth = frame.Processor.Smooth;
        var    peaks  = frame.Processor.Peaks;
        float  amp    = frame.Amp;
        bool   runners = frame.Settings.ShowPeakRunners;

        for (int i = 0; i < count; i++)
        {
            float  level = Math.Min(1f, BarResampler.Level(smooth, i, count) * amp);
            double h     = bounds.Height * level;
            if (h < 1) h = 1;
            var brush = frame.Palette.UsesLevelColor
                ? frame.Palette.LevelBrush(level)
                : frame.Palette.BarBrush(i, count);
            ctx.FillRectangle(brush, new Rect(i * width, bounds.Height - h, width * 0.7, h));

            if (!runners) continue;
            float pv = Math.Min(1f, BarResampler.Level(peaks, i, count) * amp);
            if (pv < 0.02f) continue;
            double py = bounds.Height * (1 - pv) - 2;
            if (py < 0) py = 0;
            ctx.FillRectangle(frame.Palette.Peak, new Rect(i * width, py, width * 0.7, 2));
        }
    }
}
