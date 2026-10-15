using NAudio.Dsp;
using NAudio.Wave;

namespace KebuzPlayer.Services;

public sealed class FftSampleProvider : ISampleProvider
{
    public  const int SpectrumSize = 1024;

    private readonly ISampleProvider _source;
    private readonly int       _fftSize;
    private readonly int       _m;
    private readonly float[]   _window;
    private readonly Complex[] _fftBuf;
    private readonly float[]   _ringBuf;
    private int _ringPos;
    private int _newSamples;

    private readonly float[] _spectrum = new float[SpectrumSize];
    private readonly object  _lock     = new();

    public WaveFormat WaveFormat => _source.WaveFormat;
    public float BinHz => (float)_source.WaveFormat.SampleRate / _fftSize;

    public static int FftSizeFor(int sampleRate) =>
        sampleRate > 128000 ? 8192 : sampleRate > 64000 ? 4096 : 2048;

    public FftSampleProvider(ISampleProvider source)
    {
        _source  = source;
        _fftSize = FftSizeFor(source.WaveFormat.SampleRate);
        _m       = (int)Math.Log2(_fftSize);
        _window  = new float[_fftSize];
        _fftBuf  = new Complex[_fftSize];
        _ringBuf = new float[_fftSize];
        for (int i = 0; i < _fftSize; i++)
            _window[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (_fftSize - 1)));
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        int ch   = WaveFormat.Channels;
        for (int n = 0; n < read; n += ch)
        {
            float mono = 0f;
            for (int c = 0; c < ch; c++) mono += buffer[offset + n + c];
            mono /= ch;
            _ringBuf[_ringPos & (_fftSize - 1)] = mono;
            _ringPos++;
            _newSamples++;
        }
        if (_newSamples >= _fftSize / 4)
        {
            _newSamples = 0;
            ComputeFft();
        }
        return read;
    }

    public void CopySpectrum(float[] dest)
    {
        lock (_lock)
            _spectrum.AsSpan().CopyTo(dest);
    }

    public void CopyRawSamples(float[] dest)
    {
        int rp = Volatile.Read(ref _ringPos);
        int n  = Math.Min(dest.Length, _fftSize);

        int search = (rp - n - 80 + _fftSize * 4) & (_fftSize - 1);
        int start  = search;
        for (int k = 0; k < 80; k++)
        {
            int a = (search + k)     & (_fftSize - 1);
            int b = (search + k + 1) & (_fftSize - 1);
            if (_ringBuf[a] <= 0f && _ringBuf[b] > 0f) { start = b; break; }
        }
        for (int i = 0; i < n; i++)
            dest[i] = _ringBuf[(start + i) & (_fftSize - 1)];
    }

    private void ComputeFft()
    {
        int rp = _ringPos;
        for (int i = 0; i < _fftSize; i++)
        {
            int idx = (rp + i) & (_fftSize - 1);
            _fftBuf[i].X = _ringBuf[idx] * _window[i];
            _fftBuf[i].Y = 0f;
        }
        FastFourierTransform.FFT(true, _m, _fftBuf);
        lock (_lock)
        {
            for (int i = 0; i < SpectrumSize; i++)
            {
                float re = _fftBuf[i].X, im = _fftBuf[i].Y;
                _spectrum[i] = MathF.Sqrt(re * re + im * im);
            }
        }
    }
}
