using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace KebuzPlayer.Services;

public enum CoverArtStyle
{
    Mandala, Nebula, SpectrumArt, Mosaic, Waves, Geometry, Radial,
    Plasma, Liquid, Stripes, Tiles, Bokeh, Polygons, Retro,
}

public enum CoverFinish
{
    None, Retro,
}

public static partial class CoverArtGenerator
{
    private const int Size = 400;
    private const CoverFinish DefaultFinish = CoverFinish.Retro;

    public static CoverArtStyle AutoSelectStyle(TrackFeatures f) => AutoSelectStyle(f, 0, 0f);

    public static CoverArtStyle AutoSelectStyle(TrackFeatures f, int seed, float chaos)
    {
        float harm = f.HPRatio;
        float aggr = Math.Clamp(
            0.55f * Math.Clamp(f.OnsetDensity / 5f, 0f, 1f) +
            0.25f * Math.Clamp(f.EnergyCv * 2f, 0f, 1f) +
            0.20f * (1f - f.Autocorrelation), 0f, 1f);

        if (chaos > 0.01f)
        {
            harm = Math.Clamp(harm + (Frac(seed * 0.7548776662466927) - 0.5f) * chaos * 0.7f, 0f, 1f);
            aggr = Math.Clamp(aggr + (Frac(seed * 0.5698402909980532) - 0.5f) * chaos * 0.7f, 0f, 1f);
        }

        var candidates = Zone(harm, aggr);
        return candidates[(int)((uint)seed % (uint)candidates.Length)];
    }

    private static CoverArtStyle[] Zone(float harm, float aggr)
    {
        int h = harm < 0.64f ? 0 : harm < 0.71f ? 1 : 2;
        int a = aggr < 0.26f ? 0 : aggr < 0.35f ? 1 : 2;
        return (h, a) switch
        {
            (0, 0) => [CoverArtStyle.Tiles, CoverArtStyle.Mosaic],
            (0, 1) => [CoverArtStyle.Radial, CoverArtStyle.Mosaic, CoverArtStyle.Polygons],
            (0, 2) => [CoverArtStyle.Retro, CoverArtStyle.Radial, CoverArtStyle.Mandala],
            (1, 0) => [CoverArtStyle.SpectrumArt, CoverArtStyle.Bokeh, CoverArtStyle.Tiles],
            (1, 1) => [CoverArtStyle.Waves, CoverArtStyle.Liquid, CoverArtStyle.Polygons],
            (1, 2) => [CoverArtStyle.Mandala, CoverArtStyle.Geometry, CoverArtStyle.Retro],
            (2, 0) => [CoverArtStyle.Nebula, CoverArtStyle.Bokeh, CoverArtStyle.Plasma],
            (2, 1) => [CoverArtStyle.Plasma, CoverArtStyle.Waves, CoverArtStyle.Stripes],
            _      => [CoverArtStyle.Geometry, CoverArtStyle.Stripes, CoverArtStyle.Mandala],
        };
    }

    public static int FeatureSeed(TrackFeatures f)
    {
        if (f.Segments.Length == 0)
            return StableHash.Combine((int)(f.DurationSec * 10f), (int)(f.Centroid * 1000f));

        var bytes = new byte[f.Segments.Length * 4 + 2];
        int k = 0;
        float energyMax = Math.Max(f.Segments.Max(s => s.Energy), 1e-7f);
        foreach (var s in f.Segments)
        {
            bytes[k++] = Quantize(s.Energy / energyMax);
            bytes[k++] = Quantize(s.Centroid);
            bytes[k++] = Quantize(s.HpRatio);
            bytes[k++] = Quantize(Math.Clamp(s.OnsetRate / 8f, 0f, 1f));
        }
        int dur = (int)(f.DurationSec * 10f);
        bytes[k++] = (byte)dur;
        bytes[k] = (byte)(dur >> 8);
        return StableHash.Fnv1a(bytes);
    }

    private static byte Quantize(float v) => (byte)Math.Clamp((int)(v * 63f), 0, 63);

    public static byte[] Generate(Track track) =>
        Generate(CoverArtStyle.SpectrumArt, DefaultSeedFor(track), null);

    public static byte[] Generate(CoverArtStyle style, int seed, TrackFeatures? features, float chaos = 0f) =>
        Generate(style, seed, features, chaos, DefaultFinish);

    public static byte[] Generate(CoverArtStyle style, int seed, TrackFeatures? features, float chaos, CoverFinish finish)
    {
        chaos = Math.Clamp(chaos, 0f, 1f);
        var   f   = features ?? TrackFeatures.Default;
        var   rng = new Random(SpectralSeed(seed, f.Spectrum.Length > 0 ? f.Spectrum : null));

        float hue     = SpectralHue(f.Spectrum.Length > 0 ? f.Spectrum : null);
        float hueSpan = 50f + chaos * 310f;
        hue = (hue + (Frac(seed * 0.6180339887498949) - 0.5f) * hueSpan + 720f) % 360f;

        float sat = 0.55f + rng.NextSingle() * 0.30f;
        var   accent = Hsl((hue + 150f + rng.NextSingle() * 60f) % 360f, sat, 0.60f);

        using var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);

        if (style is CoverArtStyle.Plasma or CoverArtStyle.Liquid)
        {
            DrawPixelStyle(bmp, style, hue, f, rng);
        }
        else
        {
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode   = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            switch (style)
            {
                case CoverArtStyle.Mandala:     DrawMandala(g, hue, sat, f, rng);          break;
                case CoverArtStyle.Nebula:      DrawNebula(g, hue, f, rng);                break;
                case CoverArtStyle.SpectrumArt: DrawSpectrumArt(g, hue, f, rng);           break;
                case CoverArtStyle.Mosaic:      DrawMosaic(g, hue, sat, f, rng);           break;
                case CoverArtStyle.Waves:       DrawWaves(g, hue, sat, f, rng);            break;
                case CoverArtStyle.Geometry:    DrawGeometry(g, hue, sat, accent, f, rng); break;
                case CoverArtStyle.Radial:      DrawRadial(g, hue, sat, f, rng);           break;
                case CoverArtStyle.Stripes:     DrawStripes(g, hue, f, rng);               break;
                case CoverArtStyle.Tiles:       DrawTiles(g, hue, f, rng);                 break;
                case CoverArtStyle.Bokeh:       DrawBokeh(g, hue, f, rng);                 break;
                case CoverArtStyle.Polygons:    DrawPolygons(g, hue, f, rng);              break;
                case CoverArtStyle.Retro:       DrawRetro(g, hue, sat, f, rng);            break;
            }
        }

        if (chaos > 0.01f) ApplyChaos(bmp, hue, sat, seed, chaos);
        if (finish == CoverFinish.Retro) ApplyRetroFinish(bmp, seed);

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    private static void DrawMandala(Graphics g, float hue, float sat, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var traj = SampleTrajectory(f, 12, rng);
        var segHp = SampleSegments(f, s => s.HpRatio, 12, rng, 0.65f, 0.30f);
        var segOnset = SampleSegments(f, s => Math.Clamp(s.OnsetRate / 8f, 0f, 1f), 12, rng, 0.30f, 0.40f);
        int variant = Roulette(rng, 1f, 0.7f, 0.7f);

        using (var bg = new SolidBrush(Hsl(hue, c.BgS, c.BgL)))
            g.FillRectangle(bg, 0, 0, Size, Size);

        float cx = variant == 1 ? Size * 0.34f : Size / 2f;
        float cy = variant switch { 1 => Size * 0.60f, 2 => Size * 0.99f, _ => Size / 2f };
        float maxR = variant == 2 ? Size * 0.82f : Size * 0.455f;
        float arcStart = variant == 2 ? MathF.PI : 0f;
        float arcSpan = variant == 2 ? MathF.PI : MathF.PI * 2f;

        int rings = 4 + (int)(f.Spread * 4);
        var radii = new float[rings];
        float wSum = 0f;
        for (int k = 0; k < rings; k++) wSum += 0.5f + At(traj, (float)k / (rings - 1));
        float acc = 0f;
        for (int k = 0; k < rings; k++)
        {
            acc += 0.5f + At(traj, (float)k / (rings - 1));
            radii[k] = 30f + acc / wSum * (maxR - 30f);
        }

        var spec = f.Spectrum;
        float maxBin = spec.Length - 1f;
        float globalRot = rng.NextSingle() * MathF.PI * 2f;

        for (int ring = rings - 1; ring >= 0; ring--)
        {
            float t = rings > 1 ? (float)ring / (rings - 1) : 0f;
            float ringR = radii[ring];
            float hp = At(segHp, t);
            int petals = Math.Clamp(10 + (int)(At(segOnset, t) * 16f) + ring * 2, 10, 34);

            float specEnergy = 0.5f;
            if (spec.Length > 0)
            {
                int bin = Math.Clamp((int)MathF.Pow(maxBin, t), 0, spec.Length - 1);
                specEnergy = Math.Clamp(spec[bin], 0.05f, 1f);
            }

            float ringGap = ring == 0 ? radii[0] : radii[ring] - radii[ring - 1];
            float pLen = Math.Max(ringGap * (1.1f + specEnergy * 0.9f), ringR * 0.22f);
            float pWidth = pLen * (0.16f + 0.34f * hp);
            var col = PalAt(pal, t * 0.75f + f.High * 0.25f);
            int alpha = A(150 + (int)(f.Bass * 70), c);

            for (int p = 0; p < petals; p++)
            {
                float angle = arcStart + (p + ring * 0.35f) * arcSpan / petals + globalRot * (variant == 2 ? 0.15f : 1f);
                if (variant == 2 && (angle % (MathF.PI * 2f) + MathF.PI * 2f) % (MathF.PI * 2f) is > 0.05f and < MathF.PI - 0.05f)
                    continue;
                float px = cx + MathF.Cos(angle) * ringR;
                float py = cy + MathF.Sin(angle) * ringR;

                using var brush = new SolidBrush(Color.FromArgb(alpha, col));
                var state = g.Save();
                g.TranslateTransform(px, py);
                g.RotateTransform(angle * 180f / MathF.PI + 90f);
                using var path = new GraphicsPath();
                float cy1 = -pLen * (0.30f + 0.20f * hp);
                float cy2 = -pLen * (0.72f + 0.14f * hp);
                path.AddBezier(0, 0, pWidth, cy1, pWidth, cy2, 0, -pLen);
                path.AddBezier(0, -pLen, -pWidth, cy2, -pWidth, cy1, 0, 0);
                g.FillPath(brush, path);
                using var mis = new Pen(Color.FromArgb(60, pal.Stops[(int)(t * 2 + 2) % 5]), 1f);
                var m = g.Transform.Clone();
                g.TranslateTransform(1.8f, -1.2f, MatrixOrder.Append);
                g.DrawPath(mis, path);
                g.Transform = m;
                g.Restore(state);
            }

            if ((ring & 1) == 0)
            {
                using var inkPen = new Pen(Color.FromArgb(A(70, c), Shade(col, 1.35f)), 1.2f);
                WobblyEllipse(g, inkPen, cx, cy, ringR * 0.92f, rng, 1.5f);
            }
        }

        float cr = 16f + f.Bass * 30f;
        using var cPath = new GraphicsPath();
        cPath.AddEllipse(cx - cr, cy - cr, cr * 2, cr * 2);
        using var cpgb = new PathGradientBrush(cPath);
        cpgb.CenterColor    = Color.FromArgb(A(240, c), Shade(pal.Stops[4], 1.25f));
        cpgb.SurroundColors = [Color.FromArgb(0, pal.Stops[2])];
        g.FillPath(cpgb, cPath);
    }

    private static void DrawNebula(Graphics g, float hue, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var traj = SampleTrajectory(f, 12, rng);
        var segCentroid = SampleSegments(f, s => s.Centroid, 12, rng, 0.55f, 0.30f);
        var segHigh = SampleSegments(f, s => s.High, 12, rng, 0.25f, 0.30f);
        int variant = Roulette(rng, 1f, 0.8f, 0.7f);

        float bgHueShift = 30f + rng.NextSingle() * 60f;
        var bg1 = Hsl(hue, c.BgS + 0.15f, c.BgL);
        var bg2 = Hsl((hue + bgHueShift) % 360f, c.BgS, c.BgL + 0.04f);
        var bgMode = (LinearGradientMode)(rng.Next(4));
        using (var bgb = new LinearGradientBrush(new Rectangle(0, 0, Size, Size), bg1, bg2, bgMode))
            g.FillRectangle(bgb, 0, 0, Size, Size);

        int stars = 90 + (int)(f.High * 200);
        int spikeEvery = 4 + rng.Next(5);
        for (int i = 0; i < stars; i++)
        {
            int sx = rng.Next(Size), sy = rng.Next(Size);
            float br = (0.30f + rng.NextSingle() * 0.70f) * (0.45f + At(segHigh, sx / (Size - 1f)) * 1.3f);
            int alpha = (int)(Math.Clamp(br, 0f, 1f) * 210);
            var starColor = Color.FromArgb(alpha, 210, 215, 255);

            if (i % spikeEvery == 0)
                DrawSpikeStar(g, sx, sy, 2f + rng.NextSingle() * 4f, rng.NextSingle() * MathF.PI * 0.5f, starColor);
            else
            {
                int sz = rng.Next(3);
                using var sb = new SolidBrush(starColor);
                g.FillEllipse(sb, sx, sy, sz + 1, sz + 1);
            }
        }

        var anchors = new PointF[12];
        float cxN = Size / 2f + rng.Next(-35, 35);
        float cyN = Size / 2f + rng.Next(-35, 35);
        if (variant == 0)
        {
            float rot = rng.NextSingle() * MathF.PI * 2f;
            for (int k = 0; k < 12; k++)
            {
                float a = rot + k * 0.55f;
                float r = 36f + k * 14f;
                anchors[k] = new PointF(cxN + MathF.Cos(a) * r, cyN + MathF.Sin(a) * r);
            }
        }
        else if (variant == 1)
        {
            float bandA = rng.NextSingle() * MathF.PI;
            float bw = 30f + f.Spread * 80f;
            for (int k = 0; k < 12; k++)
            {
                float along = (k / 11f - 0.5f) * Size * 1.3f;
                float perp = (rng.NextSingle() - 0.5f) * bw * 2f;
                anchors[k] = new PointF(
                    Size / 2f + MathF.Cos(bandA) * along - MathF.Sin(bandA) * perp,
                    Size / 2f + MathF.Sin(bandA) * along + MathF.Cos(bandA) * perp);
            }
        }
        else
        {
            var c1 = new PointF(Size * 0.33f, Size * 0.40f);
            var c2 = new PointF(Size * 0.70f, Size * 0.66f);
            for (int k = 0; k < 12; k++)
            {
                var b = (k & 1) == 0 ? c1 : c2;
                anchors[k] = new PointF(
                    b.X + (rng.NextSingle() - 0.5f) * 130f,
                    b.Y + (rng.NextSingle() - 0.5f) * 130f);
            }
        }

        for (int k = 0; k < 12; k++)
        {
            float bs = 28f + traj[k] * 95f + rng.Next(20);
            var bc = PalAt(pal, segCentroid[k]);
            using var path = new GraphicsPath();
            path.AddEllipse(anchors[k].X - bs, anchors[k].Y - bs, bs * 2, bs * 2);
            using var pgb = new PathGradientBrush(path);
            pgb.CenterColor    = Color.FromArgb(A(30 + (int)(traj[k] * 80f), c), bc);
            pgb.SurroundColors = [Color.FromArgb(0, bc)];
            g.FillPath(pgb, path);
        }

        int glows = 4 + (int)(f.High * 8);
        for (int i = 0; i < glows; i++)
        {
            int sx = rng.Next(20, Size - 20), sy = rng.Next(20, Size - 20);
            var gc = Shade(PalAt(pal, rng.NextSingle()), 1.5f);
            using var path = new GraphicsPath();
            path.AddEllipse(sx - 12, sy - 12, 24, 24);
            using var pgb = new PathGradientBrush(path);
            pgb.CenterColor    = Color.FromArgb(A(145, c), gc);
            pgb.SurroundColors = [Color.FromArgb(0, gc)];
            g.FillPath(pgb, path);
            using var core = new SolidBrush(Color.FromArgb(230, Color.White));
            g.FillEllipse(core, sx - 1, sy - 1, 3, 3);
        }

        int cStars = Math.Clamp(4 + f.Peaks / 4, 4, 10);
        var cPts = new PointF[cStars];
        for (int i = 0; i < cStars; i++)
            cPts[i] = new PointF(40f + rng.NextSingle() * (Size - 80f), 40f + rng.NextSingle() * (Size - 80f));
        using (var linePen = new Pen(Color.FromArgb(A(100, c), Shade(pal.Paper, 1.05f)), 1f))
            g.DrawLines(linePen, cPts);
        using (var jointPen = new Pen(Color.FromArgb(A(160, c), Shade(pal.Paper, 1.1f)), 1f))
            foreach (var p in cPts)
                g.DrawEllipse(jointPen, p.X - 2.5f, p.Y - 2.5f, 5f, 5f);

        int peak = ArgMax(traj);
        float coreR = 38f + f.Bass * 80f;
        using var corePath = new GraphicsPath();
        corePath.AddEllipse(anchors[peak].X - coreR, anchors[peak].Y - coreR, coreR * 2, coreR * 2);
        using var corePgb = new PathGradientBrush(corePath);
        corePgb.CenterColor    = Color.FromArgb(A(70 + (int)(f.Bass * 70), c), Shade(pal.Stops[3], 1.2f));
        corePgb.SurroundColors = [Color.FromArgb(0, pal.Stops[1])];
        g.FillPath(corePgb, corePath);
    }

    private static void DrawSpectrumArt(Graphics g, float hue, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var traj = SampleTrajectory(f, 12, rng);
        int variant = Roulette(rng, 1f, 0.55f + f.Spread, 0.55f + f.HarmonicStability,
            0.55f + Math.Clamp(f.OnsetDensity / 5f, 0f, 1f));

        var bgCol = Hsl(hue, c.BgS, c.BgL);
        var bgB = Hsl((hue + 28f) % 360f, c.BgS, c.BgL + 0.05f);
        using (var bgb = new LinearGradientBrush(new Rectangle(0, 0, Size, Size), bgCol, bgB,
                             LinearGradientMode.Vertical))
            g.FillRectangle(bgb, 0, 0, Size, Size);

        var src = BuildBars(f, 80, rng);

        switch (variant)
        {
            case 0:  SpectrumLandscape(g, c, pal, f, src, traj, rng);  break;
            case 1:  SpectrumPolar(g, c, pal, f, src, traj, rng);      break;
            case 2:  SpectrumRidgeline(g, c, pal, f, bgCol, rng);      break;
            default: SpectrumLed(g, c, pal, f, src, traj, rng);        break;
        }
    }

    private static float[] BuildBars(TrackFeatures f, int n, Random rng)
    {
        var sub = new Random(rng.Next());
        var src = new float[n];
        var disp = f.DisplaySpectrum;
        if (disp.Length >= 20)
        {
            float mb = disp.Length - 1f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1);
                int si = Math.Clamp((int)MathF.Pow(mb, t), 0, disp.Length - 1);
                src[i] = disp[si];
            }
        }
        else
        {
            float v = 0.25f + sub.NextSingle() * 0.40f;
            for (int i = 0; i < n; i++)
            {
                v = Math.Clamp(v + (sub.NextSingle() - 0.52f) * 0.18f, 0.04f, 0.88f);
                src[i] = v;
            }
        }
        return src;
    }

    private static void SpectrumLandscape(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] src, float[] traj, Random rng)
    {
        if (rng.NextSingle() > 0.5f)
        {
            using var slPen = new Pen(Color.FromArgb(18, 0, 0, 0), 1f);
            for (int sy = 0; sy < Size; sy += 3)
                g.DrawLine(slPen, 0, sy, Size, sy);
        }

        bool mirrored = rng.NextSingle() > 0.5f;
        float horizLine = mirrored ? Size / 2f : Size;

        for (int layer = 2; layer >= 0; layer--)
        {
            float yLift = layer * Size * 0.05f;
            float maxFrac = 0.28f + (2 - layer) * 0.25f;
            var lCol = pal.Stops[2 - layer];
            int smooth = 1 + layer * 3;

            var sm = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float acc = 0f;
                int cnt = 0;
                for (int k = -smooth; k <= smooth; k++)
                {
                    acc += src[Math.Clamp(i + k, 0, src.Length - 1)];
                    cnt++;
                }
                sm[i] = acc / cnt;
            }

            var pts = new PointF[src.Length + 2];
            pts[0] = new PointF(0, horizLine);
            for (int i = 0; i < src.Length; i++)
            {
                float u = (float)i / (src.Length - 1);
                float x = u * (Size - 1);
                float nv = Math.Clamp(sm[i] * (0.45f + 0.85f * At(traj, u)), 0.04f, 1.1f);
                float y = horizLine - yLift - nv * maxFrac * horizLine + (rng.NextSingle() - 0.5f) * 2f;
                pts[i + 1] = new PointF(x, y);
            }
            pts[^1] = new PointF(Size, horizLine);

            using var br = new SolidBrush(Color.FromArgb(A(130 + layer * 35, c), Shade(lCol, 0.45f)));
            g.FillPolygon(br, pts);

            using var pen = new Pen(Color.FromArgb(A(120 + layer * 45, c), lCol), 1.5f);
            g.DrawLines(pen, pts[1..^1]);

            if (layer == 0)
            {
                var mis = pts[1..^1].Select(p => new PointF(p.X + 2.5f, p.Y - 1.5f)).ToArray();
                using var misPen = new Pen(Color.FromArgb(70, pal.Stops[3]), 1.2f);
                g.DrawLines(misPen, mis);
                using var glowPen = new Pen(Color.FromArgb(A(90, c), Shade(lCol, 1.5f)), 3f);
                g.DrawLines(glowPen, pts[1..^1]);
            }

            if (mirrored)
            {
                var mpts = new PointF[src.Length + 2];
                mpts[0] = new PointF(0, horizLine);
                for (int i = 0; i < src.Length; i++)
                    mpts[i + 1] = new PointF(pts[i + 1].X, horizLine + (horizLine - pts[i + 1].Y));
                mpts[^1] = new PointF(Size, horizLine);

                using var mbr = new SolidBrush(Color.FromArgb(A(70 + layer * 18, c), Shade(lCol, 0.30f)));
                g.FillPolygon(mbr, mpts);
            }
        }

        float glowR = Size * (0.35f + f.Bass * 0.15f);
        float glowY = mirrored ? Size / 2f : Size * 0.75f;
        using var glowPath = new GraphicsPath();
        glowPath.AddEllipse(Size / 2f - glowR, glowY - glowR, glowR * 2, glowR * 2);
        using var pgb = new PathGradientBrush(glowPath);
        pgb.CenterColor    = Color.FromArgb(A(55 + (int)(f.Rms * 60), c), pal.Stops[2]);
        pgb.SurroundColors = [Color.FromArgb(0, pal.Stops[1])];
        g.FillPath(pgb, glowPath);
    }

    private static void SpectrumPolar(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] src, float[] traj, Random rng)
    {
        var segOnset = SampleSegments(f, s => Math.Clamp(s.OnsetRate / 8f, 0f, 1f), 12, rng, 0.30f, 0.40f);
        float cx = Size / 2f + (rng.NextSingle() - 0.5f) * 60f;
        float cy = Size / 2f + (rng.NextSingle() - 0.5f) * 60f;
        float baseR = Size * 0.16f + f.Bass * Size * 0.08f;
        const int n = 240;

        var copies = new[] { (0f, 200), (6f, 110), (-4f, 70) };
        for (int j = copies.Length - 1; j >= 0; j--)
        {
            var (rotDeg, alpha) = copies[j];
            float rot = rotDeg * MathF.PI / 180f;
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ang = t * MathF.PI * 2f + rot;
                float fold = t < 0.5f ? t * 2f : (1f - t) * 2f;
                float sv = At(src, fold);
                float r = baseR * (0.75f + 0.5f * At(traj, t)) + sv * Size * 0.18f;
                pts[i] = new PointF(cx + MathF.Cos(ang) * r, cy + MathF.Sin(ang) * r);
            }
            using var br = new SolidBrush(Color.FromArgb(A(alpha, c), pal.Stops[j]));
            g.FillPolygon(br, pts);
            using var pen = new Pen(Color.FromArgb(A(alpha + 40, c), Shade(pal.Stops[j], 1.3f)), 1.5f);
            g.DrawPolygon(pen, pts);
        }

        for (int k = 0; k < 12; k++)
        {
            float ang = k / 12f * MathF.PI * 2f;
            float len = 12f + segOnset[k] * 30f;
            float r0 = baseR * 0.55f;
            using var pen = new Pen(Color.FromArgb(A(170, c), Shade(pal.Stops[4], 1.2f)), 1.6f);
            g.DrawLine(pen,
                cx + MathF.Cos(ang) * r0, cy + MathF.Sin(ang) * r0,
                cx + MathF.Cos(ang) * (r0 + len), cy + MathF.Sin(ang) * (r0 + len));
        }

        float discR = baseR * 0.42f;
        using var disc = new SolidBrush(pal.Ink);
        g.FillEllipse(disc, cx - discR, cy - discR, discR * 2, discR * 2);
        float dotR = 3f + f.Rms * 6f;
        using var dot = new SolidBrush(Color.FromArgb(A(230, c), pal.Stops[4]));
        g.FillEllipse(dot, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
    }

    private static void SpectrumRidgeline(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, Color bgCol, Random rng)
    {
        const int rows = 14;
        const int pts = 90;
        var trajR = SampleTrajectory(f, rows, rng);
        var segBass = SampleSegments(f, s => s.Bass, rows, rng, 0.40f, 0.30f);
        var segMid = SampleSegments(f, s => s.Mid, rows, rng, 0.35f, 0.30f);
        var segHigh = SampleSegments(f, s => s.High, rows, rng, 0.25f, 0.30f);
        var segSpread = SampleSegments(f, s => s.Spread, rows, rng, 0.40f, 0.30f);
        var segFlux = SampleSegments(f, s => Math.Clamp(s.Flux * 2.5f, 0f, 1f), rows, rng, 0.40f, 0.40f);

        float mu1 = 0.22f + (rng.NextSingle() - 0.5f) * 0.10f;
        float mu2 = 0.52f + (rng.NextSingle() - 0.5f) * 0.10f;
        float mu3 = 0.80f + (rng.NextSingle() - 0.5f) * 0.10f;
        bool palLine = rng.NextSingle() > 0.6f;

        static float Gauss(float x, float mu, float sg) =>
            MathF.Exp(-(x - mu) * (x - mu) / (2f * sg * sg));

        for (int k = 0; k < rows; k++)
        {
            float y0 = 74f + k * (Size - 120f) / (rows - 1);
            float sg = 0.10f + segSpread[k] * 0.08f;
            float noise = 0f;
            var line = new PointF[pts];
            for (int i = 0; i < pts; i++)
            {
                float u = i / (pts - 1f);
                float env = segBass[k] * Gauss(u, mu1, sg)
                          + segMid[k] * Gauss(u, mu2, sg)
                          + segHigh[k] * Gauss(u, mu3, sg);
                noise = Math.Clamp(noise + (rng.NextSingle() - 0.5f) * (0.15f + 0.45f * segFlux[k]), -0.6f, 0.6f);
                float h = MathF.Max(0f, (env * 1.15f + noise * 0.35f) * trajR[k] * 78f);
                line[i] = new PointF(22f + u * (Size - 44f), y0 - h);
            }

            var poly = new PointF[pts + 2];
            poly[0] = new PointF(22f, Size);
            Array.Copy(line, 0, poly, 1, pts);
            poly[^1] = new PointF(Size - 22f, Size);
            using var fill = new SolidBrush(bgCol);
            g.FillPolygon(fill, poly);

            var lineCol = palLine ? PalAt(pal, k / (rows - 1f)) : Shade(pal.Paper, 1.05f);
            using var pen = new Pen(Color.FromArgb(A(220, c), lineCol), 1.6f);
            g.DrawLines(pen, line);
        }
    }

    private static void SpectrumLed(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] src, float[] traj, Random rng)
    {
        const int cols = 16, rows = 12, gap = 3;
        const float margin = 30f;
        float panelH = Size - margin * 2 - 24f;
        float cellW = (Size - margin * 2 - (cols - 1) * gap) / cols;
        float cellH = (panelH - (rows - 1) * gap) / rows;
        bool ladder = rng.NextSingle() < 0.35f;

        using (var bezel = new SolidBrush(Shade(pal.Ink, 1.5f)))
            g.FillRectangle(bezel, margin - 12f, margin - 12f, Size - (margin - 12f) * 2, Size - (margin - 12f) * 2);
        using (var face = new SolidBrush(Shade(pal.Ink, 0.7f)))
            g.FillRectangle(face, margin - 4f, margin - 4f, Size - (margin - 4f) * 2, Size - (margin - 4f) * 2);

        var levels = new int[cols];
        var barsPerCol = src.Length / cols;
        for (int col = 0; col < cols; col++)
        {
            float mx = 0f;
            for (int b = col * barsPerCol; b < (col + 1) * barsPerCol; b++)
                mx = MathF.Max(mx, src[Math.Min(b, src.Length - 1)]);
            levels[col] = Math.Clamp((int)MathF.Round(mx * rows), 1, rows);
        }

        for (int col = 0; col < cols; col++)
        {
            float x = margin + col * (cellW + gap);
            for (int row = 0; row < rows; row++)
            {
                float y = margin + panelH - cellH - row * (cellH + gap);
                bool lit = row < levels[col];
                Color cellCol = ladder
                    ? Hsl(118f - row * 10.5f, 0.80f, 0.52f)
                    : PalAt(pal, row / (rows - 1f));
                if (lit)
                {
                    using var br = new SolidBrush(Color.FromArgb(A(230, c), cellCol));
                    g.FillRectangle(br, x, y, cellW, cellH);
                    using var hl = new Pen(Color.FromArgb(A(70, c), Shade(cellCol, 1.5f)), 1f);
                    g.DrawLine(hl, x + 1, y + 1, x + cellW - 1, y + 1);
                }
                else
                {
                    using var br = new SolidBrush(Color.FromArgb(26, cellCol));
                    g.FillRectangle(br, x, y, cellW, cellH);
                }
            }

            int peak = Math.Min(levels[col] + 1 + rng.Next(2), rows - 1);
            float py = margin + panelH - cellH - peak * (cellH + gap);
            Color pkCol = ladder ? Hsl(118f - peak * 10.5f, 0.85f, 0.62f) : Shade(PalAt(pal, peak / (rows - 1f)), 1.4f);
            using var pk = new SolidBrush(Color.FromArgb(A(200, c), pkCol));
            g.FillRectangle(pk, x, py, cellW, Math.Max(2f, cellH * 0.4f));
        }

        float stripY = margin + panelH + 8f;
        float stripW = (Size - margin * 2 - 11f * gap) / 12f;
        for (int k = 0; k < 12; k++)
        {
            float x = margin + k * (stripW + gap);
            using var br = new SolidBrush(Color.FromArgb(A((int)(40 + traj[k] * 200), c), pal.Stops[3]));
            g.FillRectangle(br, x, stripY, stripW, 12f);
        }

        using var slPen = new Pen(Color.FromArgb(14, 0, 0, 0), 1f);
        for (int sy = (int)margin; sy < margin + panelH; sy += 3)
            g.DrawLine(slPen, margin, sy, Size - margin, sy);
    }

    private static void DrawMosaic(Graphics g, float hue, float sat, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var segHp = SampleSegments(f, s => s.HpRatio, 12, rng, 0.65f, 0.30f);
        int variant = Roulette(rng, 1f, 0.9f, 0.7f);

        using (var bg = new SolidBrush(Hsl(hue, c.BgS, c.BgL)))
            g.FillRectangle(bg, 0, 0, Size, Size);

        var disp = f.DisplaySpectrum;
        float mb = Math.Max(1f, disp.Length - 1f);

        float CellEnergy(float u, float trajV)
        {
            float e = disp.Length > 0
                ? disp[Math.Clamp((int)MathF.Pow(mb, u), 0, disp.Length - 1)]
                : 0.3f + rng.NextSingle() * 0.6f;
            return Math.Clamp(e * (0.35f + 0.85f * trajV), 0.04f, 1f);
        }

        int ShapeFor(float hp, Random r) =>
            hp > 0.62f ? (r.NextSingle() < 0.5f ? 2 : 1)
            : hp < 0.38f ? (r.NextSingle() < 0.5f ? 0 : 4)
            : 3;

        if (variant == 2)
        {
            var trajP = SampleTrajectory(f, 12, rng);
            float cx = Size / 2f, cy = Size / 2f;
            int sectors = Math.Clamp(18 + f.Peaks, 18, 44);
            float ringW = Size * 0.72f / 12f;
            for (int ring = 11; ring >= 0; ring--)
            {
                float r1 = 22f + ring * ringW;
                float sweep = 360f / sectors;
                int shape = ShapeFor(segHp[ring], rng);
                for (int s = 0; s < sectors; s++)
                {
                    float u = s / (sectors - 1f);
                    float energy = CellEnergy(u, trajP[ring]);
                    if (energy < 0.08f) continue;
                    float ang = (s * sweep + ring * 4f) * MathF.PI / 180f;
                    float rr = r1 + ringW / 2f;
                    float px = cx + MathF.Cos(ang) * rr;
                    float py = cy + MathF.Sin(ang) * rr;
                    float hw = ringW * 0.42f * (0.4f + energy * 0.7f);
                    var colr = Shade(PalAt(pal, u), 0.5f + energy * 0.75f);
                    using var br = new SolidBrush(Color.FromArgb(A((int)(80 + energy * 170), c), colr));
                    var st = g.Save();
                    g.TranslateTransform(px, py);
                    g.RotateTransform(ang * 180f / MathF.PI);
                    DrawShape(g, br, shape, hw);
                    g.Restore(st);
                }
            }
            return;
        }

        float cellSize = Math.Clamp(10f + f.Bass * 28f - f.High * 12f, 10f, 36f);
        int cols = (int)MathF.Ceiling(Size / cellSize) + 1;
        int rows = (int)MathF.Ceiling(Size / cellSize) + 1;
        float gap = Math.Max(1.5f, cellSize * 0.10f);
        var traj = SampleTrajectory(f, rows, rng);
        float baseRot = variant == 0 ? rng.NextSingle() * MathF.PI : 0f;
        float skipP = variant == 1 ? 0.04f + f.EnergyCv * 0.10f : 0f;

        for (int row = 0; row < rows; row++)
        {
            int shape = ShapeFor(segHp[row % 12], rng);
            float trajV = traj[row % traj.Length];
            for (int col = 0; col < cols; col++)
            {
                if (skipP > 0f && rng.NextSingle() < skipP) continue;
                float u = col / (cols - 1f);
                float energy = CellEnergy(u, trajV);

                float xOff = variant == 1 && (row & 1) == 1 ? cellSize * 0.5f : 0f;
                float cx2 = col * cellSize + cellSize / 2f + xOff + (rng.NextSingle() - 0.5f) * 3f;
                float cy2 = row * cellSize + cellSize / 2f + (rng.NextSingle() - 0.5f) * 3f;
                float hw = (cellSize - gap) / 2f * (0.55f + energy * 0.55f);
                float rot = variant == 1
                    ? (rng.NextSingle() - 0.5f) * 12f
                    : baseRot * 180f / MathF.PI;

                var colr = Shade(PalAt(pal, Math.Clamp(u + (rng.NextSingle() - 0.5f) * 0.08f, 0f, 1f)),
                    0.5f + energy * 0.75f);
                using var brush = new SolidBrush(Color.FromArgb(A((int)(80 + energy * 170), c), colr));
                var state = g.Save();
                g.TranslateTransform(cx2, cy2);
                g.RotateTransform(rot);
                DrawShape(g, brush, shape, hw);
                g.Restore(state);
            }
        }
    }

    private static void DrawWaves(Graphics g, float hue, float sat, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var traj = SampleTrajectory(f, 24, rng);
        int variant = Roulette(rng, 1f, 0.7f, 0.7f);

        using (var bg = new SolidBrush(Hsl(hue, c.BgS, c.BgL)))
            g.FillRectangle(bg, 0, 0, Size, Size);

        if (variant == 1)
        {
            WaveRipples(g, c, pal, f, traj, rng);
            return;
        }

        if (variant == 2)
        {
            WaveBandSet(g, c, pal, f, traj, rng, 18f + rng.NextSingle() * 8f, 0.55f);
            WaveBandSet(g, c, pal, f, traj, rng, -(22f + rng.NextSingle() * 8f), 0.45f);
        }
        else
            WaveBandSet(g, c, pal, f, traj, rng, (rng.NextSingle() - 0.5f) * 44f, 1f);
    }

    private static void WaveBandSet(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] traj, Random rng, float tiltDeg, float alphaScale)
    {
        var canvasState = g.Save();
        g.TranslateTransform(Size / 2f, Size / 2f);
        g.RotateTransform(tiltDeg);
        g.TranslateTransform(-Size / 2f, -Size / 2f);

        var spec = f.Spectrum;
        float maxBin = spec.Length - 1f;
        int waves = Math.Clamp(4 + (int)(f.Spread * 6), 4, 10);
        float foam = Math.Clamp(f.OnsetDensity / 5f, 0f, 1f);

        const float extend = 110f;
        float span = Size + 2 * extend;
        float bandH = span / waves;
        const int pts = 120;

        for (int w = 0; w < waves; w++)
        {
            float t = waves > 1 ? (float)w / (waves - 1) : 0f;
            float mid = -extend + w * bandH + bandH * 0.5f;

            float waveEnergy = f.Bass;
            if (spec.Length > 0)
            {
                int lo = Math.Clamp((int)(maxBin * w / waves), 0, spec.Length - 1);
                int hi = Math.Clamp((int)(maxBin * (w + 1) / waves), 0, spec.Length - 1);
                if (hi > lo)
                {
                    float sum = 0f;
                    for (int b = lo; b <= hi; b++) sum += spec[b] * spec[b];
                    waveEnergy = MathF.Sqrt(sum / (hi - lo + 1));
                }
            }

            float amp = bandH * (0.20f + waveEnergy * 0.50f);
            float freq = 0.012f + rng.NextSingle() * 0.016f;
            float phase = rng.NextSingle() * MathF.PI * 2f;
            float phase2 = rng.NextSingle() * MathF.PI * 2f;
            var col = PalAt(pal, (t * 0.6f + (w & 1) * 0.35f) % 1f);

            var topPts = new PointF[pts];
            for (int xi = 0; xi < pts; xi++)
            {
                float x = -extend + xi * span / (pts - 1);
                float u = Math.Clamp((x + extend) / span, 0f, 1f);
                float env = 0.45f + 0.75f * At(traj, u);
                float y = mid
                    + (0.7f * MathF.Sin(x * freq + phase) + 0.3f * MathF.Sin(x * freq * 2.7f + phase2))
                    * amp * env
                    + (rng.NextSingle() - 0.5f) * 2.4f;
                topPts[xi] = new PointF(x, y);
            }

            var poly = new PointF[pts + 2];
            poly[0] = new PointF(-extend, Size + extend);
            Array.Copy(topPts, 0, poly, 1, pts);
            poly[^1] = new PointF(Size + extend, Size + extend);

            using var brush = new SolidBrush(Color.FromArgb(A((int)(205 * alphaScale), c), col));
            g.FillPolygon(brush, poly);
            using var pen = new Pen(Color.FromArgb(A((int)(170 * alphaScale), c), Shade(col, 1.35f)), 1.5f);
            g.DrawLines(pen, topPts);
            var mis = topPts.Select(p => new PointF(p.X + 2f, p.Y - 1.6f)).ToArray();
            using var misPen = new Pen(Color.FromArgb((int)(60 * alphaScale), pal.Stops[4]), 1f);
            g.DrawLines(misPen, mis);

            int foamDots = (int)(foam * 26f * alphaScale);
            using var foamBr = new SolidBrush(Color.FromArgb(A(180, c), Shade(pal.Paper, 1.05f)));
            for (int d = 0; d < foamDots; d++)
            {
                var p = topPts[rng.Next(pts)];
                float fr = 1f + rng.NextSingle() * 2f;
                g.FillEllipse(foamBr, p.X + (rng.NextSingle() - 0.5f) * 8f, p.Y - rng.NextSingle() * 6f, fr, fr);
            }
        }
        g.Restore(canvasState);
    }

    private static void WaveRipples(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] traj, Random rng)
    {
        float cx = Size * (0.25f + rng.NextSingle() * 0.5f);
        float cy = Size * (0.25f + rng.NextSingle() * 0.5f);
        int rings = Math.Clamp(7 + (int)(f.Spread * 7), 7, 15);
        float maxR = Size * 0.95f;
        const int pts = 100;

        for (int k = rings - 1; k >= 0; k--)
        {
            float t = (float)k / (rings - 1);
            float baseR = 18f + t * maxR;
            int lobes = 2 + rng.Next(4);
            float ph = rng.NextSingle() * MathF.PI * 2f;
            float wob = 3f + At(traj, t) * 16f;

            var ring = new PointF[pts];
            for (int i = 0; i < pts; i++)
            {
                float a = i * MathF.PI * 2f / pts;
                float r = baseR + MathF.Sin(a * lobes + ph) * wob + (rng.NextSingle() - 0.5f) * 2f;
                ring[i] = new PointF(cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r);
            }

            var col = PalAt(pal, (t * 0.55f + (k & 1) * 0.40f) % 1f);
            using var fill = new SolidBrush(Color.FromArgb(A(120 + (int)(At(traj, t) * 90f), c), col));
            g.FillPolygon(fill, ring);
            using var pen = new Pen(Color.FromArgb(A(160, c), Shade(col, 1.4f)), 1.6f);
            g.DrawPolygon(pen, ring);
        }

        float dotR = 4f + f.Bass * 8f;
        using var dot = new SolidBrush(Color.FromArgb(A(230, c), pal.Stops[4]));
        g.FillEllipse(dot, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
    }

    private static void DrawGeometry(Graphics g, float hue, float sat, Color accent, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var traj = SampleTrajectory(f, 12, rng);
        var segFlux = SampleSegments(f, s => Math.Clamp(s.Flux * 2.5f, 0f, 1f), 12, rng, 0.40f, 0.40f);
        int variant = Roulette(rng, 1f, 0.7f, 0.8f);

        using (var bg = new SolidBrush(Hsl(hue, c.BgS, c.BgL)))
            g.FillRectangle(bg, 0, 0, Size, Size);

        if (variant == 2)
        {
            GeometryIsoCity(g, c, pal, f, traj, rng);
            return;
        }

        if (variant == 1)
        {
            var blocks = new List<RectangleF>();
            int bc = 2 + rng.Next(2);
            float bx = 0f;
            for (int i = 0; i < bc; i++)
            {
                float bw = (Size - bx) / (bc - i) * (0.75f + rng.NextSingle() * 0.5f);
                if (i == bc - 1) bw = Size - bx;
                float split = Size * (0.35f + rng.NextSingle() * 0.3f);
                blocks.Add(new RectangleF(bx, 0, bw, split));
                blocks.Add(new RectangleF(bx, split, bw, Size - split));
                bx += bw;
            }
            foreach (var b in blocks)
            {
                var st = g.Save();
                g.SetClip(b);
                GeometryLattice(g, c, pal, f, traj, segFlux, rng, b);
                g.Restore(st);
                using var pen = new Pen(Color.FromArgb(A(90, c), pal.Ink), 2f);
                g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);
            }
        }
        else
            GeometryLattice(g, c, pal, f, traj, segFlux, rng, new RectangleF(0, 0, Size, Size));

        using var glowPath = new GraphicsPath();
        glowPath.AddEllipse(Size * 0.1f, Size * 0.1f, Size * 0.8f, Size * 0.8f);
        using var pgb = new PathGradientBrush(glowPath);
        pgb.CenterColor    = Color.FromArgb(A(45, c), accent);
        pgb.SurroundColors = [Color.FromArgb(0, accent)];
        g.FillPath(pgb, glowPath);
    }

    private static void GeometryLattice(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] traj, float[] segFlux, Random rng, RectangleF area)
    {
        var disp = f.DisplaySpectrum;
        float maxBin = Math.Max(1f, disp.Length - 1f);
        float globalRot = rng.NextSingle() * 180f;
        int baseShape = (int)(f.Centroid * 5f + rng.NextSingle() * 3f) % 5;

        float tileSize = Math.Clamp(25f + (1f - f.Spread) * 35f, 25f, 60f) * (0.8f + rng.NextSingle() * 0.4f);
        float spacing = tileSize * 1.15f;
        int cols = (int)MathF.Ceiling(area.Width / spacing) + 2;
        int rows = (int)MathF.Ceiling(area.Height / spacing) + 2;
        int heroRow = (int)((float)ArgMax(traj) / (traj.Length - 1) * (rows - 1));
        int heroCol = rng.Next(cols);

        for (int row = 0; row < rows; row++)
        {
            float v = rows > 1 ? (float)row / (rows - 1) : 0f;
            float trajV = At(traj, v);
            float rowRot = globalRot + row * At(segFlux, v) * 18f;
            int shapeKind = (baseShape + (row & 1) * 2 + row % 3) % 5;
            for (int col = 0; col < cols; col++)
            {
                float xOff = row % 2 == 0 ? 0 : spacing * 0.5f;
                float cx = area.X - spacing + col * spacing + xOff;
                float cy = area.Y - spacing + row * spacing;

                float u = cols > 1 ? (float)col / (cols - 1) : 0f;
                float specJit = 0.55f + rng.NextSingle() * 0.45f;
                if (disp.Length > 0)
                {
                    int bin = Math.Clamp((int)MathF.Pow(maxBin, u), 0, disp.Length - 1);
                    specJit = 0.35f + disp[bin] * 0.65f;
                }

                bool hero = row == heroRow && col == heroCol;
                float halfS = tileSize * 0.5f * specJit * (0.40f + 0.85f * trajV) * (hero ? 2.2f : 1f);
                var col3 = hero ? pal.Stops[4] : Shade(PalAt(pal, u), 0.55f + specJit * 0.6f);
                int al = A(hero ? 235 : 140 + (int)(specJit * 90), c);

                using var br = new SolidBrush(Color.FromArgb(al, col3));
                var state = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(rowRot);
                DrawShape(g, br, shapeKind, halfS);
                if (hero)
                {
                    using var mis = new Pen(Color.FromArgb(120, pal.Ink), 1.6f);
                    g.TranslateTransform(2.5f, -2f);
                    DrawShapeOutline(g, mis, shapeKind, halfS);
                }
                g.Restore(state);
            }
        }
    }

    private static void GeometryIsoCity(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] traj, Random rng)
    {
        var disp = f.DisplaySpectrum;
        float maxBin = Math.Max(1f, disp.Length - 1f);
        const int cols = 9, rows = 7;
        float cw = Size / (cols - 1.2f);
        float ch = cw * 0.5f;

        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            float u = (float)col / (cols - 1);
            float v = (float)row / (rows - 1);
            float e = disp.Length > 0
                ? disp[Math.Clamp((int)MathF.Pow(maxBin, u), 0, disp.Length - 1)]
                : 0.3f + rng.NextSingle() * 0.6f;
            float h = (12f + e * At(traj, v) * 110f) * (0.8f + rng.NextSingle() * 0.4f);

            float ix = col * cw * 0.92f + (row % 2 == 0 ? 0 : cw * 0.46f) - cw * 0.3f;
            float iy = 60f + row * (Size - 90f) / (rows - 1);

            var baseCol = PalAt(pal, u);
            var top = new[]
            {
                new PointF(ix, iy - h),
                new PointF(ix + cw * 0.4f, iy - h + ch * 0.4f),
                new PointF(ix, iy - h + ch * 0.8f),
                new PointF(ix - cw * 0.4f, iy - h + ch * 0.4f),
            };
            var left = new[]
            {
                top[3], top[2],
                new PointF(top[2].X, top[2].Y + h),
                new PointF(top[3].X, top[3].Y + h),
            };
            var right = new[]
            {
                top[2], top[1],
                new PointF(top[1].X, top[1].Y + h),
                new PointF(top[2].X, top[2].Y + h),
            };

            using (var lb = new SolidBrush(Color.FromArgb(A(220, c), Shade(baseCol, 0.45f))))
                g.FillPolygon(lb, left);
            using (var rb = new SolidBrush(Color.FromArgb(A(220, c), Shade(baseCol, 0.72f))))
                g.FillPolygon(rb, right);
            using (var tb = new SolidBrush(Color.FromArgb(A(235, c), Shade(baseCol, 1.15f))))
                g.FillPolygon(tb, top);
        }
    }

    private static void DrawShapeOutline(Graphics g, Pen pen, int shapeKind, float halfS)
    {
        switch (shapeKind)
        {
            case 0:
                g.DrawPolygon(pen, new PointF[]
                {
                    new(0, -halfS),
                    new(halfS * 0.866f, halfS * 0.5f),
                    new(-halfS * 0.866f, halfS * 0.5f)
                });
                break;
            case 1:
                var hex = new PointF[6];
                for (int k = 0; k < 6; k++)
                {
                    float a = k * MathF.PI / 3f;
                    hex[k] = new PointF(halfS * MathF.Cos(a), halfS * MathF.Sin(a));
                }
                g.DrawPolygon(pen, hex);
                break;
            case 2:
                g.DrawEllipse(pen, -halfS, -halfS, halfS * 2, halfS * 2);
                break;
            case 3:
                g.DrawRectangle(pen, -halfS, -halfS, halfS * 2, halfS * 2);
                break;
            default:
                g.DrawPolygon(pen, new PointF[]
                {
                    new(0, -halfS), new(halfS, 0),
                    new(0, halfS),  new(-halfS, 0)
                });
                break;
        }
    }

    private static void DrawRadial(Graphics g, float hue, float sat, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var traj = SampleTrajectory(f, 12, rng);
        var segFlux = SampleSegments(f, s => Math.Clamp(s.Flux * 2.5f, 0f, 1f), 12, rng, 0.40f, 0.40f);
        var segOnset = SampleSegments(f, s => Math.Clamp(s.OnsetRate / 8f, 0f, 1f), 12, rng, 0.30f, 0.40f);
        int variant = Roulette(rng, 1f, 0.8f, 0.7f);

        using (var bg = new SolidBrush(Hsl(hue, c.BgS, c.BgL)))
            g.FillRectangle(bg, 0, 0, Size, Size);

        if (variant == 1)
        {
            RadialSunburst(g, c, pal, f, traj, segOnset, rng);
            return;
        }

        float cx = variant == 2 ? Size * 0.22f : Size / 2f;
        float cy = variant == 2 ? Size * 0.26f : Size / 2f;
        float maxR = variant == 2 ? Size * 1.05f : Size * 0.71f;
        int rings = Math.Clamp(7 + (int)(f.Peaks * 0.35f), 7, 14);
        var disp = f.DisplaySpectrum;
        float maxBin = Math.Max(1f, disp.Length - 1f);
        const int pts = 90;

        var radii = new float[rings + 1];
        float wSum = 0f;
        for (int k = 0; k < rings; k++) wSum += 0.3f + At(traj, (float)k / (rings - 1));
        float acc = 0f;
        for (int k = 0; k < rings; k++)
        {
            acc += 0.3f + At(traj, (float)k / (rings - 1));
            radii[k + 1] = acc / wSum * maxR;
        }

        var lobes = new int[rings + 1];
        var phases = new float[rings + 1];
        for (int k = 0; k <= rings; k++)
        {
            lobes[k] = 3 + rng.Next(5);
            phases[k] = rng.NextSingle() * MathF.PI * 2f;
        }

        float RingRadius(int k, float a)
        {
            if (k == 0) return 0f;
            float wob = 1.5f + At(segFlux, (float)(k - 1) / Math.Max(1, rings - 1)) * 6f;
            return radii[k] + MathF.Sin(a * lobes[k] + phases[k]) * wob;
        }

        for (int ring = rings - 1; ring >= 0; ring--)
        {
            float t = (float)ring / (rings - 1);
            float energy;
            if (disp.Length > 0)
            {
                int bi = Math.Clamp((int)MathF.Pow(maxBin, t), 0, disp.Length - 1);
                energy = Math.Clamp(disp[bi], 0.04f, 1f);
            }
            else
                energy = 0.3f + rng.NextSingle() * 0.65f;

            var outer = new PointF[pts + 1];
            var inner = new PointF[pts + 1];
            for (int i = 0; i <= pts; i++)
            {
                float a = i % pts * MathF.PI * 2f / pts;
                float ro = RingRadius(ring + 1, a);
                float ri = RingRadius(ring, a);
                outer[i] = new PointF(cx + MathF.Cos(a) * ro, cy + MathF.Sin(a) * ro);
                inner[i] = new PointF(cx + MathF.Cos(a) * ri, cy + MathF.Sin(a) * ri);
            }

            var annulus = new PointF[(pts + 1) * 2];
            Array.Copy(outer, annulus, pts + 1);
            for (int i = 0; i <= pts; i++) annulus[pts + 1 + i] = inner[pts - i];

            var rc = Shade(PalAt(pal, (t * 0.5f + (ring & 1) * 0.40f + ring % 3 * 0.08f) % 1f),
                0.55f + energy * 0.65f);
            using var br = new SolidBrush(Color.FromArgb(A(150 + (int)(energy * 100), c), rc));
            g.FillPolygon(br, annulus);
            using var edgePen = new Pen(Color.FromArgb(A(60 + (int)(energy * 90), c), Shade(rc, 1.45f)), 1.2f);
            g.DrawPolygon(edgePen, outer);
        }

        float dotR = 6f + f.Bass * 14f;
        using var dotBrush = new SolidBrush(Color.FromArgb(A(235, c), pal.Stops[4]));
        g.FillEllipse(dotBrush, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
    }

    private static void RadialSunburst(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] traj, float[] segOnset, Random rng)
    {
        float cx = Size / 2f + (rng.NextSingle() - 0.5f) * 50f;
        float cy = Size / 2f + (rng.NextSingle() - 0.5f) * 50f;
        float maxR = Size * 0.62f;

        for (int i = 0; i < 3; i++)
        {
            using var pen = new Pen(Color.FromArgb(A(50, c), PalAt(pal, 0.3f + i * 0.2f)), 1.1f);
            WobblyEllipse(g, pen, cx, cy, maxR * (0.35f + i * 0.28f), rng, 2f);
        }

        float rot = rng.NextSingle() * MathF.PI * 2f;
        for (int k = 0; k < 12; k++)
        {
            float groupA = rot + k * MathF.PI * 2f / 12f;
            int rays = 1 + (int)(segOnset[k] * 6f);
            float len = 36f + traj[k] * (maxR - 36f);
            var col = PalAt(pal, (k / 11f * 0.6f + (k & 1) * 0.35f) % 1f);

            for (int r = 0; r < rays; r++)
            {
                float a = groupA + (rng.NextSingle() - 0.5f) * 0.30f;
                float l = len * (0.75f + rng.NextSingle() * 0.35f);
                float w = 2f + rng.NextSingle() * 3f;
                using var pen = new Pen(Color.FromArgb(A(190, c), col), w)
                    { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(pen, cx + MathF.Cos(a) * 20f, cy + MathF.Sin(a) * 20f,
                    cx + MathF.Cos(a) * (20f + l), cy + MathF.Sin(a) * (20f + l));
                if (rng.NextSingle() < 0.35f)
                {
                    using var tip = new SolidBrush(Color.FromArgb(A(220, c), Shade(col, 1.4f)));
                    float tx = cx + MathF.Cos(a) * (20f + l);
                    float ty = cy + MathF.Sin(a) * (20f + l);
                    g.FillEllipse(tip, tx - 2.5f, ty - 2.5f, 5f, 5f);
                }
            }
        }

        float dotR = 7f + f.Bass * 12f;
        using var dot = new SolidBrush(Color.FromArgb(A(235, c), pal.Stops[4]));
        g.FillEllipse(dot, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
        using var ring = new Pen(Color.FromArgb(A(140, c), pal.Ink), 2f);
        g.DrawEllipse(ring, cx - dotR - 3f, cy - dotR - 3f, (dotR + 3f) * 2, (dotR + 3f) * 2);
    }

    private static void DrawPixelStyle(Bitmap bmp, CoverArtStyle style, float hue, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var lut = PaletteLut(pal);
        var traj = SampleTrajectory(f, 12, rng);
        var pixels = new int[Size * Size];

        if (style == CoverArtStyle.Plasma)
            RenderPlasma(pixels, f, traj, lut, rng);
        else
            RenderLiquid(pixels, f, traj, lut, rng);

        var data = bmp.LockBits(new Rectangle(0, 0, Size, Size),
                                 ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bmp.UnlockBits(data);
    }

    private static void RenderPlasma(int[] pixels, TrackFeatures f, float[] traj, int[] lut, Random rng)
    {
        int variant = Roulette(rng, 1f, 0.9f, 0.8f);
        float xF = (26f - 17f * f.Centroid) * (0.8f + rng.NextSingle() * 0.4f);
        float yF = (22f - 13f * f.Centroid) * (0.8f + rng.NextSingle() * 0.4f);
        float dF = (18f - 10f * f.Centroid) * (0.8f + rng.NextSingle() * 0.4f);
        float rF = (15f - 8f * f.Centroid) * (0.8f + rng.NextSingle() * 0.4f);
        float ph = rng.NextSingle() * MathF.PI * 2f;
        float ph2 = rng.NextSingle() * MathF.PI * 2f;
        float cx = Size * (0.3f + rng.NextSingle() * 0.4f);
        float cy = Size * (0.3f + rng.NextSingle() * 0.4f);
        int levels = Math.Clamp(3 + f.Peaks / 8, 3, 8);
        int[,] bayer =
        {
            { 0, 8, 2, 10 },
            { 12, 4, 14, 6 },
            { 3, 11, 1, 9 },
            { 15, 7, 13, 5 },
        };

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float v;
            if (variant == 1)
            {
                float warp = (14f + f.EnergyCv * 40f) * (0.5f + At(traj, y / (Size - 1f)));
                float wx = x + warp * MathF.Sin(y / yF + ph);
                float wy = y + warp * MathF.Sin(x / xF + ph2);
                float dx = wx - cx, dy = wy - cy;
                v = 0.5f + 0.5f * MathF.Sin(MathF.Sqrt(dx * dx + dy * dy) / rF + ph);
            }
            else
            {
                float dx = x - cx, dy = y - cy;
                float v1 = MathF.Sin(x / xF + ph);
                float v2 = MathF.Sin(y / yF);
                float v3 = MathF.Sin((x + y) / dF + ph * 0.7f);
                float v4 = MathF.Sin(MathF.Sqrt(dx * dx + dy * dy) / rF);
                float v5 = At(traj, x / (Size - 1f)) * 2f - 1f;
                v = (v1 + v2 + v3 + v4 + 2f * v5 + 6f) / 12f;
            }

            if (variant == 2)
            {
                v += (bayer[x & 3, y & 3] / 16f - 0.5f) / levels;
                v = MathF.Floor(Math.Clamp(v, 0f, 0.999f) * levels) / (levels - 1f);
            }

            pixels[y * Size + x] = lut[Math.Clamp((int)(v * 255f), 0, 255)];
        }
    }

    private static void RenderLiquid(int[] pixels, TrackFeatures f, float[] traj, int[] lut, Random rng)
    {
        int variant = Roulette(rng, 1f, 0.9f, 0.8f);
        var segCentroid = SampleSegments(f, s => s.Centroid, 12, rng, 0.55f, 0.30f);
        var segFlux = SampleSegments(f, s => Math.Clamp(s.Flux * 2.5f, 0f, 1f), 12, rng, 0.40f, 0.40f);
        const int n = 12;

        var cum = new float[n + 1];
        float wSum = 0f;
        for (int k = 0; k < n; k++) wSum += 0.3f + traj[k];
        for (int k = 0; k < n; k++) cum[k + 1] = cum[k] + (0.3f + traj[k]) / wSum;

        if (variant == 2)
        {
            float cx = Size * (0.25f + rng.NextSingle() * 0.5f);
            float cy = Size * (0.25f + rng.NextSingle() * 0.5f);
            float maxR = Size * 0.85f;
            int lobes = 3 + rng.Next(4);
            float ph = rng.NextSingle() * MathF.PI * 2f;
            float ph2 = rng.NextSingle() * MathF.PI * 2f;

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = x - cx, dy = y - cy;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float a = MathF.Atan2(dy, dx);
                float dd = dist + MathF.Sin(a * lobes + ph) * 14f + MathF.Sin(a * 2f + ph2) * 8f;
                float t = Math.Clamp(dd / maxR, 0f, 0.999f);
                int ring = 0;
                while (ring < n - 1 && cum[ring + 1] < t) ring++;
                float baseT = (segCentroid[ring] * 0.5f + (ring & 1) * 0.45f + ring % 3 * 0.10f) % 1f;
                int idx = Math.Clamp((int)(baseT * 255f) + (((x * 7 + y * 13) & 7) - 3) * 4, 0, 255);
                pixels[y * Size + x] = lut[idx];
            }
            return;
        }

        bool vertical = variant == 0;
        var fr1 = new float[n];
        var fr2 = new float[n];
        var ph1 = new float[n];
        var phB = new float[n];
        var amp = new float[n];
        for (int k = 0; k < n; k++)
        {
            fr1[k] = 0.010f + rng.NextSingle() * 0.016f;
            fr2[k] = 0.030f + rng.NextSingle() * 0.030f;
            ph1[k] = rng.NextSingle() * MathF.PI * 2f;
            phB[k] = rng.NextSingle() * MathF.PI * 2f;
            amp[k] = 4f + segFlux[k] * 26f;
        }

        var bounds = new float[n - 1];
        for (int major = 0; major < Size; major++)
        {
            for (int k = 0; k < n - 1; k++)
                bounds[k] = cum[k + 1] * Size
                    + amp[k] * MathF.Sin(major * fr1[k] + ph1[k])
                    + amp[k] * 0.4f * MathF.Sin(major * fr2[k] + phB[k]);

            for (int minor = 0; minor < Size; minor++)
            {
                int band = 0;
                while (band < n - 1 && bounds[band] < minor) band++;
                float drift = (major / (Size - 1f) - 0.5f) * 0.16f;
                int x = vertical ? minor : major;
                int y = vertical ? major : minor;
                float baseT = (segCentroid[band] * 0.5f + (band & 1) * 0.45f + band % 3 * 0.10f) % 1f;
                int idx = Math.Clamp((int)((baseT + drift) * 255f)
                    + (((x * 7 + y * 13) & 7) - 3) * 3, 0, 255);
                pixels[y * Size + x] = lut[idx];
            }
        }
    }

    private static void DrawStripes(Graphics g, float hue, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var traj = SampleTrajectory(f, 12, rng);
        var segCentroid = SampleSegments(f, s => s.Centroid, 12, rng, 0.55f, 0.30f);
        var segHp = SampleSegments(f, s => s.HpRatio, 12, rng, 0.65f, 0.30f);
        int variant = Roulette(rng, 1f, 0.9f, 0.8f);

        using (var bg = new SolidBrush(Hsl(hue, c.BgS, c.BgL)))
            g.FillRectangle(bg, 0, 0, Size, Size);

        switch (variant)
        {
            case 0:  StripesBars(g, c, pal, traj, segCentroid, segHp, rng);    break;
            case 1:  StripesSunset(g, c, pal, f, traj, rng);                   break;
            default: StripesChevron(g, c, pal, traj, segCentroid, rng);        break;
        }
    }

    private static void StripesBars(Graphics g, Contrast c, Palette pal,
        float[] traj, float[] segCentroid, float[] segHp, Random rng)
    {
        bool paperBg = rng.NextSingle() < 0.45f;
        if (paperBg)
            using (var bg = new SolidBrush(pal.Paper))
                g.FillRectangle(bg, 0, 0, Size, Size);

        float angle = (rng.NextSingle() - 0.5f) * 50f;
        if (rng.NextSingle() < 0.40f) angle += 90f;
        var state = g.Save();
        g.TranslateTransform(Size / 2f, Size / 2f);
        g.RotateTransform(angle);
        g.TranslateTransform(-Size / 2f, -Size / 2f);

        int n = rng.NextSingle() < 0.35f ? 24 : 12;
        float span = Size * 1.9f;
        float x = Size / 2f - span / 2f;
        var w = new float[n];
        float wSum = 0f;
        for (int k = 0; k < n; k++)
        {
            w[k] = 0.35f + At(traj, k / (n - 1f));
            wSum += w[k];
        }

        float gap = paperBg ? 3f : 2f;
        for (int k = 0; k < n; k++)
        {
            float sw = w[k] / wSum * span;
            float t  = k / (n - 1f);
            var col  = PalAt(pal, (At(segCentroid, t) * 0.5f + (k & 1) * 0.45f + k % 3 * 0.10f) % 1f);
            var rect = new RectangleF(x + gap / 2f, -Size, sw - gap, Size * 3f);

            if (At(segHp, t) > 0.60f)
            {
                using var lg = new LinearGradientBrush(
                    new PointF(rect.X, -Size), new PointF(rect.X, Size * 2f),
                    Shade(col, 0.55f), col);
                g.FillRectangle(lg, rect);
            }
            else
            {
                using var br = new SolidBrush(Color.FromArgb(A(235, c), col));
                g.FillRectangle(br, rect);
                HalftoneRect(g, new RectangleF(rect.X, 0, rect.Width, Size), Color.FromArgb(70, pal.Ink),
                    7f, (u, v) => 0.30f + 0.45f * v);
            }

            if (k % 3 == 2)
            {
                using var pen = new Pen(Color.FromArgb(A(90, c),
                    PalAt(pal, (At(segCentroid, t) + 0.45f) % 1f)), 2f);
                g.DrawLine(pen, x + sw + 1.5f, -Size, x + sw + 1.5f, Size * 2f);
            }
            x += sw;
        }
        g.Restore(state);
    }

    private static void StripesSunset(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] traj, Random rng)
    {
        int n = 12;
        float horizonY = Size * (0.40f + 0.28f * ArgMax(traj) / (traj.Length - 1f));
        var w = new float[n];
        float wSum = 0f;
        for (int k = 0; k < n; k++)
        {
            w[k] = 0.35f + traj[k];
            wSum += w[k];
        }

        float y = 0f;
        for (int k = 0; k < n; k++)
        {
            float bh = w[k] / wSum * horizonY;
            using var br = new SolidBrush(PalAt(pal, 1f - k / (n - 1f)));
            g.FillRectangle(br, 0, y, Size, bh + 1f);
            y += bh;
        }

        float sunR = Size * (0.18f + f.Bass * 0.12f);
        float sunX = Size * (0.30f + rng.NextSingle() * 0.40f);
        var prevClip = g.Clip;
        g.SetClip(new RectangleF(0, 0, Size, horizonY));
        using (var sun = new SolidBrush(pal.Stops[4]))
            g.FillEllipse(sun, sunX - sunR, horizonY - sunR * 1.35f, sunR * 2, sunR * 2);
        int slits = 5 + rng.Next(3);
        for (int i = 0; i < slits; i++)
        {
            float sy = horizonY - sunR * 1.35f + sunR * (1.0f + 0.95f * i / slits);
            float sh = 2f + i * 2.2f;
            var band = new RectangleF(sunX - sunR - 4, sy, sunR * 2 + 8, sh);
            var t = (sy + sh / 2f) / horizonY;
            using var gapBr = new SolidBrush(PalAt(pal, 1f - Math.Clamp(t, 0f, 1f)));
            g.FillRectangle(gapBr, band);
        }
        g.Clip = prevClip;

        using (var ground = new SolidBrush(Shade(pal.Stops[0], 0.30f)))
            g.FillRectangle(ground, 0, horizonY, Size, Size - horizonY);
        int refl = 6 + rng.Next(4);
        for (int i = 0; i < refl; i++)
        {
            float ry = horizonY + 8f + i * (Size - horizonY - 16f) / refl;
            float rw = sunR * (1.6f - 1.2f * i / refl) * (0.7f + rng.NextSingle() * 0.5f);
            using var rb = new SolidBrush(Color.FromArgb(A(60 - i * 5, c), pal.Stops[4]));
            g.FillRectangle(rb, sunX - rw / 2f, ry, rw, 2.5f);
        }
        using (var hp = new Pen(Color.FromArgb(A(160, c), pal.Stops[3]), 2f))
            g.DrawLine(hp, 0, horizonY, Size, horizonY);
    }

    private static void StripesChevron(Graphics g, Contrast c, Palette pal,
        float[] traj, float[] segCentroid, Random rng)
    {
        int folds = 1 + rng.Next(2);
        var order = Enumerable.Range(0, traj.Length).OrderByDescending(i => traj[i]).ToArray();
        var xs = new List<float> { 0f, Size };
        for (int i = 0; i < folds; i++)
        {
            float fx = Size * order[i] / (traj.Length - 1f);
            if (fx > 40f && fx < Size - 40f) xs.Add(fx);
        }
        xs.Sort();

        float baseAngle = 15f + rng.NextSingle() * 20f;
        int n = 12;
        var w = new float[n];
        float wSum = 0f;
        for (int k = 0; k < n; k++)
        {
            w[k] = 0.35f + traj[k];
            wSum += w[k];
        }

        for (int r = 0; r < xs.Count - 1; r++)
        {
            float x0 = xs[r], x1 = xs[r + 1];
            var region = new RectangleF(x0, 0, x1 - x0, Size);
            var state = g.Save();
            g.SetClip(region);
            g.TranslateTransform((x0 + x1) / 2f, Size / 2f);
            g.RotateTransform(r % 2 == 0 ? baseAngle : -baseAngle);
            g.TranslateTransform(-(x0 + x1) / 2f, -Size / 2f);

            float span = Size * 2.6f;
            float y = Size / 2f - span / 2f;
            for (int k = 0; k < n; k++)
            {
                float bh = w[k] / wSum * span;
                var col = PalAt(pal, At(segCentroid, k / (n - 1f)));
                using var br = new SolidBrush(Color.FromArgb(A(240, c), col));
                g.FillRectangle(br, x0 - Size, y, (x1 - x0) + Size * 2f, bh - 2f);
                y += bh;
            }
            g.Restore(state);
        }

        foreach (var fx in xs.Skip(1).Take(xs.Count - 2))
        {
            using var pen = new Pen(Color.FromArgb(A(120, c), pal.Ink), 2.5f);
            g.DrawLine(pen, fx, 0, fx, Size);
        }
    }

    private static void DrawTiles(Graphics g, float hue, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var traj = SampleTrajectory(f, 12, rng);
        var segHp = SampleSegments(f, s => s.HpRatio, 12, rng, 0.65f, 0.30f);
        int variant = Roulette(rng, 1f, 1f, 0.7f);

        var rects = new List<RectangleF> { new(0, 0, Size, Size) };
        int splits = 10 + (int)(Math.Clamp(f.OnsetDensity / 5f, 0f, 1f) * 8) + rng.Next(4);
        for (int i = 0; i < splits && rects.Count < 40; i++)
        {
            int ri = rng.Next(rects.Count);
            var r  = rects[ri];
            float t = 0.25f + traj[i % 12] * 0.5f;
            if (r.Width >= r.Height && r.Width * Math.Min(t, 1 - t) > 34)
            {
                rects.RemoveAt(ri);
                rects.Add(new(r.X,               r.Y, r.Width * t,       r.Height));
                rects.Add(new(r.X + r.Width * t, r.Y, r.Width * (1 - t), r.Height));
            }
            else if (r.Height * Math.Min(t, 1 - t) > 34)
            {
                rects.RemoveAt(ri);
                rects.Add(new(r.X, r.Y,                r.Width, r.Height * t));
                rects.Add(new(r.X, r.Y + r.Height * t, r.Width, r.Height * (1 - t)));
            }
        }

        switch (variant)
        {
            case 0:  TilesMondrian(g, c, pal, f, rects, rng);       break;
            case 1:  TilesBauhaus(g, c, pal, rects, segHp, rng);    break;
            default: TilesCollage(g, c, pal, rects, rng);           break;
        }

        int misIdx = rng.Next(rects.Count);
        var mis = rects[misIdx];
        var misCol = Hsl((hue + 30f) % 360f, c.AccentS, c.AccentL);
        using var misBr = new SolidBrush(Color.FromArgb(110, misCol));
        g.FillRectangle(misBr, mis.X + 3f, mis.Y + 3f, mis.Width - 3f, mis.Height - 3f);
    }

    private static void TilesMondrian(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, List<RectangleF> rects, Random rng)
    {
        using (var bg = new SolidBrush(pal.Paper))
            g.FillRectangle(bg, 0, 0, Size, Size);

        var byArea = rects.OrderByDescending(r => r.Width * r.Height).ToArray();
        int accents = Math.Clamp(2 + f.Peaks / 8, 2, 5);
        var accentSet = new HashSet<RectangleF>(byArea.Take(accents));

        foreach (var r in rects)
        {
            Color col;
            if (accentSet.Contains(r))
                col = pal.Stops[rng.Next(pal.Stops.Length)];
            else if (rng.NextSingle() < 0.18f)
                col = Shade(pal.Ink, 1.4f);
            else
                col = Shade(pal.Paper, 0.86f + rng.NextSingle() * 0.20f);
            using var br = new SolidBrush(col);
            g.FillRectangle(br, r);
        }

        float bw = 3f + f.Bass * 4f;
        using var pen = new Pen(pal.Ink, bw) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        foreach (var r in rects)
        {
            float over = 3f + rng.NextSingle() * 3f;
            g.DrawLines(pen, WobblyLine(rng, new PointF(r.X - over, r.Y), new PointF(r.Right + over, r.Y), 1f));
            g.DrawLines(pen, WobblyLine(rng, new PointF(r.X, r.Y - over), new PointF(r.X, r.Bottom + over), 1f));
        }
        g.DrawLines(pen, WobblyLine(rng, new PointF(0, Size - 1), new PointF(Size, Size - 1), 1f));
        g.DrawLines(pen, WobblyLine(rng, new PointF(Size - 1, 0), new PointF(Size - 1, Size), 1f));
    }

    private static void TilesBauhaus(Graphics g, Contrast c, Palette pal,
        List<RectangleF> rects, float[] segHp, Random rng)
    {
        using (var bg = new SolidBrush(Shade(pal.Paper, 0.92f)))
            g.FillRectangle(bg, 0, 0, Size, Size);

        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            bool darkTile = (i & 1) == 0 ? rng.NextSingle() < 0.35f : rng.NextSingle() < 0.15f;
            var tileBg = darkTile ? Shade(pal.Ink, 1.3f) : Shade(pal.Paper, 0.95f + rng.NextSingle() * 0.10f);
            using (var br = new SolidBrush(tileBg))
                g.FillRectangle(br, r);

            var motifCol = PalAt(pal, rng.NextSingle());
            int motif = ((int)(At(segHp, i / Math.Max(1f, rects.Count - 1f)) * 2.9f) + rng.Next(2)) % 5;
            float inset = Math.Min(r.Width, r.Height) * 0.14f;
            var m = new RectangleF(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2);
            float d = Math.Min(m.Width, m.Height);
            using var mb = new SolidBrush(motifCol);
            switch (motif)
            {
                case 0:
                    int corner = rng.Next(4);
                    float qx = corner is 0 or 3 ? m.X - d : m.X + m.Width - d;
                    float qy = corner < 2 ? m.Y - d : m.Y + m.Height - d;
                    var prev = g.Clip;
                    g.SetClip(m);
                    g.FillEllipse(mb, qx, qy, d * 2, d * 2);
                    g.Clip = prev;
                    break;
                case 1:
                    g.FillPie(mb, m.X + m.Width / 2f - d / 2f, m.Y + m.Height - d / 2f - 1, d, d, 180, 180);
                    break;
                case 2:
                    g.FillEllipse(mb, m.X + m.Width / 2f - d / 2f, m.Y + m.Height / 2f - d / 2f, d, d);
                    break;
                case 3:
                    g.FillPolygon(mb, new PointF[]
                    {
                        new(m.X, m.Y + m.Height),
                        new(m.X + m.Width, m.Y + m.Height),
                        new(m.X + m.Width, m.Y),
                    });
                    break;
                default:
                    float sh = m.Height / 5f;
                    for (int s = 0; s < 3; s++)
                        g.FillRectangle(mb, m.X, m.Y + s * sh * 2f, m.Width, sh);
                    break;
            }
        }

        using var pen = new Pen(Color.FromArgb(A(70, c), pal.Ink), 1.5f);
        foreach (var r in rects)
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    private static void TilesCollage(Graphics g, Contrast c, Palette pal,
        List<RectangleF> rects, Random rng)
    {
        using (var bg = new SolidBrush(Shade(pal.Ink, 1.6f)))
            g.FillRectangle(bg, 0, 0, Size, Size);

        float shear = 0.12f + rng.NextSingle() * 0.13f;
        if (rng.NextSingle() < 0.5f) shear = -shear;
        var state = g.Save();
        using (var mtx = new Matrix(1f, 0f, shear, 1f, -shear * Size / 2f, 0f))
            g.MultiplyTransform(mtx);

        foreach (var r in rects)
        {
            using (var sh = new SolidBrush(Color.FromArgb(160, Shade(pal.Ink, 0.6f))))
                g.FillRectangle(sh, r.X + 4f, r.Y + 4f, r.Width - 2f, r.Height - 2f);
            var col = rng.NextSingle() < 0.35f ? pal.Paper : PalAt(pal, rng.NextSingle());
            using var br = new SolidBrush(col);
            g.FillRectangle(br, r.X + 2f, r.Y + 2f, r.Width - 6f, r.Height - 6f);
        }
        g.Restore(state);
    }

    private static void DrawBokeh(Graphics g, float hue, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var traj = SampleTrajectory(f, 12, rng);
        var segCentroid = SampleSegments(f, s => s.Centroid, 12, rng, 0.55f, 0.30f);
        var segOnset = SampleSegments(f, s => Math.Clamp(s.OnsetRate / 8f, 0f, 1f), 12, rng, 0.30f, 0.40f);
        int variant = Roulette(rng, 1f, 0.8f, 0.8f);
        bool hexDiscs = f.PercussiveDensity > 0.5f;

        using (var bg = new SolidBrush(Hsl(hue, c.BgS, c.BgL)))
            g.FillRectangle(bg, 0, 0, Size, Size);

        switch (variant)
        {
            case 0:  BokehString(g, c, pal, traj, segCentroid, segOnset, hexDiscs, rng); break;
            case 1:  BokehBubbles(g, c, pal, traj, segCentroid, hexDiscs, rng);          break;
            default: BokehRiso(g, c, pal, rng);                                          break;
        }
    }

    private static void BokehDisc(Graphics g, Color col, int alpha, float cx, float cy, float r,
        bool hex, Random rng)
    {
        using var fringe = new SolidBrush(Color.FromArgb((int)(alpha * 0.4f),
            Color.FromArgb(col.B, col.R, col.G)));
        using var br = new SolidBrush(Color.FromArgb(alpha, col));
        if (hex)
        {
            var pts = new PointF[6];
            float rot = rng.NextSingle() * MathF.PI / 3f;
            for (int k = 0; k < 6; k++)
            {
                float a = rot + k * MathF.PI / 3f;
                pts[k] = new PointF(cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r);
            }
            var fr = pts.Select(p => new PointF(p.X + 2f, p.Y + 1.5f)).ToArray();
            g.FillPolygon(fringe, fr);
            g.FillPolygon(br, pts);
        }
        else
        {
            g.FillEllipse(fringe, cx - r + 2f, cy - r + 1.5f, r * 2, r * 2);
            g.FillEllipse(br, cx - r, cy - r, r * 2, r * 2);
        }
    }

    private static void BokehString(Graphics g, Contrast c, Palette pal,
        float[] traj, float[] segCentroid, float[] segOnset, bool hex, Random rng)
    {
        for (int i = 0; i < 22; i++)
        {
            float r = 6f + rng.NextSingle() * 20f;
            BokehDisc(g, PalAt(pal, rng.NextSingle()), 12 + rng.Next(16),
                rng.NextSingle() * Size, rng.NextSingle() * Size, r, hex, rng);
        }

        var p0 = new PointF(-30f, Size * (0.15f + rng.NextSingle() * 0.7f));
        var p3 = new PointF(Size + 30f, Size * (0.15f + rng.NextSingle() * 0.7f));
        var p1 = new PointF(Size * 0.33f, Size * rng.NextSingle());
        var p2 = new PointF(Size * 0.66f, Size * rng.NextSingle());

        for (int k = 0; k < 12; k++)
        {
            float t = k / 11f;
            float u = 1f - t;
            float bx = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
            float by = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;

            int discs = 1 + (int)(segOnset[k] * 4f);
            for (int d = 0; d < discs; d++)
            {
                float r = (8f + traj[k] * 48f) * (0.5f + rng.NextSingle() * 0.8f);
                float dx = bx + (rng.NextSingle() - 0.5f) * 95f;
                float dy = by + (rng.NextSingle() - 0.5f) * 95f;
                int alpha = A(20 + (int)(traj[k] * 70f), c);
                BokehDisc(g, PalAt(pal, segCentroid[k]), alpha, dx, dy, r, hex, rng);
            }
        }

        for (int i = 0; i < 3; i++)
        {
            float r = 90f + rng.NextSingle() * 55f;
            BokehDisc(g, PalAt(pal, rng.NextSingle()), A(14, c),
                rng.NextSingle() * Size, rng.NextSingle() * Size, r, hex, rng);
        }
    }

    private static void BokehBubbles(Graphics g, Contrast c, Palette pal,
        float[] traj, float[] segCentroid, bool hex, Random rng)
    {
        for (int k = 0; k < 12; k++)
        {
            float colX = Size * (k + 0.5f) / 12f;
            int discs = 1 + (int)(traj[k] * 3f);
            for (int d = 0; d < discs; d++)
            {
                float cy = Size * (0.85f - traj[k] * 0.55f) + (rng.NextSingle() - 0.5f) * 95f;
                float cx = colX + (rng.NextSingle() - 0.5f) * 40f;
                float r = 6f + traj[k] * 26f + rng.NextSingle() * 14f;
                var col = PalAt(pal, segCentroid[k]);
                int alpha = A(50 + (int)(traj[k] * 90f), c);

                using var rim = new Pen(Color.FromArgb(alpha, col), 2.5f);
                if (hex)
                {
                    var pts = new PointF[6];
                    float rot = rng.NextSingle() * MathF.PI / 3f;
                    for (int v = 0; v < 6; v++)
                    {
                        float a = rot + v * MathF.PI / 3f;
                        pts[v] = new PointF(cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r);
                    }
                    g.DrawPolygon(rim, pts);
                }
                else
                    g.DrawEllipse(rim, cx - r, cy - r, r * 2, r * 2);
                using var fill = new SolidBrush(Color.FromArgb((int)(alpha * 0.30f), col));
                g.FillEllipse(fill, cx - r, cy - r, r * 2, r * 2);
                using var spot = new SolidBrush(Color.FromArgb(alpha, Color.White));
                g.FillEllipse(spot, cx - r * 0.35f, cy - r * 0.5f, r * 0.3f, r * 0.3f);
            }
        }
    }

    private static void BokehRiso(Graphics g, Contrast c, Palette pal, Random rng)
    {
        int discs = 6 + rng.Next(4);
        float ringR = Size * 0.22f;
        float rot = rng.NextSingle() * MathF.PI * 2f;
        for (int i = 0; i < discs; i++)
        {
            float a = rot + i * MathF.PI * 2f / discs;
            float cx = Size / 2f + MathF.Cos(a) * ringR * (0.6f + rng.NextSingle() * 0.8f);
            float cy = Size / 2f + MathF.Sin(a) * ringR * (0.6f + rng.NextSingle() * 0.8f);
            float r = Size * (0.22f + rng.NextSingle() * 0.23f);
            var col = pal.Stops[i % pal.Stops.Length];
            using var br = new SolidBrush(Color.FromArgb(A(70 + rng.Next(40), c), col));
            g.FillEllipse(br, cx - r, cy - r, r * 2, r * 2);
        }

        using var dust = new SolidBrush(Color.FromArgb(120, pal.Paper));
        int specks = 30 + rng.Next(30);
        for (int i = 0; i < specks; i++)
        {
            float sx = rng.NextSingle() * Size, sy = rng.NextSingle() * Size;
            float sr = 0.8f + rng.NextSingle() * 1.8f;
            g.FillEllipse(dust, sx, sy, sr, sr);
        }
    }

    private static void DrawPolygons(Graphics g, float hue, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var traj = SampleTrajectory(f, 12, rng);
        var segCentroid = SampleSegments(f, s => s.Centroid, 12, rng, 0.55f, 0.30f);
        var segFlux = SampleSegments(f, s => Math.Clamp(s.Flux * 2.5f, 0f, 1f), 12, rng, 0.40f, 0.40f);
        int variant = Roulette(rng, 1f, 0.9f, 0.7f);

        using (var bg = new SolidBrush(Hsl(hue, c.BgS, c.BgL)))
            g.FillRectangle(bg, 0, 0, Size, Size);

        switch (variant)
        {
            case 0:  PolyShards(g, c, pal, f, traj, segCentroid, rng);  break;
            case 1:  PolyTerrain(g, c, pal, f, traj, segFlux, rng);     break;
            default: PolyNested(g, c, pal, f, traj, rng);               break;
        }
    }

    private static void PolyShards(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] traj, float[] segCentroid, Random rng)
    {
        float ox = Size * 0.38f + (rng.NextSingle() - 0.5f) * 80f;
        float oy = Size * 0.62f + (rng.NextSingle() - 0.5f) * 80f;
        int shards = 14 + f.Peaks;
        float rot = rng.NextSingle() * MathF.PI * 2f;

        for (int k = 0; k < shards; k++)
        {
            if (rng.NextSingle() < 0.25f) continue;
            float t = (float)k / shards;
            float a0 = rot + t * MathF.PI * 2f;
            float a1 = rot + (t + 0.9f / shards) * MathF.PI * 2f;
            float outer = 40f + At(traj, t) * Size * 0.42f + rng.NextSingle() * 20f;
            float inner = 8f + rng.NextSingle() * 14f;

            var pts = new[]
            {
                Jit(rng, ox + MathF.Cos(a0) * inner, oy + MathF.Sin(a0) * inner, 2f),
                Jit(rng, ox + MathF.Cos(a0) * outer, oy + MathF.Sin(a0) * outer, 3f),
                Jit(rng, ox + MathF.Cos(a1) * outer * (0.75f + rng.NextSingle() * 0.35f),
                         oy + MathF.Sin(a1) * outer * (0.75f + rng.NextSingle() * 0.35f), 3f),
                Jit(rng, ox + MathF.Cos(a1) * inner, oy + MathF.Sin(a1) * inner, 2f),
            };

            var col = PalAt(pal, At(segCentroid, t));
            using var fill = new SolidBrush(Color.FromArgb(A(120 + (int)(At(traj, t) * 110f), c), col));
            g.FillPolygon(fill, pts);
            if (k % 3 == 0)
            {
                using var pen = new Pen(Color.FromArgb(A(150, c), pal.Ink), 1.5f);
                g.DrawPolygon(pen, pts);
            }
        }

        using var core = new SolidBrush(Color.FromArgb(A(220, c), pal.Stops[4]));
        float cr = 5f + f.Bass * 9f;
        g.FillEllipse(core, ox - cr, oy - cr, cr * 2, cr * 2);
    }

    private static void PolyTerrain(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] traj, float[] segFlux, Random rng)
    {
        const int gw = 9, gh = 7;
        float ridgeY = Size * 0.52f;

        using (var sky = new LinearGradientBrush(new Rectangle(0, 0, Size, (int)(ridgeY + 40)),
                   Shade(PalAt(pal, 0.9f), 0.35f), Shade(PalAt(pal, 0.6f), 0.75f),
                   LinearGradientMode.Vertical))
            g.FillRectangle(sky, 0, 0, Size, ridgeY + 40);
        float sunX = Size * (0.25f + rng.NextSingle() * 0.5f);
        float sunR = 18f + f.Bass * 30f;
        using (var sun = new SolidBrush(Color.FromArgb(A(200, c), pal.Stops[4])))
            g.FillEllipse(sun, sunX - sunR, ridgeY - 90f - sunR, sunR * 2, sunR * 2);

        var grid = new PointF[gh, gw];
        for (int r = 0; r < gh; r++)
        for (int col = 0; col < gw; col++)
        {
            float u = col / (gw - 1f);
            float v = r / (gh - 1f);
            float x = -40f + u * (Size + 80f);
            float baseY = ridgeY + v * (Size - ridgeY + 40f);
            float lift = r == 0 ? At(traj, u) * Size * 0.25f : At(traj, u) * Size * 0.25f * (1f - v) * 0.5f;
            float jitAmp = 4f + At(segFlux, u) * 22f;
            grid[r, col] = Jit(rng, x, baseY - lift, r == 0 ? jitAmp : jitAmp * 0.6f);
        }

        for (int r = 0; r < gh - 1; r++)
        for (int col = 0; col < gw - 1; col++)
        {
            var pA = grid[r, col];
            var pB = grid[r, col + 1];
            var pC = grid[r + 1, col];
            var pD = grid[r + 1, col + 1];
            float shade0 = 0.30f + 0.55f * (1f - (float)r / gh) + (rng.NextSingle() - 0.5f) * 0.12f;
            var baseCol = PalAt(pal, 0.25f + 0.5f * (col / (gw - 1f)));
            bool flip = (r + col & 1) == 0;
            var t1 = flip ? new[] { pA, pB, pC } : new[] { pA, pB, pD };
            var t2 = flip ? new[] { pB, pD, pC } : new[] { pA, pD, pC };
            using (var b1 = new SolidBrush(Shade(baseCol, Math.Clamp(shade0, 0.1f, 1f))))
                g.FillPolygon(b1, t1);
            using (var b2 = new SolidBrush(Shade(baseCol, Math.Clamp(shade0 * 0.82f, 0.1f, 1f))))
                g.FillPolygon(b2, t2);
        }

        using var ridge = new Pen(Color.FromArgb(A(140, c), pal.Stops[4]), 1.5f);
        var ridgePts = new PointF[gw];
        for (int col = 0; col < gw; col++) ridgePts[col] = grid[0, col];
        g.DrawLines(ridge, ridgePts);
    }

    private static void PolyNested(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] traj, Random rng)
    {
        int count = 7 + rng.Next(4);
        int sides = Math.Clamp(3 + f.Peaks / 6, 3, 8);
        float rotStep = (10f + f.Autocorrelation * 25f) * MathF.PI / 180f;
        float rot = rng.NextSingle() * MathF.PI * 2f;
        float cx = Size / 2f + (rng.NextSingle() - 0.5f) * 60f;
        float cy = Size / 2f + (rng.NextSingle() - 0.5f) * 60f;
        float maxR = Size * 0.52f;

        for (int k = count - 1; k >= 0; k--)
        {
            float t = (k + 1f) / count;
            float r = maxR * t * (0.75f + 0.4f * traj[k % traj.Length]);
            float a0 = rot + k * rotStep;
            var pts = new PointF[sides];
            for (int v = 0; v < sides; v++)
            {
                float a = a0 + v * MathF.PI * 2f / sides;
                pts[v] = Jit(rng, cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r, 2f);
            }

            if ((k & 1) == 0)
            {
                using var fill = new SolidBrush(Color.FromArgb(A(90 + (int)(traj[k % traj.Length] * 90f), c),
                    PalAt(pal, t)));
                g.FillPolygon(fill, pts);
            }
            else
            {
                using var pen = new Pen(Color.FromArgb(A(170, c), pal.Ink), 2f);
                g.DrawPolygon(pen, pts);
            }
        }
    }

    private static void DrawRetro(Graphics g, float hue, float sat, TrackFeatures f, Random rng)
    {
        var c   = ContrastFor(f, rng);
        var pal = BuildPalette(hue, f, c, rng);
        var traj = SampleTrajectory(f, 12, rng);
        var segHigh = SampleSegments(f, s => s.High, 12, rng, 0.25f, 0.30f);
        int variant = Roulette(rng, 1f, 1f, 0.6f);

        var bg1 = Hsl(hue, Math.Min(c.BgS + 0.25f, 1f), c.BgL);
        var bg2 = Hsl((hue + 40f) % 360f, c.BgS, c.BgL + 0.06f);
        using (var bgb = new LinearGradientBrush(new Rectangle(0, 0, Size, Size), bg1, bg2,
                             LinearGradientMode.ForwardDiagonal))
            g.FillRectangle(bgb, 0, 0, Size, Size);

        switch (variant)
        {
            case 0:  RetroRibbons(g, c, pal, traj, segHigh, rng);  break;
            case 1:  RetroSun(g, c, pal, f, traj, rng);            break;
            default: RetroFlare(g, c, pal, f, traj, rng);          break;
        }
    }

    private static void RetroRibbons(Graphics g, Contrast c, Palette pal,
        float[] traj, float[] segHigh, Random rng)
    {
        int ribbons = 3 + rng.Next(3);
        float avgE = traj.Average();
        float w0 = 10f + avgE * 26f;

        for (int j = ribbons - 1; j >= 0; j--)
        {
            var pts = new PointF[13];
            for (int k = 0; k < 13; k++)
            {
                float x = k / 12f * Size + (rng.NextSingle() - 0.5f) * 12f;
                float y = Size * (0.82f - At(traj, k / 12f) * 0.58f) + j * 17f + (rng.NextSingle() - 0.5f) * 20f;
                pts[k] = new PointF(x, y);
            }
            var col = pal.Stops[j % pal.Stops.Length];
            using (var p1 = new Pen(Color.FromArgb(A(80, c), col), w0)
                { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawCurve(p1, pts, 0.6f);
            using (var p2 = new Pen(Color.FromArgb(A(140, c), col), w0 * 0.55f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawCurve(p2, pts, 0.6f);
            using (var p3 = new Pen(Color.FromArgb(A(210, c), Shade(col, 1.35f)), w0 * 0.22f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawCurve(p3, pts, 0.6f);
        }

        for (int k = 0; k < 12; k++)
        {
            int sparkles = (int)(segHigh[k] * 8f);
            for (int s = 0; s < sparkles; s++)
            {
                float sx = Size * k / 11f + (rng.NextSingle() - 0.5f) * 40f;
                float sy = rng.NextSingle() * Size;
                float sr = 1f + rng.NextSingle() * 4f;
                var col = Color.FromArgb(A(120 + rng.Next(120), c), Shade(PalAt(pal, rng.NextSingle()), 1.5f));
                if (rng.NextSingle() > 0.6f)
                    DrawSpikeStar(g, sx, sy, sr, rng.NextSingle() * MathF.PI * 0.5f, col);
                else
                {
                    using var sb = new SolidBrush(col);
                    g.FillEllipse(sb, sx - sr, sy - sr, sr * 2, sr * 2);
                }
            }
        }

        int peak = ArgMax(traj);
        float fx = Size * peak / 11f;
        float fy = Size * (0.82f - traj[peak] * 0.58f);
        RetroLensFlare(g, c, pal, fx, fy, 35f + rng.NextSingle() * 30f, rng);
    }

    private static void RetroLensFlare(Graphics g, Contrast c, Palette pal,
        float fx, float fy, float fr, Random rng)
    {
        int rays = 8 + rng.Next(8);
        float rotOff = rng.NextSingle() * MathF.PI * 2f;
        for (int r = 0; r < rays; r++)
        {
            float a = rotOff + r * MathF.PI * 2f / rays;
            float rayLen = fr * (1.5f + rng.NextSingle() * 2.5f);
            using var rpen = new Pen(Color.FromArgb(A(60 + rng.Next(70), c), Shade(pal.Stops[4], 1.3f)),
                0.5f + rng.NextSingle() * 2f);
            g.DrawLine(rpen, fx, fy, fx + MathF.Cos(a) * rayLen, fy + MathF.Sin(a) * rayLen);
        }
        using var flarePath = new GraphicsPath();
        flarePath.AddEllipse(fx - fr, fy - fr, fr * 2, fr * 2);
        using var pgb = new PathGradientBrush(flarePath);
        pgb.CenterColor    = Color.FromArgb(A(140, c), Shade(pal.Stops[4], 1.25f));
        pgb.SurroundColors = [Color.FromArgb(0, pal.Stops[2])];
        g.FillPath(pgb, flarePath);
        using var core = new SolidBrush(Color.FromArgb(220, Color.White));
        g.FillEllipse(core, fx - 3, fy - 3, 6, 6);
    }

    private static void RetroSun(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] traj, Random rng)
    {
        float horizonY = Size * (0.44f + rng.NextSingle() * 0.10f);
        float sunX = Size * (0.35f + rng.NextSingle() * 0.30f);
        float sunR = Size * (0.20f + f.Bass * 0.09f);

        int stars = 15 + (int)(f.High * 70f);
        using (var sb = new SolidBrush(Color.FromArgb(A(180, c), Shade(pal.Paper, 1.1f))))
            for (int i = 0; i < stars; i++)
            {
                float sx = rng.NextSingle() * Size;
                float sy = rng.NextSingle() * (horizonY - 20f);
                float sr = 0.6f + rng.NextSingle() * 1.6f;
                g.FillEllipse(sb, sx, sy, sr, sr);
            }

        var prevClip = g.Clip;
        g.SetClip(new RectangleF(0, 0, Size, horizonY));
        var sunRect = new RectangleF(sunX - sunR, horizonY - sunR * 1.6f, sunR * 2, sunR * 2);
        using (var sun = new LinearGradientBrush(
                   new PointF(sunX, sunRect.Y), new PointF(sunX, sunRect.Bottom),
                   Shade(pal.Stops[4], 1.2f), pal.Stops[1]))
            g.FillEllipse(sun, sunRect.X, sunRect.Y, sunRect.Width, sunRect.Height);
        using (var gapBr = new SolidBrush(Hsl(0, 0, 0)))
        {
            gapBr.Color = Color.FromArgb(255, Shade(pal.Ink, 1.1f));
            for (int k = 0; k < 12; k++)
            {
                float t = k / 11f;
                float sy = sunRect.Y + sunRect.Height * (0.45f + 0.55f * t);
                float gapH = (1f - traj[k]) * 7f + 1.5f;
                g.FillRectangle(gapBr, sunRect.X - 4f, sy, sunRect.Width + 8f, gapH);
            }
        }
        g.Clip = prevClip;

        using (var ground = new SolidBrush(Shade(pal.Ink, 1.15f)))
            g.FillRectangle(ground, 0, horizonY, Size, Size - horizonY);

        float spacing = 26f + (1f - f.Autocorrelation) * 34f;
        using var gridPen = new Pen(Color.FromArgb(A(150, c), pal.Stops[2]), 1.3f);
        int vLines = (int)(Size / spacing) + 2;
        for (int i = -vLines; i <= vLines; i++)
        {
            float xb = sunX + i * spacing * 2.2f;
            float xt = sunX + i * spacing * 0.4f;
            g.DrawLine(gridPen, xb, Size, xt, horizonY);
        }
        int hLines = 8;
        for (int i = 1; i <= hLines; i++)
        {
            float hy = horizonY + (Size - horizonY) * MathF.Pow(i / (float)hLines, 1.8f);
            g.DrawLines(gridPen, WobblyLine(rng, new PointF(0, hy), new PointF(Size, hy), 1f));
        }
        using var hp = new Pen(Color.FromArgb(A(200, c), pal.Stops[4]), 2f);
        g.DrawLine(hp, 0, horizonY, Size, horizonY);
    }

    private static void RetroFlare(Graphics g, Contrast c, Palette pal,
        TrackFeatures f, float[] traj, Random rng)
    {
        float cx = Size / 2f + (rng.NextSingle() - 0.5f) * 70f;
        float cy = Size / 2f + (rng.NextSingle() - 0.5f) * 70f;

        float r = 26f;
        for (int k = 0; k < 7; k++)
        {
            using var pen = new Pen(Color.FromArgb(A(50 + (int)(traj[k % traj.Length] * 90f), c),
                PalAt(pal, k / 6f)), 2f + traj[k % traj.Length] * 3f);
            WobblyEllipse(g, pen, cx, cy, r, rng, 1.5f);
            r += 20f * (0.8f + traj[k % traj.Length] * 0.5f);
        }

        int orbits = 3;
        for (int o = 0; o < orbits; o++)
        {
            float or2 = 50f + o * 55f + rng.NextSingle() * 20f;
            float tilt = rng.NextSingle() * MathF.PI;
            int dots = 5 + rng.Next(6);
            for (int d = 0; d < dots; d++)
            {
                float a = rng.NextSingle() * MathF.PI * 2f;
                float px = cx + MathF.Cos(a) * or2;
                float py = cy + MathF.Sin(a) * or2 * 0.45f;
                float rx = (px - cx) * MathF.Cos(tilt) - (py - cy) * MathF.Sin(tilt) + cx;
                float ry = (px - cx) * MathF.Sin(tilt) + (py - cy) * MathF.Cos(tilt) + cy;
                float sr = 1f + rng.NextSingle() * 3f;
                using var sb = new SolidBrush(Color.FromArgb(A(150, c), Shade(PalAt(pal, rng.NextSingle()), 1.4f)));
                g.FillEllipse(sb, rx - sr, ry - sr, sr * 2, sr * 2);
            }
        }

        RetroLensFlare(g, c, pal, cx, cy, 30f + f.Bass * 35f, rng);
    }

    private static void DrawShape(Graphics g, SolidBrush brush, int shapeKind, float halfS)
    {
        switch (shapeKind)
        {
            case 0:
                g.FillPolygon(brush, new PointF[]
                {
                    new(0, -halfS),
                    new(halfS * 0.866f, halfS * 0.5f),
                    new(-halfS * 0.866f, halfS * 0.5f)
                });
                break;
            case 1:
                var hex = new PointF[6];
                for (int k = 0; k < 6; k++)
                {
                    float a = k * MathF.PI / 3f;
                    hex[k] = new PointF(halfS * MathF.Cos(a), halfS * MathF.Sin(a));
                }
                g.FillPolygon(brush, hex);
                break;
            case 2:
                g.FillEllipse(brush, -halfS, -halfS, halfS * 2, halfS * 2);
                break;
            case 3:
                g.FillRectangle(brush, -halfS, -halfS, halfS * 2, halfS * 2);
                break;
            default:
                g.FillPolygon(brush, new PointF[]
                {
                    new(0, -halfS), new(halfS, 0),
                    new(0, halfS),  new(-halfS, 0)
                });
                break;
        }
    }

    private static void DrawSpikeStar(Graphics g, float cx, float cy, float r, float rot, Color c)
    {
        var pts = new PointF[8];
        for (int k = 0; k < 8; k++)
        {
            float a  = k * MathF.PI / 4f + rot;
            float rr = k % 2 == 0 ? r : r * 0.22f;
            pts[k] = new PointF(cx + MathF.Cos(a) * rr, cy + MathF.Sin(a) * rr);
        }
        using var br = new SolidBrush(c);
        g.FillPolygon(br, pts);
    }

    private static float SpectralHue(float[]? spectrum)
    {
        if (spectrum == null || spectrum.Length < 20) return 200f;
        int   n    = Math.Min(spectrum.Length, 512);
        float logN = MathF.Log2(n + 2f);

        float wSum = 0f, wTot = 0f;
        for (int i = 1; i < n; i++)
        {
            float logPos = MathF.Log2(i + 2f) / logN;
            float w      = spectrum[i] * (0.1f + logPos * 2.2f);
            wSum += logPos * w;
            wTot += w;
        }
        float c = wTot > 1e-7f ? wSum / wTot : 0.65f;
        return Math.Clamp((c - 0.40f) / 0.45f, 0f, 1f) * 360f;
    }

    private static int SpectralSeed(int baseSeed, float[]? spectrum)
    {
        int seed = baseSeed;
        if (spectrum == null || spectrum.Length == 0) return seed;
        float maxBin = spectrum.Length - 1f;
        for (int i = 0; i < 32; i++)
        {
            int bin = Math.Clamp((int)MathF.Pow(maxBin, i / 31f), 0, spectrum.Length - 1);
            seed = seed * 31 + (int)(Math.Clamp(spectrum[bin], 0f, 1f) * 1000);
        }
        return seed;
    }

    public static int DefaultSeedFor(Track t) =>
        StableHash.Combine(StableHash.Fnv1a(t.Title), StableHash.Fnv1a(t.Artist ?? ""), t.Id);

    private static Color Hsl(float h, float s, float l)
    {
        h = ((h % 360f) + 360f) % 360f;
        float c  = (1f - MathF.Abs(2f * l - 1f)) * s;
        float x  = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m  = l - c / 2f;
        float r, gv, b;
        if      (h < 60)  { r = c;  gv = x;  b = 0;  }
        else if (h < 120) { r = x;  gv = c;  b = 0;  }
        else if (h < 180) { r = 0;  gv = c;  b = x;  }
        else if (h < 240) { r = 0;  gv = x;  b = c;  }
        else if (h < 300) { r = x;  gv = 0;  b = c;  }
        else              { r = c;  gv = 0;  b = x;  }
        return Color.FromArgb(
            Math.Clamp((int)((r  + m) * 255), 0, 255),
            Math.Clamp((int)((gv + m) * 255), 0, 255),
            Math.Clamp((int)((b  + m) * 255), 0, 255));
    }

    private static float Frac(double v) => (float)(v - Math.Floor(v));

    private static void ApplyChaos(Bitmap bmp, float hue, float sat, int seed, float chaos)
    {
        var rng = new Random(unchecked(seed * 16777619 + 0x2545));

        float angle = (rng.NextSingle() - 0.5f) * 46f * chaos;
        if (MathF.Abs(angle) > 0.4f)
        {
            using var src = (Bitmap)bmp.Clone();
            using var g   = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float rad   = MathF.Abs(angle) * MathF.PI / 180f;
            float scale = MathF.Cos(rad) + MathF.Sin(rad);
            g.TranslateTransform(Size / 2f, Size / 2f);
            g.RotateTransform(angle);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-Size / 2f, -Size / 2f);
            g.DrawImage(src, 0, 0, Size, Size);
        }

        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int blobs = (int)(chaos * 7);
            for (int i = 0; i < blobs; i++)
            {
                float r   = Size * (0.10f + rng.NextSingle() * 0.28f);
                float bx  = rng.NextSingle() * Size;
                float by  = rng.NextSingle() * Size;
                float bh  = (hue + rng.Next(-40, 120) + 360f) % 360f;
                int   a   = 24 + rng.Next(0, (int)(70 * chaos) + 1);
                var   col = Hsl(bh, sat, 0.45f + rng.NextSingle() * 0.25f);
                using var br = new SolidBrush(Color.FromArgb(a, col));
                g.FillEllipse(br, bx - r, by - r, r * 2, r * 2);
            }
        }

        int grain = (int)(chaos * 38);
        if (grain > 0) AddGrain(bmp, rng, grain);
    }

    private static void AddGrain(Bitmap bmp, Random rng, int amount)
    {
        var rect = new Rectangle(0, 0, Size, Size);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int n   = Size * Size;
        var buf = new int[n];
        Marshal.Copy(data.Scan0, buf, 0, n);
        for (int i = 0; i < n; i++)
        {
            int d  = rng.Next(-amount, amount + 1);
            int c  = buf[i];
            int b  = Math.Clamp((c & 0xFF) + d, 0, 255);
            int gg = Math.Clamp(((c >> 8)  & 0xFF) + d, 0, 255);
            int r  = Math.Clamp(((c >> 16) & 0xFF) + d, 0, 255);
            buf[i] = (c & unchecked((int)0xFF000000)) | (r << 16) | (gg << 8) | b;
        }
        Marshal.Copy(buf, 0, data.Scan0, n);
        bmp.UnlockBits(data);
    }
}
