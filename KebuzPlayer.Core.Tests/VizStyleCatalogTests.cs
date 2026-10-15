using System.Linq;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class VizStyleCatalogTests
{
    [Fact]
    public void Catalog_HasExactlyOneEntryPerEnumValue()
    {
        var enumValues = Enum.GetValues<VisualizerStyle>();
        Assert.Equal(enumValues.Length, VizStyleCatalog.Styles.Length);
        foreach (var value in enumValues)
            Assert.Single(VizStyleCatalog.Styles, s => s.Style == value);
    }

    [Fact]
    public void Catalog_AllEntriesHaveDisplayNames()
    {
        foreach (var info in VizStyleCatalog.Styles)
            Assert.False(string.IsNullOrWhiteSpace(info.DisplayName));
    }

    [Fact]
    public void Get_ReturnsMatchingEntryForEveryValue()
    {
        foreach (var value in Enum.GetValues<VisualizerStyle>())
            Assert.Equal(value, VizStyleCatalog.Get(value).Style);
    }

    [Fact]
    public void ForFormFactor_WideStrip_ExcludesRadialStyles()
    {
        var styles = VizStyleCatalog.ForFormFactor(VizFormFactors.WideStrip);
        Assert.DoesNotContain(styles, s => s.Style == VisualizerStyle.RadialBars);
        Assert.DoesNotContain(styles, s => s.Style == VisualizerStyle.RadialWave);
        Assert.Contains(styles, s => s.Style == VisualizerStyle.Bars);
    }

    [Fact]
    public void ForFormFactor_Compact_ExcludesRadialStyles()
    {
        var styles = VizStyleCatalog.ForFormFactor(VizFormFactors.Compact);
        Assert.DoesNotContain(styles, s => s.Style == VisualizerStyle.RadialBars);
        Assert.DoesNotContain(styles, s => s.Style == VisualizerStyle.RadialWave);
    }

    [Fact]
    public void ForFormFactor_Full_ReturnsAllStyles()
    {
        var styles = VizStyleCatalog.ForFormFactor(VizFormFactors.Full);
        Assert.Equal(VizStyleCatalog.Styles.Length, styles.Count);
    }

    [Fact]
    public void OnlyOscilloscopeNeedsRawSamples()
    {
        foreach (var info in VizStyleCatalog.Styles)
            Assert.Equal(info.Style == VisualizerStyle.Oscilloscope, info.NeedsRawSamples);
    }

    [Fact]
    public void SupportsBarCount_OnlyDiscreteBarStyles()
    {
        VisualizerStyle[] expected =
        [
            VisualizerStyle.Bars, VisualizerStyle.Mirror, VisualizerStyle.RadialBars,
            VisualizerStyle.RadialBarsWide, VisualizerStyle.LedMatrix, VisualizerStyle.Particles,
            VisualizerStyle.Dots, VisualizerStyle.Segments,
        ];
        foreach (var info in VizStyleCatalog.Styles)
            Assert.Equal(expected.Contains(info.Style), info.SupportsBarCount);
    }

    [Fact]
    public void Wave_IsLastInCatalogOrder()
    {
        Assert.Equal(VisualizerStyle.Wave, VizStyleCatalog.Styles[^1].Style);
    }
}
