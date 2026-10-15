namespace KebuzPlayer.Services;

public sealed class SegmentFeatures
{
    public float Energy { get; init; }
    public float Centroid { get; init; }
    public float Flux { get; init; }
    public float OnsetRate { get; init; }
    public float HpRatio { get; init; }
    public float HarmonicEnergy { get; init; }
    public float Autocorrelation { get; init; }
    public float Bass { get; init; }
    public float Mid { get; init; }
    public float High { get; init; }
    public float Spread { get; init; }
}

public sealed record TrackFeatures
{
    public float[] Spectrum { get; init; } = Array.Empty<float>();
    public float[] DisplaySpectrum { get; init; } = Array.Empty<float>();

    public SegmentFeatures[] Segments { get; init; } = Array.Empty<SegmentFeatures>();
    public float[] EnergyTrajectory { get; init; } = Array.Empty<float>();
    public float DurationSec { get; init; }
    public float DynamicRangeDb { get; init; }
    public float OnsetDensity { get; init; }
    public float EnergyCv { get; init; }
    public bool IsFallback { get; init; }

    public float Bass { get; init; }
    public float Mid  { get; init; }
    public float High { get; init; }

    public float SpectralFlux    { get; init; }
    public float FluxStdDev      { get; init; }
    public float Autocorrelation { get; init; }
    public float AttackRate      { get; init; }

    public float HPRatio           { get; init; }
    public float HarmonicStability { get; init; }
    public float PercussiveDensity { get; init; }

    public float Centroid { get; init; }
    public float Spread   { get; init; }
    public int   Peaks    { get; init; }
    public float Rms      { get; init; }

    public static readonly TrackFeatures Default = new()
    {
        IsFallback = true,
        Spectrum = Array.Empty<float>(),
        Bass = 0.50f, Mid = 0.30f, High = 0.20f,
        SpectralFlux = 0.05f, FluxStdDev = 0.50f,
        Autocorrelation = 0.85f, AttackRate = 0.10f,
        HPRatio = 0.65f, HarmonicStability = 0.75f, PercussiveDensity = 0.10f,
        Centroid = 0.67f, Spread = 0.42f, Peaks = 8, Rms = 0.5f
    };
}
