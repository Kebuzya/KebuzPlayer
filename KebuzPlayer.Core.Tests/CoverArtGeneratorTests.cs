using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using System.Drawing;
using System.Drawing.Imaging;
using Xunit.Abstractions;

namespace KebuzPlayer.Core.Tests;

public class StableHashTests
{
    [Theory]
    [InlineData("", -2128831035)]
    [InlineData("a", 723832900)]
    [InlineData("KebuzPlayer", -668787859)]
    [InlineData("Трек", -1571049264)]
    public void Fnv1a_MatchesReferenceValues(string input, int expected)
    {
        Assert.Equal(expected, StableHash.Fnv1a(input));
    }

    [Fact]
    public void Combine_IsOrderSensitive()
    {
        Assert.NotEqual(StableHash.Combine(1, 2), StableHash.Combine(2, 1));
        Assert.Equal(StableHash.Combine(1, 2), StableHash.Combine(1, 2));
    }
}

public class CoverArtGeneratorTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public CoverArtGeneratorTests(ITestOutputHelper output)
    {
        _out = output;
        _dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string CreateWav(string name, int seconds, Func<int, double> sample)
    {
        const int rate = 44100;
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

    [Fact]
    public void FeatureSeed_IsDeterministicAcrossReanalysis()
    {
        var path = CreateWav("seed.wav", 30, i =>
            0.4 * Math.Sin(2 * Math.PI * 440 * i / 44100.0) +
            0.1 * Math.Sin(2 * Math.PI * 3000 * i / 44100.0));

        var seed1 = CoverArtGenerator.FeatureSeed(TrackAnalyzer.Analyze(path));
        var seed2 = CoverArtGenerator.FeatureSeed(TrackAnalyzer.Analyze(path));
        Assert.Equal(seed1, seed2);
    }

    [Fact]
    public void FeatureSeed_DiffersForDifferentTrajectories()
    {
        var flat = CreateWav("flat.wav", 30, i => 0.4 * Math.Sin(2 * Math.PI * 440 * i / 44100.0));
        var ramp = CreateWav("ramp.wav", 30, i =>
            (0.05 + 0.55 * i / (44100.0 * 30)) * Math.Sin(2 * Math.PI * 440 * i / 44100.0));

        Assert.NotEqual(
            CoverArtGenerator.FeatureSeed(TrackAnalyzer.Analyze(flat)),
            CoverArtGenerator.FeatureSeed(TrackAnalyzer.Analyze(ramp)));
    }

    [Fact]
    public void Generate_IsByteIdenticalForSameInputs()
    {
        var path = CreateWav("gen.wav", 20, i => 0.4 * Math.Sin(2 * Math.PI * 440 * i / 44100.0));
        var f = TrackAnalyzer.Analyze(path);
        var seed = CoverArtGenerator.FeatureSeed(f);
        var style = CoverArtGenerator.AutoSelectStyle(f, seed, 0.3f);

        var png1 = CoverArtGenerator.Generate(style, seed, f, 0.3f);
        var png2 = CoverArtGenerator.Generate(style, seed, f, 0.3f);
        Assert.Equal(png1, png2);
        Assert.True(png1.Length > 1000);
    }

    [Fact]
    public void AutoSelectStyle_DistinguishesSyntheticSignals()
    {
        var sine = CreateWav("sine.wav", 30, i => 0.5 * Math.Sin(2 * Math.PI * 440 * i / 44100.0));
        var noiseRng = new Random(42);
        var noise = CreateWav("noise.wav", 30, _ => 0.3 * (noiseRng.NextDouble() * 2 - 1));
        var kickRng = new Random(7);
        var kick = CreateWav("kick.wav", 30, i =>
        {
            int t = i % 22050;
            return t < 3000
                ? Math.Sin(2 * Math.PI * 55 * t / 44100.0) * Math.Exp(-t / 400.0)
                  + 0.4 * (kickRng.NextDouble() * 2 - 1) * Math.Exp(-t / 150.0)
                : 0.0;
        });

        var styles = new[] { sine, noise, kick }
            .Select(p =>
            {
                var f = TrackAnalyzer.Analyze(p);
                return CoverArtGenerator.AutoSelectStyle(f, CoverArtGenerator.FeatureSeed(f), 0f);
            })
            .ToArray();

        Assert.Equal(3, styles.Distinct().Count());
    }

    [Fact]
    public void AutoSelectStyle_AllFourteenFamiliesReachableWithoutChaos()
    {
        var found = new HashSet<CoverArtStyle>();
        for (float harm = 0.025f; harm < 1f; harm += 0.05f)
            for (float aggr = 0.025f; aggr < 1f; aggr += 0.05f)
                for (int seed = 0; seed < 3; seed++)
                {
                    var f = new TrackFeatures
                    {
                        HPRatio = harm,
                        OnsetDensity = aggr * 5f,
                        EnergyCv = aggr * 0.5f,
                        Autocorrelation = 1f - aggr,
                    };
                    found.Add(CoverArtGenerator.AutoSelectStyle(f, seed, 0f));
                }

        Assert.Equal(14, found.Count);
    }

    [Fact]
    public void ZoneDistribution_Diagnostic()
    {
        var dir = Environment.GetEnvironmentVariable("KEBUZ_SAMPLE_DIR");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        var exts = new[] { ".mp3", ".wav", ".flac", ".m4a", ".ogg", ".wma", ".aac" };
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(p => exts.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Take(200)
            .ToArray();

        var zones = new int[3, 3];
        var harms = new List<float>();
        var aggrs = new List<float>();
        foreach (var file in files)
        {
            var f = TrackAnalyzer.Analyze(file);
            if (f.IsFallback) continue;
            float harm = f.HPRatio;
            float aggr = Math.Clamp(
                0.55f * Math.Clamp(f.OnsetDensity / 5f, 0f, 1f) +
                0.25f * Math.Clamp(f.EnergyCv * 2f, 0f, 1f) +
                0.20f * (1f - f.Autocorrelation), 0f, 1f);
            int h = harm < 0.64f ? 0 : harm < 0.71f ? 1 : 2;
            int a = aggr < 0.26f ? 0 : aggr < 0.35f ? 1 : 2;
            zones[h, a]++;
            harms.Add(harm);
            aggrs.Add(aggr);
            var style = CoverArtGenerator.AutoSelectStyle(f, CoverArtGenerator.FeatureSeed(f), 0f);
            _out.WriteLine($"{Path.GetFileName(file)}: harm={harm:F2} aggr={aggr:F2} zone=({h},{a}) style={style}");
        }

        if (harms.Count == 0) return;
        harms.Sort();
        aggrs.Sort();
        for (int h = 0; h < 3; h++)
            _out.WriteLine($"zone row h={h}: {zones[h, 0]} {zones[h, 1]} {zones[h, 2]}");
        _out.WriteLine($"harm p33={harms[harms.Count / 3]:F2} p67={harms[harms.Count * 2 / 3]:F2}");
        _out.WriteLine($"aggr p33={aggrs[aggrs.Count / 3]:F2} p67={aggrs[aggrs.Count * 2 / 3]:F2}");
    }

    [Fact]
    public void Generate_AllStyles_NonUniform_RealAndFallback()
    {
        var path = CreateWav("styles.wav", 20, i =>
            0.4 * Math.Sin(2 * Math.PI * 440 * i / 44100.0) +
            0.15 * Math.Sin(2 * Math.PI * 2500 * i / 44100.0));
        var real = TrackAnalyzer.Analyze(path);
        Assert.False(real.IsFallback);

        foreach (var style in Enum.GetValues<CoverArtStyle>())
        foreach (var f in new[] { real, TrackFeatures.Default })
        {
            var bytes = CoverArtGenerator.Generate(style, 12345, f, 0f);
            using var ms = new MemoryStream(bytes);
            using var bmp = new Bitmap(ms);
            var colors = new HashSet<int>();
            for (int x = 20; x < 400; x += 40)
            for (int y = 20; y < 400; y += 40)
                colors.Add(bmp.GetPixel(x, y).ToArgb());
            Assert.True(colors.Count >= 4, $"{style} fallback={f.IsFallback} colors={colors.Count}");
        }
    }

    [Fact]
    public void Generate_DifferentSeedsProduceDifferentImages()
    {
        var path = CreateWav("seedvar.wav", 20, i => 0.4 * Math.Sin(2 * Math.PI * 440 * i / 44100.0));
        var f = TrackAnalyzer.Analyze(path);

        foreach (var style in Enum.GetValues<CoverArtStyle>())
        {
            var a = CoverArtGenerator.Generate(style, 1, f, 0f);
            var b = CoverArtGenerator.Generate(style, 2, f, 0f);
            Assert.False(a.SequenceEqual(b));
        }
    }

    [Fact]
    public void Generate_FinishSwitchIsDeterministicAndDistinct()
    {
        var path = CreateWav("finish.wav", 20, i => 0.4 * Math.Sin(2 * Math.PI * 440 * i / 44100.0));
        var f = TrackAnalyzer.Analyze(path);
        int seed = CoverArtGenerator.FeatureSeed(f);
        var style = CoverArtGenerator.AutoSelectStyle(f, seed, 0f);

        var none1 = CoverArtGenerator.Generate(style, seed, f, 0f, CoverFinish.None);
        var none2 = CoverArtGenerator.Generate(style, seed, f, 0f, CoverFinish.None);
        var retro1 = CoverArtGenerator.Generate(style, seed, f, 0f, CoverFinish.Retro);
        var retro2 = CoverArtGenerator.Generate(style, seed, f, 0f, CoverFinish.Retro);

        Assert.Equal(none1, none2);
        Assert.Equal(retro1, retro2);
        Assert.False(none1.SequenceEqual(retro1));
    }

    [Fact]
    public void RenderContactSheets_Diagnostic()
    {
        var srcDir = Environment.GetEnvironmentVariable("KEBUZ_SAMPLE_DIR");
        var outDir = Environment.GetEnvironmentVariable("KEBUZ_RENDER_DIR");
        if (string.IsNullOrEmpty(srcDir) || !Directory.Exists(srcDir) || string.IsNullOrEmpty(outDir)) return;
        Directory.CreateDirectory(outDir);

        var exts = new[] { ".mp3", ".wav", ".flac", ".m4a", ".ogg", ".wma", ".aac" };
        var tracks = Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories)
            .Where(p => exts.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Take(18)
            .Select(p => (Name: Path.GetFileNameWithoutExtension(p), F: TrackAnalyzer.Analyze(p)))
            .Where(t => !t.F.IsFallback)
            .ToArray();
        if (tracks.Length == 0) return;

        foreach (var style in Enum.GetValues<CoverArtStyle>())
        {
            SaveSheet(Path.Combine(outDir, $"sheet_{style}.png"), 6, tracks.Length, i =>
            {
                int seed = CoverArtGenerator.FeatureSeed(tracks[i].F);
                return CoverArtGenerator.Generate(style, seed, tracks[i].F, 0f);
            });

            SaveSheet(Path.Combine(outDir, $"variance_{style}.png"), 6, 12, i =>
            {
                int seed = CoverArtGenerator.FeatureSeed(tracks[0].F) + i;
                return CoverArtGenerator.Generate(style, seed, tracks[0].F, 0f);
            });
        }

        int cmpTracks = Math.Min(8, tracks.Length);
        SaveSheet(Path.Combine(outDir, "finish_compare.png"), 2, cmpTracks * 2, i =>
        {
            var f = tracks[i / 2].F;
            int seed = CoverArtGenerator.FeatureSeed(f);
            var style = CoverArtGenerator.AutoSelectStyle(f, seed, 0f);
            var finish = i % 2 == 0 ? CoverFinish.None : CoverFinish.Retro;
            return CoverArtGenerator.Generate(style, seed, f, 0f, finish);
        });

        _out.WriteLine($"sheets written to {outDir}, tracks={tracks.Length}");
    }

    private static void SaveSheet(string path, int cols, int count, Func<int, byte[]> render)
    {
        const int cell = 200, gap = 6;
        int rows = (count + cols - 1) / cols;
        using var sheet = new Bitmap(cols * (cell + gap) + gap, rows * (cell + gap) + gap);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(24, 24, 24));
        for (int i = 0; i < count; i++)
        {
            using var ms = new MemoryStream(render(i));
            using var img = Image.FromStream(ms);
            g.DrawImage(img, gap + i % cols * (cell + gap), gap + i / cols * (cell + gap), cell, cell);
        }
        sheet.Save(path, ImageFormat.Png);
    }

    [Fact]
    public void DefaultSeedFor_IsStableForSameMetadata()
    {
        var t1 = new Track { Id = 5, Title = "Песня", Artist = "Артист" };
        var t2 = new Track { Id = 5, Title = "Песня", Artist = "Артист" };
        var t3 = new Track { Id = 5, Title = "Песня", Artist = "Другой" };

        Assert.Equal(CoverArtGenerator.DefaultSeedFor(t1), CoverArtGenerator.DefaultSeedFor(t2));
        Assert.NotEqual(CoverArtGenerator.DefaultSeedFor(t1), CoverArtGenerator.DefaultSeedFor(t3));
    }
}
