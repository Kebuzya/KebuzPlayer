using System;
using Avalonia;
using Avalonia.Media;

namespace KebuzPlayer.Visualizer.Renderers;

public sealed class ParticlesRenderer : IVizRenderer
{
    private const int PoolSize = 256;
    private const float Gravity = 0.045f;
    private const float LifeStep = 0.028f;

    private struct Particle
    {
        public float X, Y, Vx, Vy, Life;
        public int Bar;
    }

    private readonly Particle[] _pool = new Particle[PoolSize];
    private float[] _prevPeaks = [];
    private readonly Random _rng = new(12345);
    private int _alive;

    public bool IsAnimated => _alive > 0;

    public void Reset()
    {
        _alive = 0;
        Array.Clear(_pool);
        Array.Clear(_prevPeaks);
    }

    public void Render(DrawingContext ctx, Rect bounds, in VizFrame frame)
    {
        int   count  = Math.Max(1, frame.Settings.BarCount);
        double width = bounds.Width / count;
        var   smooth = frame.Processor.Smooth;
        var   peaks  = frame.Processor.Peaks;
        float amp    = frame.Amp;

        if (_prevPeaks.Length != count)
            _prevPeaks = new float[count];

        using (ctx.PushOpacity(0.30))
            for (int i = 0; i < count; i++)
            {
                float  level = Math.Min(1f, BarResampler.Level(smooth, i, count) * amp);
                double h     = bounds.Height * level;
                if (h < 1) h = 1;
                ctx.FillRectangle(frame.Palette.BarBrush(i, count),
                    new Rect(i * width, bounds.Height - h, width * 0.7, h));
            }

        int burst = 1 + (int)(frame.Settings.Intensity * 2.5f);
        for (int i = 0; i < count; i++)
        {
            float pv = Math.Min(1f, BarResampler.Level(peaks, i, count) * amp);
            if (pv > _prevPeaks[i] + 0.035f && pv > 0.08f)
                Spawn(i, (float)(i * width + width * 0.35), (float)(bounds.Height * (1 - pv)), burst);
            _prevPeaks[i] = pv;
        }

        float steps = frame.Steps;
        _alive = 0;
        for (int p = 0; p < PoolSize; p++)
        {
            ref var pt = ref _pool[p];
            if (pt.Life <= 0f) continue;
            pt.X += pt.Vx * steps;
            pt.Y += pt.Vy * steps;
            pt.Vy += Gravity * steps;
            pt.Life -= LifeStep * steps;
            if (pt.Life <= 0f || pt.Y > bounds.Height + 4) { pt.Life = 0f; continue; }
            _alive++;
            double size = 1 + pt.Life * 2.2;
            ctx.FillRectangle(frame.Palette.BarBrush(pt.Bar, count),
                new Rect(pt.X - size / 2, pt.Y - size / 2, size, size));
        }
    }

    private void Spawn(int bar, float x, float y, int burst)
    {
        int spawned = 0;
        for (int p = 0; p < PoolSize && spawned < burst; p++)
        {
            ref var pt = ref _pool[p];
            if (pt.Life > 0f) continue;
            pt.X = x;
            pt.Y = y;
            pt.Vx = (_rng.NextSingle() - 0.5f) * 1.6f;
            pt.Vy = -0.6f - _rng.NextSingle() * 1.8f;
            pt.Life = 0.65f + _rng.NextSingle() * 0.35f;
            pt.Bar = bar;
            spawned++;
        }
        _alive += spawned;
    }
}
