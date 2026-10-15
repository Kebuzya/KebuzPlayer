using System;
using Avalonia;
using Avalonia.Media;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class LedMatrixRenderer : IVizRenderer
{
    public bool IsAnimated => false;

    public void Reset() { }

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        int cols = Math.Max(1, frame.Settings.BarCount);
        int rows = Math.Clamp((int)(bounds.Height / 9), 3, 24);

        double cellW = bounds.Width / cols;
        double cellH = bounds.Height / rows;
        double padW  = Math.Min(cellW * 0.18, 1.5);
        double padH  = Math.Min(cellH * 0.18, 1.5);

        var   smooth = frame.Processor.Smooth;
        var   peaks  = frame.Processor.Peaks;
        float amp    = frame.Amp;
        bool  runners = frame.Settings.ShowPeakRunners;

        using (ctx.PushOpacity(0.10))
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    ctx.FillRectangle(frame.Palette.BarBrush(x, cols),
                        CellRect(x, y));

        for (int x = 0; x < cols; x++)
        {
            float level = Math.Min(1f, BarResampler.Level(smooth, x, cols) * amp);
            int   lit   = (int)Math.Round(level * rows);
            for (int y = 0; y < lit; y++)
            {
                var brush = frame.Palette.UsesLevelColor
                    ? frame.Palette.LevelBrush((y + 0.5f) / rows)
                    : frame.Palette.BarBrush(x, cols);
                ctx.FillRectangle(brush, CellRect(x, rows - 1 - y));
            }

            if (!runners) continue;
            float pv = Math.Min(1f, BarResampler.Level(peaks, x, cols) * amp);
            int   py = (int)Math.Round(pv * rows);
            if (py <= lit || py < 1) continue;
            ctx.FillRectangle(frame.Palette.Peak, CellRect(x, rows - py));
        }

        Rect CellRect(int x, int y) =>
            new(x * cellW + padW / 2, y * cellH + padH / 2, cellW - padW, cellH - padH);
    }
}
