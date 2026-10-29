namespace KebuzPlayer.Services;

public enum EqualizerPreset { Flat, Bass, Treble, Vocal, Rock, Electronic, Custom }

public static class EqualizerPresets
{
    public const int   BandCount = 10;
    public const float MaxGainDb = 12f;

    public static readonly float[] BandFrequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public static float[] GainsFor(EqualizerPreset preset) => preset switch
    {
        EqualizerPreset.Bass       => [6, 5, 4, 2, 0, 0, 0, 0, 0, 0],
        EqualizerPreset.Treble     => [0, 0, 0, 0, 0, 0, 2, 4, 5, 6],
        EqualizerPreset.Vocal      => [-2, -2, -1, 0, 2, 4, 4, 2, 0, -1],
        EqualizerPreset.Rock       => [5, 4, 2, -1, -2, -1, 1, 3, 4, 5],
        EqualizerPreset.Electronic => [5, 4, 1, 0, -2, 1, 0, 2, 4, 5],
        _                          => new float[BandCount],
    };

    public static float[] Normalize(IReadOnlyList<float>? gains)
    {
        var result = new float[BandCount];
        if (gains == null || gains.Count != BandCount) return result;
        for (int i = 0; i < BandCount; i++)
            result[i] = float.IsFinite(gains[i]) ? Math.Clamp(gains[i], -MaxGainDb, MaxGainDb) : 0f;
        return result;
    }
}

public sealed class Equalizer
{
    private float[]? _activeGains;

    public bool    Enabled { get; private set; }
    public float[] Gains   { get; private set; } = new float[EqualizerPresets.BandCount];

    public float[]? ActiveGains => Volatile.Read(ref _activeGains);

    public void Apply(bool enabled, IReadOnlyList<float> gains)
    {
        var normalized = EqualizerPresets.Normalize(gains);
        Enabled = enabled;
        Gains   = normalized;
        Volatile.Write(ref _activeGains, enabled && normalized.Any(g => g != 0f) ? normalized : null);
    }
}
