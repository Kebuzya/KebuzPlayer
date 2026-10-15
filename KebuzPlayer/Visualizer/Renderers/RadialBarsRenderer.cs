using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class RadialBarsRenderer : IVizRenderer
{
    private float[]? _cos;
    private float[]? _sin;
    private int       _cachedCount;
    private VizPalette? _cachedPalette;
    private ImmutablePen[]? _pens;

    public bool IsAnimated => false;

    public void Reset() { }

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        int   count  = Math.Max(1, frame.Settings.BarCount);
        var   smooth = frame.Processor.Smooth;
        var   peaks  = frame.Processor.Peaks;
        float amp    = frame.Amp;
        bool  runners = frame.Settings.ShowPeakRunners;

        if (_cos == null || _cachedCount != count)
        {
            (_cos, _sin) = BuildTables(count);
            _cachedCount = count;
        }

        if (!ReferenceEquals(_cachedPalette, frame.Palette) || _pens == null || _pens.Length != count)
        {
            _cachedPalette = frame.Palette;
            _pens = new ImmutablePen[count];
            for (int i = 0; i < count; i++)
                _pens[i] = new ImmutablePen(frame.Palette.BarBrush(i, count), 2.5);
        }

        var    center = bounds.Center;
        double minHalf = Math.Min(bounds.Width, bounds.Height) / 2;
        double r0   = minHalf * 0.34;
        double span = minHalf * 0.60;

        for (int i = 0; i < count; i++)
        {
            float  level = Math.Min(1f, BarResampler.Level(smooth, i, count) * amp);
            double r1    = r0 + span * Math.Max(level, 0.015f);
            var p0 = new Point(center.X + _cos![i] * r0, center.Y + _sin![i] * r0);
            var p1 = new Point(center.X + _cos[i] * r1, center.Y + _sin[i] * r1);
            ctx.DrawLine(_pens![i], p0, p1);

            if (!runners) continue;
            float pv = Math.Min(1f, BarResampler.Level(peaks, i, count) * amp);
            if (pv < 0.02f) continue;
            double pr = r0 + span * pv + 3;
            ctx.FillRectangle(frame.Palette.Peak,
                new Rect(center.X + _cos![i] * pr - 1, center.Y + _sin![i] * pr - 1, 2, 2));
        }
    }

    private static (float[] Cos, float[] Sin) BuildTables(int count)
    {
        var cos = new float[count];
        var sin = new float[count];
        for (int i = 0; i < count; i++)
        {
            float a = (float)(2 * Math.PI * i / count - Math.PI / 2);
            cos[i] = MathF.Cos(a);
            sin[i] = MathF.Sin(a);
        }
        return (cos, sin);
    }
}
