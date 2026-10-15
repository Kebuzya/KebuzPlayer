using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;

namespace KebuzPlayer.Services;

public static class MetaCoverArtGenerator
{
    public static byte[] Generate(Track track, int variant = 0, float chaos = 0f)
    {
        int seed = StableHash.Combine(MetaSeed(track), variant);
        var style = (CoverArtStyle)((uint)seed % (uint)Enum.GetValues<CoverArtStyle>().Length);
        var features = SyntheticFeatures(seed);
        return CoverArtGenerator.Generate(style, seed, features, chaos);
    }

    private static int MetaSeed(Track t)
    {
        int h = t.Id * 1_000_003;
        foreach (char c in t.Title)        h = h * 31 + c;
        foreach (char c in t.Artist ?? "") h = h * 37 + c;
        foreach (char c in t.Album  ?? "") h = h * 41 + c;
        return h;
    }

    private static TrackFeatures SyntheticFeatures(int seed)
    {
        var rng = new Random(seed);
        const int n = 40;
        var spec = new float[n];
        for (int i = 0; i < n; i++) spec[i] = MathF.Pow(rng.NextSingle(), 1.5f);

        return TrackFeatures.Default with
        {
            Spectrum = spec,
            DisplaySpectrum = spec,
            Bass = 0.30f + rng.NextSingle() * 0.5f,
            Mid  = 0.20f + rng.NextSingle() * 0.5f,
            High = 0.10f + rng.NextSingle() * 0.5f,
            DynamicRangeDb = 6f + rng.NextSingle() * 18f,
            Rms = 0.30f + rng.NextSingle() * 0.60f,
            EnergyCv = rng.NextSingle() * 0.60f,
            OnsetDensity = rng.NextSingle() * 4f,
            Peaks = rng.Next(2, 24),
            Centroid = 0.30f + rng.NextSingle() * 0.50f,
            Spread = 0.20f + rng.NextSingle() * 0.60f,
        };
    }
}
