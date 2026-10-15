using KebuzPlayer.Services;
using Xunit.Abstractions;

namespace KebuzPlayer.Core.Tests;

public class TrackAnalyzerTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public TrackAnalyzerTests(ITestOutputHelper output)
    {
        _out = output;
        _dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string CreateWav(string name, int seconds, Func<int, double> sample, int rate = 44100)
    {
        int total = rate * seconds;
        var path = Path.Combine(_dir, name);
        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);
        int dataSize = total * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataSize);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataSize);
        for (int i = 0; i < total; i++)
            w.Write((short)(Math.Clamp(sample(i), -1.0, 1.0) * short.MaxValue));
        return path;
    }

    private string Sine(string name = "sine.wav", double amp = 0.5, int seconds = 40) =>
        CreateWav(name, seconds, i => amp * Math.Sin(2 * Math.PI * 440 * i / 44100.0));

    private string Noise(string name = "noise.wav", int seconds = 40)
    {
        var rng = new Random(42);
        return CreateWav(name, seconds, _ => 0.3 * (rng.NextDouble() * 2 - 1));
    }

    private string Kick(string name = "kick.wav", int seconds = 40)
    {
        var rng = new Random(7);
        return CreateWav(name, seconds, i =>
        {
            int t = i % 22050;
            return t < 3000
                ? Math.Sin(2 * Math.PI * 55 * t / 44100.0) * Math.Exp(-t / 400.0)
                  + 0.4 * (rng.NextDouble() * 2 - 1) * Math.Exp(-t / 150.0)
                : 0.0;
        });
    }

    private void Dump(string label, TrackFeatures f)
    {
        _out.WriteLine($"{label}: segs={f.Segments.Length} " +
            $"Onset={f.OnsetDensity:F2}/s HP={f.HPRatio:F2} Autocorr={f.Autocorrelation:F2} " +
            $"DR={f.DynamicRangeDb:F1}dB EnergyCv={f.EnergyCv:F2} FluxCV={f.FluxStdDev:F2} " +
            $"Bass={f.Bass:F2} Mid={f.Mid:F2} High={f.High:F2} Centroid={f.Centroid:F2}");
    }

    [Fact]
    public void Analyze_IsDeterministic()
    {
        var path = Sine("det.wav", seconds: 20);
        var a = TrackAnalyzer.Analyze(path);
        var b = TrackAnalyzer.Analyze(path);

        Assert.Equal(a.Spectrum, b.Spectrum);
        Assert.Equal(a.OnsetDensity, b.OnsetDensity);
        Assert.Equal(a.HPRatio, b.HPRatio);
        Assert.Equal(a.DynamicRangeDb, b.DynamicRangeDb);
        Assert.Equal(a.Rms, b.Rms);
        Assert.Equal(a.Centroid, b.Centroid);
        Assert.Equal(a.Spread, b.Spread);
        Assert.Equal(a.HarmonicStability, b.HarmonicStability);
        Assert.Equal(a.EnergyTrajectory, b.EnergyTrajectory);
        Assert.Equal(a.Segments.Length, b.Segments.Length);
        for (int i = 0; i < a.Segments.Length; i++)
        {
            Assert.Equal(a.Segments[i].Energy, b.Segments[i].Energy);
            Assert.Equal(a.Segments[i].OnsetRate, b.Segments[i].OnsetRate);
        }
    }

    [Fact]
    public void Sine_IsHarmonicStableAndQuietDynamics()
    {
        var f = TrackAnalyzer.Analyze(Sine());
        Dump("SINE", f);

        Assert.False(f.IsFallback);
        Assert.True(f.HPRatio > 0.75f);
        Assert.True(f.Autocorrelation > 0.9f);
        Assert.True(f.OnsetDensity < 0.4f);
        Assert.True(f.DynamicRangeDb < 3f);
        Assert.True(f.Mid > 0.5f);
        Assert.True(f.HarmonicStability > 0.9f);
    }

    [Fact]
    public void Noise_HasNoOnsetsAndFlatDynamics()
    {
        var f = TrackAnalyzer.Analyze(Noise());
        Dump("NOISE", f);

        Assert.False(f.IsFallback);
        Assert.True(f.OnsetDensity < 0.4f);
        Assert.True(f.DynamicRangeDb < 3f);
        Assert.True(f.Centroid > 0.6f);
        Assert.True(f.Autocorrelation < 0.3f);
    }

    [Fact]
    public void Kick_IsPercussiveWithOnsets()
    {
        var f = TrackAnalyzer.Analyze(Kick());
        Dump("KICK", f);

        Assert.False(f.IsFallback);
        Assert.True(f.HPRatio < 0.45f);
        Assert.InRange(f.OnsetDensity, 1.0f, 4.0f);
        Assert.True(f.Bass > 0.5f);
    }

    [Fact]
    public void QuietAndLoudHalves_GiveLargeDynamicRange()
    {
        var path = CreateWav("halves.wav", 40, i =>
        {
            double amp = i < 44100 * 20 ? 0.04 : 0.6;
            return amp * Math.Sin(2 * Math.PI * 330 * i / 44100.0);
        });
        var f = TrackAnalyzer.Analyze(path);
        Dump("HALVES", f);

        Assert.True(f.DynamicRangeDb > 10f);
        Assert.True(f.EnergyCv > 0.3f);
        Assert.True(f.EnergyTrajectory.First() < 0.3f);
        Assert.Equal(1f, f.EnergyTrajectory.Max(), 3);
    }

    [Fact]
    public void FlatAndRampTracks_HaveDifferentTrajectories()
    {
        var flat = TrackAnalyzer.Analyze(Sine("flat.wav", amp: 0.4));
        var ramp = TrackAnalyzer.Analyze(CreateWav("ramp.wav", 40, i =>
            (0.05 + 0.55 * i / (44100.0 * 40)) * Math.Sin(2 * Math.PI * 440 * i / 44100.0)));

        Dump("FLAT", flat);
        Dump("RAMP", ramp);

        float diff = flat.EnergyTrajectory
            .Zip(ramp.EnergyTrajectory, (a, b) => MathF.Abs(a - b))
            .Sum();
        Assert.True(diff > 1.0f);
    }

    [Fact]
    public void ShortOrMissingFile_ReturnsFallbackDefault()
    {
        var missing = TrackAnalyzer.Analyze(Path.Combine(_dir, "missing.wav"));
        Assert.True(missing.IsFallback);

        var tiny = TrackAnalyzer.Analyze(CreateWav("tiny.wav", 1, i => 0.3 * Math.Sin(2 * Math.PI * 440 * i / 44100.0)));
        Assert.True(tiny.IsFallback);
    }

    [Fact]
    public void Analyze_ProducesTwelveSegmentsOnNormalTrack()
    {
        var f = TrackAnalyzer.Analyze(Sine("segcount.wav", seconds: 60));
        Assert.Equal(12, f.Segments.Length);
        Assert.Equal(12, f.EnergyTrajectory.Length);
        Assert.Equal(512, f.Spectrum.Length);
        Assert.Equal(512, f.DisplaySpectrum.Length);
    }

    [Fact]
    public void Analyze_IsIndependentOfSampleRate()
    {
        double Signal(int i, int rate)
        {
            double t = (double)i / rate;
            return 0.3 * Math.Sin(2 * Math.PI * 220 * t)
                 + 0.2 * Math.Sin(2 * Math.PI * 880 * t)
                 + 0.1 * Math.Sin(2 * Math.PI * 3520 * t);
        }
        var a = TrackAnalyzer.Analyze(CreateWav("sr44.wav", 30, i => Signal(i, 44100), 44100));
        var b = TrackAnalyzer.Analyze(CreateWav("sr48.wav", 30, i => Signal(i, 48000), 48000));
        Dump("SR44", a);
        Dump("SR48", b);

        Assert.True(MathF.Abs(a.Centroid - b.Centroid) < 0.05f);
        Assert.True(MathF.Abs(a.Bass - b.Bass) < 0.05f);
        Assert.True(MathF.Abs(a.Mid - b.Mid) < 0.05f);
        Assert.True(MathF.Abs(a.High - b.High) < 0.05f);
        Assert.True(MathF.Abs(a.HPRatio - b.HPRatio) < 0.07f);
        Assert.True(MathF.Abs(a.Spread - b.Spread) < 0.05f);

        float meanDiff = a.Spectrum.Zip(b.Spectrum, (x, y) => MathF.Abs(x - y)).Average();
        Assert.True(meanDiff < 0.03f);
        int peakA = Array.IndexOf(a.Spectrum, a.Spectrum.Max());
        int peakB = Array.IndexOf(b.Spectrum, b.Spectrum.Max());
        Assert.True(Math.Abs(peakA - peakB) <= 2);
    }

    [Fact]
    public void Bands_SplitByRealFrequencies()
    {
        var bass = TrackAnalyzer.Analyze(CreateWav("b100.wav", 20, i => 0.5 * Math.Sin(2 * Math.PI * 100 * i / 44100.0)));
        var mid = TrackAnalyzer.Analyze(CreateWav("m1k.wav", 20, i => 0.5 * Math.Sin(2 * Math.PI * 1000 * i / 44100.0)));
        var high = TrackAnalyzer.Analyze(CreateWav("h14k.wav", 20, i => 0.5 * Math.Sin(2 * Math.PI * 14000 * i / 44100.0)));
        Dump("B100", bass);
        Dump("M1K", mid);
        Dump("H14K", high);

        Assert.True(bass.Bass > 0.8f);
        Assert.True(mid.Mid > 0.8f);
        Assert.True(high.High > 0.8f);
    }

    [Fact]
    public void DynamicRange_CatchesShortBurstsInsideSegments()
    {
        var path = CreateWav("bursts.wav", 40, i =>
        {
            double t = i / 44100.0;
            double phase = ((t - 1.6) % 3.3455 + 3.3455) % 3.3455;
            double amp = phase < 0.5 ? 0.6 : 0.02;
            return amp * Math.Sin(2 * Math.PI * 330 * i / 44100.0);
        });
        var f = TrackAnalyzer.Analyze(path);
        Dump("BURSTS", f);

        Assert.True(f.DynamicRangeDb > 12f);
    }

    [Fact]
    public void Spread_RanksNoiseAboveSine()
    {
        var sine = TrackAnalyzer.Analyze(Sine("spread_sine.wav"));
        var noise = TrackAnalyzer.Analyze(Noise("spread_noise.wav"));

        Assert.True(noise.Spread > sine.Spread + 0.2f);
    }

    [Fact]
    public void Rms_GrowsWithLoudness()
    {
        var quiet = TrackAnalyzer.Analyze(Sine("quiet.wav", amp: 0.05));
        var loud = TrackAnalyzer.Analyze(Sine("loud.wav", amp: 0.6));

        Assert.True(loud.Rms > quiet.Rms + 0.2f);
    }
}
