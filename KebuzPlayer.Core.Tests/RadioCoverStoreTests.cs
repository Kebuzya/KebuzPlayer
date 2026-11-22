using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class RadioCoverStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"KebuzTests_{Guid.NewGuid():N}");

    [Fact]
    public void CoversStayWithTheirStationsAndPruneOnlyRemovesUnusedCovers()
    {
        const string first = "https://example.test/first";
        const string second = "https://example.test/second";
        var store = new RadioCoverStore(_directory);
        Assert.Null(store.Load(first));
        store.Save(first, [0xff, 0xd8, 1]);
        store.Save(second, [0xff, 0xd8, 2]);
        store.Save(first, [0xff, 0xd8, 3]);
        var unrelated = Path.Combine(_directory, "station-not-managed.jpg");
        File.WriteAllBytes(unrelated, [0xff, 0xd8]);

        var reloaded = new RadioCoverStore(_directory);
        Assert.Equal(new byte[] { 0xff, 0xd8, 3 }, reloaded.Load(first));
        Assert.Equal(new byte[] { 0xff, 0xd8, 2 }, reloaded.Load(second));
        reloaded.KeepOnly([first]);
        Assert.Null(reloaded.Load(second));
        Assert.Equal(new byte[] { 0xff, 0xd8, 3 }, reloaded.Load(first));
        Assert.True(File.Exists(unrelated));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void VariantsRoundTripRecreatesTheSameArtworkAndPrunesWithTheStation()
    {
        const string first = "https://example.test/first";
        const string second = "https://example.test/second";
        var store = new RadioCoverStore(_directory);
        var features = TrackFeatures.Default with
        {
            IsFallback = false,
            DurationSec = 30,
            DisplaySpectrum = [0.1f, 0.5f, 0.8f, 0.2f],
            EnergyTrajectory = [0.2f, 0.8f, 0.3f],
            Segments = [new SegmentFeatures { Energy = 0.4f, HpRatio = 0.7f }],
        };
        var variants = new RadioCoverVariants(features, 0.32f, 4, 5, "First", "Radio");
        store.SaveVariants(first, variants);
        store.SaveVariants(second, variants with { Title = "Second", FirstVariant = 1 });

        var reloaded = new RadioCoverStore(_directory);
        Assert.NotNull(reloaded.LoadVariants(first));
        var restored = reloaded.LoadVariants(first)!;
        Assert.Equal(5, restored.FirstVariant);
        Assert.Equal(4, restored.BatchSize);
        Assert.Equal(Generate(variants, 6), Generate(restored, 6));

        reloaded.KeepOnly([first]);
        Assert.Null(reloaded.LoadVariants(second));
        Assert.NotNull(reloaded.LoadVariants(first));
    }

    private static byte[] Generate(RadioCoverVariants variants, int number)
    {
        var features = variants.Features;
        var seed = KebuzPlayer.Infrastructure.StableHash.Combine(CoverArtGenerator.FeatureSeed(features), number);
        var style = CoverArtGenerator.AutoSelectStyle(features, seed, variants.Chaos);
        return CoverArtGenerator.Generate(style, seed, features, variants.Chaos);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
