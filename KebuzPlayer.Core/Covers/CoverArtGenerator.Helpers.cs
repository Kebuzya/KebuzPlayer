using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace KebuzPlayer.Services;

public static partial class CoverArtGenerator
{
    private readonly record struct Contrast(
        float Dr, float BgL, float BgS, float AccentL, float AccentS, float AlphaScale);

    private sealed record Palette(Color[] Stops, Color Ink, Color Paper);

    private static float[] SampleTrajectory(TrackFeatures f, int n, Random rng)
    {
        var sub = new Random(rng.Next());
        var src = f.EnergyTrajectory;
        if (src.Length >= 2) return Interp(src, n);

        var a = new float[n];
        float v = 0.35f + sub.NextSingle() * 0.35f;
        for (int i = 0; i < n; i++)
        {
            v = Math.Clamp(v + (sub.NextSingle() - 0.5f) * 0.22f, 0.15f, 0.85f);
            a[i] = v;
        }
        int peak = sub.Next(n);
        float w = n * 0.16f;
        for (int i = 0; i < n; i++)
            a[i] = MathF.Max(a[i], MathF.Exp(-((i - peak) * (i - peak)) / (2f * w * w)));
        float max = a.Max();
        for (int i = 0; i < n; i++) a[i] /= max;
        return a;
    }

    private static float[] SampleSegments(TrackFeatures f, Func<SegmentFeatures, float> pick,
                                          int n, Random rng, float fbCenter, float fbSpread)
    {
        var sub = new Random(rng.Next());
        if (f.Segments.Length >= 2)
            return Interp(f.Segments.Select(s => Math.Clamp(pick(s), 0f, 1f)).ToArray(), n);

        var a = new float[n];
        float v = Math.Clamp(fbCenter + (sub.NextSingle() - 0.5f) * fbSpread, 0f, 1f);
        for (int i = 0; i < n; i++)
        {
            a[i] = v;
            v = Math.Clamp(v + (sub.NextSingle() - 0.5f) * fbSpread * 0.6f, 0f, 1f);
        }
        return a;
    }

    private static float[] Interp(float[] src, int n)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++)
        {
            float pos = i * (src.Length - 1f) / Math.Max(1, n - 1);
            int lo = (int)pos;
            int hi = Math.Min(lo + 1, src.Length - 1);
            a[i] = src[lo] + (src[hi] - src[lo]) * (pos - lo);
        }
        return a;
    }

    private static float At(float[] a, float t01)
    {
        float pos = Math.Clamp(t01, 0f, 1f) * (a.Length - 1);
        int lo = (int)pos;
        int hi = Math.Min(lo + 1, a.Length - 1);
        return a[lo] + (a[hi] - a[lo]) * (pos - lo);
    }

    private static int ArgMax(float[] a)
    {
        int best = 0;
        for (int i = 1; i < a.Length; i++) if (a[i] > a[best]) best = i;
        return best;
    }

    private static Contrast ContrastFor(TrackFeatures f, Random rng)
    {
        var sub = new Random(rng.Next());
        float drDb = f.IsFallback ? 8f + sub.NextSingle() * 16f : f.DynamicRangeDb;
        float dr = Math.Clamp(drDb / 28f, 0f, 1f);
        return new Contrast(
            dr,
            0.025f + (1f - dr) * 0.075f + sub.NextSingle() * 0.02f,
            0.25f + (1f - dr) * 0.30f,
            Math.Clamp(0.45f + dr * 0.25f + f.Rms * 0.12f, 0f, 0.85f),
            Math.Clamp(0.50f + dr * 0.20f + f.Rms * 0.15f, 0f, 1f),
            0.80f + dr * 0.40f);
    }

    private static int A(int alpha, Contrast c) => Math.Clamp((int)(alpha * c.AlphaScale), 0, 255);

    private static Palette BuildPalette(float hue, TrackFeatures f, Contrast c, Random rng)
    {
        var sub = new Random(rng.Next());
        int scheme = Roulette(sub,
            0.5f + f.HarmonicStability,
            0.5f + (1f - f.HarmonicStability),
            0.35f + f.Spread,
            0.45f);
        float[] offs = scheme switch
        {
            0 => [0f, 18f, 38f, -22f, 55f],
            1 => [0f, 150f + sub.Next(-12, 13), 210f + sub.Next(-12, 13), 24f, 178f],
            2 => [0f, 120f, 240f, 60f, 300f],
            _ => [0f, 28f, 195f, 218f, 12f],
        };
        var stops = new Color[5];
        for (int i = 0; i < 5; i++)
            stops[i] = Hsl((hue + offs[i] + 720f) % 360f,
                Math.Clamp(c.AccentS * (0.80f + sub.NextSingle() * 0.35f), 0f, 1f),
                Math.Clamp(c.AccentL * (0.70f + i * 0.12f), 0.15f, 0.88f));
        var ink   = Hsl(hue, 0.30f, 0.05f + (1f - c.Dr) * 0.04f);
        var paper = Hsl((hue + 42f) % 360f, 0.16f, 0.84f - (1f - c.Dr) * 0.10f);
        return new Palette(stops, ink, paper);
    }

    private static Color PalAt(Palette p, float t)
    {
        float pos = Math.Clamp(t, 0f, 1f) * (p.Stops.Length - 1);
        int lo = (int)pos;
        int hi = Math.Min(lo + 1, p.Stops.Length - 1);
        float fr = pos - lo;
        Color a = p.Stops[lo], b = p.Stops[hi];
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * fr),
            (int)(a.G + (b.G - a.G) * fr),
            (int)(a.B + (b.B - a.B) * fr));
    }

    private static int[] PaletteLut(Palette p, int n = 256)
    {
        var lut = new int[n];
        for (int i = 0; i < n; i++)
            lut[i] = PalAt(p, i / (n - 1f)).ToArgb();
        return lut;
    }

    private static Color Shade(Color col, float k) =>
        Color.FromArgb(col.A,
            Math.Clamp((int)(col.R * k), 0, 255),
            Math.Clamp((int)(col.G * k), 0, 255),
            Math.Clamp((int)(col.B * k), 0, 255));

    private static int Roulette(Random rng, params float[] w)
    {
        float x = rng.NextSingle() * w.Sum();
        for (int i = 0; i < w.Length; i++)
        {
            x -= w[i];
            if (x <= 0f) return i;
        }
        return w.Length - 1;
    }

    private static PointF Jit(Random rng, float x, float y, float amp) =>
        new(x + (rng.NextSingle() - 0.5f) * 2f * amp, y + (rng.NextSingle() - 0.5f) * 2f * amp);

    private static PointF[] WobblyLine(Random rng, PointF a, PointF b, float amp, float step = 14f)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        int n = Math.Max(2, (int)(MathF.Sqrt(dx * dx + dy * dy) / step) + 1);
        var pts = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);
            float x = a.X + dx * t, y = a.Y + dy * t;
            pts[i] = i == 0 || i == n - 1 ? new PointF(x, y) : Jit(rng, x, y, amp);
        }
        return pts;
    }

    private static void WobblyEllipse(Graphics g, Pen pen, float cx, float cy, float r, Random rng, float amp)
    {
        var pts = new PointF[72];
        for (int i = 0; i < 72; i++)
        {
            float a = i * MathF.PI * 2f / 72;
            float rr = r + (rng.NextSingle() - 0.5f) * 2f * amp;
            pts[i] = new PointF(cx + MathF.Cos(a) * rr, cy + MathF.Sin(a) * rr);
        }
        g.DrawPolygon(pen, pts);
    }

    private static void HalftoneRect(Graphics g, RectangleF r, Color col, float spacing, Func<float, float, float> amp01)
    {
        using var br = new SolidBrush(col);
        int rows = (int)(r.Height / spacing) + 2;
        for (int row = 0; row < rows; row++)
        {
            float y = r.Y + row * spacing;
            if (y > r.Bottom) break;
            float xOff = (row & 1) == 0 ? 0f : spacing * 0.5f;
            for (float x = r.X + xOff; x < r.Right; x += spacing)
            {
                float u = (x - r.X) / Math.Max(1f, r.Width);
                float v = (y - r.Y) / Math.Max(1f, r.Height);
                float dotR = spacing * 0.5f * Math.Clamp(amp01(u, v), 0f, 1f);
                if (dotR > 0.4f)
                    g.FillEllipse(br, x - dotR, y - dotR, dotR * 2, dotR * 2);
            }
        }
    }

    private static void ApplyRetroFinish(Bitmap bmp, int seed)
    {
        var rng = new Random(unchecked(seed * 486187739 + 0x5F17));
        var rect = new Rectangle(0, 0, Size, Size);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int n = Size * Size;
        var buf = new int[n];
        Marshal.Copy(data.Scan0, buf, 0, n);
        for (int i = 0; i < n; i++)
        {
            int x = i % Size, y = i / Size;
            int cpx = buf[i];
            float r = (cpx >> 16) & 0xFF, gg = (cpx >> 8) & 0xFF, b = cpx & 0xFF;
            float luma = 0.299f * r + 0.587f * gg + 0.114f * b;
            r += (luma - r) * 0.12f;
            gg += (luma - gg) * 0.12f;
            b += (luma - b) * 0.12f;
            r *= 1.035f;
            b *= 0.955f;
            float nx = (x - Size / 2f) / (Size / 2f), ny = (y - Size / 2f) / (Size / 2f);
            float vig = 1f - 0.20f * Smooth01((nx * nx + ny * ny - 0.50f) / 0.90f);
            r *= vig; gg *= vig; b *= vig;
            float m = 1f - MathF.Abs(luma / 255f * 2f - 1f);
            int d = (int)(rng.Next(-9, 10) * (0.35f + 0.65f * m));
            int ri = Math.Clamp((int)r + d, 0, 255);
            int gi = Math.Clamp((int)gg + d, 0, 255);
            int bi = Math.Clamp((int)b + d, 0, 255);
            buf[i] = (cpx & unchecked((int)0xFF000000)) | (ri << 16) | (gi << 8) | bi;
        }
        Marshal.Copy(buf, 0, data.Scan0, n);
        bmp.UnlockBits(data);
    }

    private static float Smooth01(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
