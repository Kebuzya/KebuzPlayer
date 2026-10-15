namespace KebuzPlayer.Services;

[Flags]
public enum VizFormFactors
{
    None      = 0,
    Full      = 1,
    WideStrip = 2,
    Compact   = 4,
    All       = Full | WideStrip | Compact,
}

public sealed class VizStyleInfo
{
    public VisualizerStyle Style           { get; init; }
    public string          DisplayName     { get; init; } = "";
    public VizFormFactors  FormFactors     { get; init; } = VizFormFactors.All;
    public bool            NeedsRawSamples { get; init; }
    public bool            SupportsPeaks   { get; init; }
    public bool            SupportsBarCount { get; init; }
}

public static class VizStyleCatalog
{
    public static readonly VizStyleInfo[] Styles =
    [
        new() { Style = VisualizerStyle.Bars,         DisplayName = "Бары",            FormFactors = VizFormFactors.All,  SupportsPeaks = true, SupportsBarCount = true },
        new() { Style = VisualizerStyle.Mirror,       DisplayName = "Зеркало",         FormFactors = VizFormFactors.All,  SupportsPeaks = true, SupportsBarCount = true },
        new() { Style = VisualizerStyle.RadialBars,   DisplayName = "Радиальные бары", FormFactors = VizFormFactors.Full, SupportsPeaks = true, SupportsBarCount = true },
        new() { Style = VisualizerStyle.RadialBarsWide, DisplayName = "Радиальные бары (широкие)", FormFactors = VizFormFactors.Full, SupportsPeaks = true, SupportsBarCount = true },
        new() { Style = VisualizerStyle.RadialWave,   DisplayName = "Кольцо-волна",    FormFactors = VizFormFactors.Full                        },
        new() { Style = VisualizerStyle.Spectrum,     DisplayName = "Спектр",          FormFactors = VizFormFactors.All                         },
        new() { Style = VisualizerStyle.Oscilloscope, DisplayName = "Осциллограф",     FormFactors = VizFormFactors.All,  NeedsRawSamples = true },
        new() { Style = VisualizerStyle.Waterfall,    DisplayName = "Водопад",         FormFactors = VizFormFactors.Full | VizFormFactors.WideStrip },
        new() { Style = VisualizerStyle.LedMatrix,    DisplayName = "LED-матрица",     FormFactors = VizFormFactors.All,  SupportsPeaks = true, SupportsBarCount = true },
        new() { Style = VisualizerStyle.Particles,    DisplayName = "Частицы",         FormFactors = VizFormFactors.Full | VizFormFactors.WideStrip, SupportsPeaks = true, SupportsBarCount = true },
        new() { Style = VisualizerStyle.Dots,         DisplayName = "Точки",           FormFactors = VizFormFactors.All,  SupportsPeaks = true, SupportsBarCount = true },
        new() { Style = VisualizerStyle.Segments,     DisplayName = "Сегменты",        FormFactors = VizFormFactors.All,  SupportsPeaks = true, SupportsBarCount = true },
        new() { Style = VisualizerStyle.Pulse,        DisplayName = "Пульс",           FormFactors = VizFormFactors.Full                        },
        new() { Style = VisualizerStyle.Wave,         DisplayName = "Волна",           FormFactors = VizFormFactors.All                         },
    ];

    public static VizStyleInfo Get(VisualizerStyle style)
    {
        foreach (var info in Styles)
            if (info.Style == style)
                return info;
        return Styles[0];
    }

    public static IReadOnlyList<VizStyleInfo> ForFormFactor(VizFormFactors factor)
    {
        var list = new List<VizStyleInfo>(Styles.Length);
        foreach (var info in Styles)
            if ((info.FormFactors & factor) != 0)
                list.Add(info);
        return list;
    }
}
