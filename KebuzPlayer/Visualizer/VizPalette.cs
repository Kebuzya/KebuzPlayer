using System;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using KebuzPlayer.Services;

namespace KebuzPlayer.Visualizer;

public sealed class VizPalette
{
    private const int BarSteps   = 64;
    private const int LevelSteps = 32;

    private static readonly Color[] RainbowColors = BuildRainbow();
    private static readonly Color[] ClassicColors = BuildClassic();
    private static readonly Color[] NeonColors    = BuildNeon();
    private static readonly Color[] FireColors    = BuildFire();
    private static readonly Color[] OceanColors   = BuildOcean();

    private readonly ImmutableSolidColorBrush[] _bars;
    private readonly ImmutableSolidColorBrush[] _levels;

    public ImmutableSolidColorBrush Peak    { get; }
    public ImmutablePen             LinePen { get; }
    public bool                     UsesLevelColor { get; }

    private VizPalette(ImmutableSolidColorBrush[] bars, ImmutableSolidColorBrush[] levels,
                       ImmutableSolidColorBrush peak, bool usesLevelColor)
    {
        _bars   = bars;
        _levels = levels;
        Peak    = peak;
        LinePen = new ImmutablePen(bars[BarSteps / 2], 1.5);
        UsesLevelColor = usesLevelColor;
    }

    public ImmutableSolidColorBrush BarBrush(int index, int count)
    {
        int i = count <= 1 ? 0 : index * (BarSteps - 1) / (count - 1);
        return _bars[i];
    }

    public ImmutableSolidColorBrush LevelBrush(float level)
    {
        int i = (int)(Math.Clamp(level, 0f, 1f) * (LevelSteps - 1));
        return _levels[i];
    }

    public static VizPalette Build(VizColorMode mode, Color accent, Color accent2)
    {
        var peak = new ImmutableSolidColorBrush(accent2);
        switch (mode)
        {
            case VizColorMode.Monochrome:
            {
                var bars   = new ImmutableSolidColorBrush[BarSteps];
                var levels = new ImmutableSolidColorBrush[LevelSteps];
                for (int i = 0; i < BarSteps; i++)
                    bars[i] = new ImmutableSolidColorBrush(ScaleLightness(accent, 0.55f + 0.75f * i / (BarSteps - 1f)));
                for (int i = 0; i < LevelSteps; i++)
                    levels[i] = new ImmutableSolidColorBrush(ScaleLightness(accent, 0.55f + 0.75f * i / (LevelSteps - 1f)));
                return new VizPalette(bars, levels, peak, false);
            }
            case VizColorMode.Duotone:
            {
                var bars   = new ImmutableSolidColorBrush[BarSteps];
                var levels = new ImmutableSolidColorBrush[LevelSteps];
                for (int i = 0; i < BarSteps; i++)
                    bars[i] = new ImmutableSolidColorBrush(Lerp(accent, accent2, i / (BarSteps - 1f)));
                for (int i = 0; i < LevelSteps; i++)
                    levels[i] = new ImmutableSolidColorBrush(Lerp(accent2, accent, i / (LevelSteps - 1f)));
                return new VizPalette(bars, levels, peak, false);
            }
            case VizColorMode.Rainbow:
                return new VizPalette(ToBrushes(RainbowColors), ToBrushes(Resample(RainbowColors, LevelSteps)), peak, false);
            case VizColorMode.Neon:
                return new VizPalette(ToBrushes(NeonColors), ToBrushes(Resample(NeonColors, LevelSteps)), peak, false);
            case VizColorMode.ClassicSpectrum:
                return new VizPalette(ToBrushes(Resample(ClassicColors, BarSteps)), ToBrushes(ClassicColors), peak, true);
            case VizColorMode.Fire:
                return new VizPalette(ToBrushes(Resample(FireColors, BarSteps)), ToBrushes(FireColors), peak, true);
            case VizColorMode.Ocean:
                return new VizPalette(ToBrushes(Resample(OceanColors, BarSteps)), ToBrushes(OceanColors), peak, true);
            default:
            {
                var bar    = new ImmutableSolidColorBrush(accent);
                var bars   = new ImmutableSolidColorBrush[BarSteps];
                var levels = new ImmutableSolidColorBrush[LevelSteps];
                Array.Fill(bars, bar);
                Array.Fill(levels, bar);
                return new VizPalette(bars, levels, peak, false);
            }
        }
    }

    private static ImmutableSolidColorBrush[] ToBrushes(Color[] colors)
    {
        var brushes = new ImmutableSolidColorBrush[colors.Length];
        for (int i = 0; i < colors.Length; i++)
            brushes[i] = new ImmutableSolidColorBrush(colors[i]);
        return brushes;
    }

    private static Color[] Resample(Color[] source, int count)
    {
        var result = new Color[count];
        for (int i = 0; i < count; i++)
            result[i] = source[i * (source.Length - 1) / Math.Max(count - 1, 1)];
        return result;
    }

    private static Color[] BuildRainbow()
    {
        var colors = new Color[BarSteps];
        for (int i = 0; i < BarSteps; i++)
            colors[i] = FromHsv(300f * i / (BarSteps - 1f), 0.80f, 0.95f);
        return colors;
    }

    private static Color[] BuildClassic()
    {
        var colors = new Color[LevelSteps];
        for (int i = 0; i < LevelSteps; i++)
            colors[i] = FromHsv(120f * (1f - i / (LevelSteps - 1f)), 0.90f, 0.92f);
        return colors;
    }

    private static Color[] BuildNeon()
    {
        var colors = new Color[BarSteps];
        for (int i = 0; i < BarSteps; i++)
            colors[i] = FromHsv(300f - 110f * i / (BarSteps - 1f), 0.85f, 1.0f);
        return colors;
    }

    private static Color[] BuildFire()
    {
        var colors = new Color[LevelSteps];
        for (int i = 0; i < LevelSteps; i++)
        {
            float t = i / (LevelSteps - 1f);
            colors[i] = FromHsv(8f + 40f * t, 1.0f - 0.15f * t, 0.5f + 0.5f * t);
        }
        return colors;
    }

    private static Color[] BuildOcean()
    {
        var colors = new Color[LevelSteps];
        for (int i = 0; i < LevelSteps; i++)
        {
            float t = i / (LevelSteps - 1f);
            colors[i] = FromHsv(225f - 45f * t, 0.75f, 0.35f + 0.65f * t);
        }
        return colors;
    }

    private static Color Lerp(Color a, Color b, float t) =>
        Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));

    private static Color ScaleLightness(Color c, float factor)
    {
        if (factor <= 1f)
            return Color.FromArgb(c.A, (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
        float t = Math.Min(factor - 1f, 1f);
        return Color.FromArgb(c.A,
            (byte)(c.R + (255 - c.R) * t),
            (byte)(c.G + (255 - c.G) * t),
            (byte)(c.B + (255 - c.B) * t));
    }

    private static Color FromHsv(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - Math.Abs(h / 60f % 2f - 1f));
        float m = v - c;
        (float r, float g, float b) = (int)(h / 60f) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
