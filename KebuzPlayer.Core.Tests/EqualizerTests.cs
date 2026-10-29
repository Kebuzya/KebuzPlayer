using KebuzPlayer.Services;
using NAudio.Wave;

namespace KebuzPlayer.Core.Tests;

public class EqualizerTests : IDisposable
{
    private readonly string _dir;

    public EqualizerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class SineSource(double hz, float amplitude, int sampleRate = 44100) : ISampleProvider
    {
        private long _frame;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i += 2)
            {
                float v = amplitude * (float)Math.Sin(2 * Math.PI * hz * _frame++ / WaveFormat.SampleRate);
                buffer[offset + i]     = v;
                buffer[offset + i + 1] = v;
            }
            return count;
        }
    }

    private static float SteadyPeak(ISampleProvider provider, double seconds = 1.0)
    {
        int total = (int)(provider.WaveFormat.SampleRate * seconds) * 2;
        var buffer = new float[1024];
        float peak = 0f;
        for (int done = 0; done < total; done += buffer.Length)
        {
            provider.Read(buffer, 0, buffer.Length);
            if (done < total * 3 / 4) continue;
            foreach (var v in buffer)
            {
                Assert.True(float.IsFinite(v));
                peak = Math.Max(peak, Math.Abs(v));
            }
        }
        return peak;
    }

    private static float Ratio(Equalizer eq, double hz, int sampleRate = 44100) =>
        SteadyPeak(new EqualizerSampleProvider(new SineSource(hz, 0.5f, sampleRate), eq)) / 0.5f;

    private static Equalizer With(bool enabled, float[] gains)
    {
        var eq = new Equalizer();
        eq.Apply(enabled, gains);
        return eq;
    }

    [Fact]
    public void Disabled_PassesSamplesUntouched()
    {
        var eq = With(false, EqualizerPresets.GainsFor(EqualizerPreset.Rock));
        var plain = new float[4096];
        var equalized = new float[4096];

        new SineSource(440, 0.7f).Read(plain, 0, plain.Length);
        new EqualizerSampleProvider(new SineSource(440, 0.7f), eq).Read(equalized, 0, equalized.Length);

        Assert.Null(eq.ActiveGains);
        Assert.Equal(plain, equalized);
    }

    [Fact]
    public void FlatGains_AreBypassedEvenWhenEnabled()
    {
        var eq = With(true, EqualizerPresets.GainsFor(EqualizerPreset.Flat));

        Assert.True(eq.Enabled);
        Assert.Null(eq.ActiveGains);
    }

    [Fact]
    public void BassPreset_RaisesLowToneAgainstHighTone()
    {
        var eq = With(true, EqualizerPresets.GainsFor(EqualizerPreset.Bass));

        Assert.True(Ratio(eq, 62) / Ratio(eq, 4000) > 1.5f);
    }

    [Fact]
    public void Cut_LowersThatBand()
    {
        var gains = new float[EqualizerPresets.BandCount];
        gains[5] = -12f;
        var eq = With(true, gains);

        Assert.True(Ratio(eq, 1000) < 0.4f);
        Assert.InRange(Ratio(eq, 62), 0.95f, 1.05f);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(62)]
    [InlineData(1000)]
    [InlineData(16000)]
    public void FullBoost_NeverGoesPastFullScale(double hz)
    {
        var gains = Enumerable.Repeat(EqualizerPresets.MaxGainDb, EqualizerPresets.BandCount).ToArray();

        Assert.True(SteadyPeak(new EqualizerSampleProvider(new SineSource(hz, 0.9f), With(true, gains))) <= 0.981f);
    }

    [Fact]
    public void Boost_DoesNotQuietenTheRestOfTheTrack()
    {
        var gains = new float[EqualizerPresets.BandCount];
        gains[5] = 6f;
        var eq = With(true, gains);

        Assert.InRange(SteadyPeak(new EqualizerSampleProvider(new SineSource(62, 0.1f), eq)) / 0.1f, 0.95f, 1.05f);
        Assert.True(SteadyPeak(new EqualizerSampleProvider(new SineSource(1000, 0.1f), eq)) / 0.1f > 1.8f);
    }

    [Fact]
    public void BandsAboveNyquist_AreSkippedAtLowSampleRate()
    {
        var eq = With(true, EqualizerPresets.GainsFor(EqualizerPreset.Treble));

        Assert.Equal(BiquadCoefficients.Identity, EqualizerSampleProvider.Coefficients(6f, 16000, 22050));
        Assert.InRange(Ratio(eq, 1000, 22050), 0.9f, 1.2f);
    }

    [Fact]
    public void ChangeWhilePlaying_IsPickedUpByTheRunningProvider()
    {
        var eq = new Equalizer();
        var provider = new EqualizerSampleProvider(new SineSource(1000, 0.5f), eq);
        Assert.InRange(SteadyPeak(provider, 0.3) / 0.5f, 0.99f, 1.01f);

        var gains = new float[EqualizerPresets.BandCount];
        gains[5] = -12f;
        eq.Apply(true, gains);

        Assert.True(SteadyPeak(provider, 0.5) / 0.5f < 0.4f);
    }

    [Fact]
    public void Presets_HaveEveryBandWithinRange()
    {
        foreach (var preset in Enum.GetValues<EqualizerPreset>())
        {
            var gains = EqualizerPresets.GainsFor(preset);
            Assert.Equal(EqualizerPresets.BandCount, gains.Length);
            Assert.All(gains, g => Assert.InRange(g, -EqualizerPresets.MaxGainDb, EqualizerPresets.MaxGainDb));
        }
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var path = Path.Combine(_dir, "settings.json");
        var svc = new JsonSettingsService(path);
        svc.Current.EqualizerEnabled = true;
        svc.Current.EqualizerPreset = EqualizerPreset.Custom;
        svc.Current.EqualizerCustomGains[0] = 4.5f;
        svc.Current.EqualizerCustomGains[9] = -3f;
        svc.Save();

        var reloaded = new JsonSettingsService(path).Current;
        Assert.True(reloaded.EqualizerEnabled);
        Assert.Equal(EqualizerPreset.Custom, reloaded.EqualizerPreset);
        Assert.Equal(4.5f, reloaded.EqualizerCustomGains[0]);
        Assert.Equal(-3f, reloaded.EqualizerCustomGains[9]);
    }

    [Fact]
    public void Settings_BadValuesAreRepaired()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{\"EqualizerPreset\":\"Bogus\",\"EqualizerCustomGains\":[50,1,2]}");
        var shortArray = new JsonSettingsService(path).Current;

        File.WriteAllText(path, "{\"EqualizerCustomGains\":[50,0,0,0,0,0,0,0,0,-50]}");
        var outOfRange = new JsonSettingsService(path).Current;

        Assert.Equal(EqualizerPreset.Flat, shortArray.EqualizerPreset);
        Assert.Equal(new float[EqualizerPresets.BandCount], shortArray.EqualizerCustomGains);
        Assert.Equal(EqualizerPresets.MaxGainDb, outOfRange.EqualizerCustomGains[0]);
        Assert.Equal(-EqualizerPresets.MaxGainDb, outOfRange.EqualizerCustomGains[9]);
    }
}
