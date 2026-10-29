using NAudio.Wave;

namespace KebuzPlayer.Services;

public readonly record struct BiquadCoefficients(double B0, double B1, double B2, double A1, double A2)
{
    public static readonly BiquadCoefficients Identity = new(1, 0, 0, 0, 0);
}

public sealed class EqualizerSampleProvider : ISampleProvider
{
    private const double BandQ            = 1.41;
    private const double NyquistShare     = 0.45;
    private const float  LimiterCeiling   = 0.98f;
    private const double LimiterReleaseMs = 150;

    private readonly ISampleProvider      _source;
    private readonly Equalizer            _equalizer;
    private readonly int                  _channels;
    private readonly BiquadCoefficients[] _coefficients = new BiquadCoefficients[EqualizerPresets.BandCount];
    private readonly double[]             _z1;
    private readonly double[]             _z2;
    private readonly float                _release;
    private int[]    _activeBands = [];
    private float[]? _applied;
    private float    _limiterGain = 1f;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public EqualizerSampleProvider(ISampleProvider source, Equalizer equalizer)
    {
        _source    = source;
        _equalizer = equalizer;
        _channels  = source.WaveFormat.Channels;
        _z1        = new double[_channels * EqualizerPresets.BandCount];
        _z2        = new double[_channels * EqualizerPresets.BandCount];
        _release   = (float)Math.Exp(-1000 / (LimiterReleaseMs * source.WaveFormat.SampleRate));
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read  = _source.Read(buffer, offset, count);
        var gains = _equalizer.ActiveGains;
        if (gains == null)
        {
            _applied = null;
            return read;
        }
        if (!ReferenceEquals(gains, _applied)) Configure(gains);

        for (int n = 0; n < read; n += _channels)
        {
            float peak = 0f;
            for (int c = 0; c < _channels; c++)
            {
                double x = buffer[offset + n + c];
                foreach (int b in _activeBands)
                {
                    int s = c * EqualizerPresets.BandCount + b;
                    var k = _coefficients[b];
                    double y = k.B0 * x + _z1[s];
                    _z1[s] = k.B1 * x - k.A1 * y + _z2[s];
                    _z2[s] = k.B2 * x - k.A2 * y;
                    x = y;
                }
                buffer[offset + n + c] = (float)x;
                peak = Math.Max(peak, Math.Abs((float)x));
            }
            float target = peak > LimiterCeiling ? LimiterCeiling / peak : 1f;
            _limiterGain = target < _limiterGain ? target : target - (target - _limiterGain) * _release;
            if (_limiterGain < 1f)
                for (int c = 0; c < _channels; c++)
                    buffer[offset + n + c] *= _limiterGain;
        }
        FlushDenormals();
        return read;
    }

    private void Configure(float[] gains)
    {
        if (_applied == null)
        {
            Array.Clear(_z1);
            Array.Clear(_z2);
            _limiterGain = 1f;
        }
        int sampleRate = WaveFormat.SampleRate;
        var active = new List<int>();
        for (int b = 0; b < EqualizerPresets.BandCount; b++)
        {
            _coefficients[b] = Coefficients(gains[b], EqualizerPresets.BandFrequencies[b], sampleRate);
            if (_coefficients[b] != BiquadCoefficients.Identity) active.Add(b);
        }
        _activeBands = [.. active];
        _applied     = gains;
    }

    private void FlushDenormals()
    {
        for (int i = 0; i < _z1.Length; i++)
        {
            if (Math.Abs(_z1[i]) < 1e-15) _z1[i] = 0;
            if (Math.Abs(_z2[i]) < 1e-15) _z2[i] = 0;
        }
    }

    public static BiquadCoefficients Coefficients(float gainDb, double centreHz, int sampleRate)
    {
        if (gainDb == 0f || centreHz >= sampleRate * NyquistShare) return BiquadCoefficients.Identity;
        double a     = Math.Pow(10, gainDb / 40.0);
        double w0    = 2 * Math.PI * centreHz / sampleRate;
        double cos   = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2 * BandQ);
        double a0    = 1 + alpha / a;
        return new BiquadCoefficients(
            (1 + alpha * a) / a0,
            -2 * cos / a0,
            (1 - alpha * a) / a0,
            -2 * cos / a0,
            (1 - alpha / a) / a0);
    }
}
