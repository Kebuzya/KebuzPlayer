namespace KebuzPlayer.Services;

public sealed class SpectrumProcessor
{
    public const int   BarCount    = 64;
    private const float ScaleFactor = 10f;
    private const float Attack      = 0.60f;
    private const float Decay       = 0.88f;
    private const float PeakDecay   = 0.016f;
    private const float ReferenceFps = 30f;
    private const float MinSteps     = 0.05f;
    private const float MaxSteps     = 4f;
    public  const float ReferenceBinHz = 44100f / 2048f;
    private const int   MaxCachedMaps  = 16;

    private static readonly Dictionary<int, int[]> BinMaps = new();

    private static readonly float[] FreqBoost = BuildFreqBoost();

    private readonly float[] _smooth = new float[BarCount];
    private readonly float[] _peaks  = new float[BarCount];

    public float[] Smooth => _smooth;
    public float[] Peaks  => _peaks;

    public bool HasVisibleContent { get; private set; }
    public bool HasChanged        { get; private set; }

    public void Update(float[] spectrum) => Update(spectrum, 1f / ReferenceFps);

    public void Update(float[] spectrum, float deltaSeconds) =>
        Update(spectrum, deltaSeconds, ReferenceBinHz);

    public void Update(float[] spectrum, float deltaSeconds, float binHz)
    {
        var binMap = BinMapFor(binHz);
        float steps     = Math.Clamp(deltaSeconds * ReferenceFps, MinSteps, MaxSteps);
        float attack    = 1f - MathF.Pow(1f - Attack, steps);
        float decay     = MathF.Pow(Decay, steps);
        float peakDecay = PeakDecay * steps;

        bool visible = false;
        bool changed = false;
        for (int i = 0; i < BarCount; i++)
        {
            int start = i == 0 ? 0 : binMap[i - 1];
            int end   = binMap[i];
            if (end <= start) end = start + 1;

            float val = 0f;
            for (int b = start; b < end && b < spectrum.Length; b++)
                val = Math.Max(val, spectrum[b]);
            val = Math.Clamp(val * ScaleFactor * FreqBoost[i], 0f, 1f);
            val = MathF.Log10(1f + val * 9f);

            float prevSmooth = _smooth[i];
            float prevPeak   = _peaks[i];

            _smooth[i] = val > _smooth[i]
                ? _smooth[i] + (val - _smooth[i]) * attack
                : _smooth[i] * decay;

            _peaks[i] = _smooth[i] >= _peaks[i]
                ? _smooth[i]
                : Math.Max(0f, _peaks[i] - peakDecay);

            if (MathF.Abs(_smooth[i] - prevSmooth) > 0.001f ||
                MathF.Abs(_peaks[i]  - prevPeak)   > 0.001f) changed = true;
            if (_smooth[i] > 0.005f || _peaks[i] > 0.01f) visible = true;
        }

        if (HasVisibleContent != visible) changed = true;
        HasVisibleContent = visible;
        HasChanged        = changed;
    }

    private static int[] BinMapFor(float binHz)
    {
        if (!(binHz > 0)) binHz = ReferenceBinHz;
        int key = (int)MathF.Round(binHz * 1000f);
        lock (BinMaps)
        {
            if (BinMaps.TryGetValue(key, out var map)) return map;
            if (BinMaps.Count >= MaxCachedMaps) BinMaps.Clear();
            return BinMaps[key] = BuildBinMap(binHz);
        }
    }

    private static int[] BuildBinMap(float binHz)
    {
        const float lo     = 20f;
        float       hi     = Math.Min(18000f, binHz * FftSampleProvider.SpectrumSize * 0.9f);
        int         maxBin = Math.Min((int)(hi / binHz), FftSampleProvider.SpectrumSize - 1);

        var map  = new int[BarCount];
        int prev = 0;
        for (int i = 0; i < BarCount; i++)
        {
            double t   = (double)(i + 1) / BarCount;
            int    bin = (int)(lo * Math.Pow(hi / lo, t) / binHz);
            bin        = Math.Max(bin, prev + 1);
            map[i]     = Math.Clamp(bin, 1, maxBin);
            prev       = map[i];
        }
        return map;
    }

    private static float[] BuildFreqBoost()
    {
        var boost = new float[BarCount];
        for (int i = 0; i < BarCount; i++)
            boost[i] = 1f + (float)i / (BarCount - 1) * 4.0f;
        return boost;
    }
}
