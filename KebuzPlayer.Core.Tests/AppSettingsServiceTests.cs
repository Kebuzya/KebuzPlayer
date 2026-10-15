using KebuzPlayer.Data;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class AppSettingsServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly AppSettingsService _settings;

    public AppSettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        _db = new Database(Path.Combine(_dir, "test.db"));
        _settings = new AppSettingsService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public async Task CoverChaos_DefaultsToZero()
    {
        Assert.Equal(0f, await _settings.GetCoverChaosAsync());
    }

    [Fact]
    public async Task CoverChaos_RoundTripsAsFraction()
    {
        await _settings.SetCoverChaosPercentAsync(40);
        Assert.Equal(0.4f, await _settings.GetCoverChaosAsync(), 3);
    }

    [Fact]
    public async Task CoverChaos_ClampsOutOfRange()
    {
        await _settings.SetCoverChaosPercentAsync(150);
        Assert.Equal(1f, await _settings.GetCoverChaosAsync());

        await _settings.SetCoverChaosPercentAsync(-10);
        Assert.Equal(0f, await _settings.GetCoverChaosAsync());
    }

    [Fact]
    public async Task MasterVolume_RoundTripsWithInvariantCulture()
    {
        Assert.Null(await _settings.GetMasterVolumeAsync());

        await _settings.SetMasterVolumeAsync(0.85f);
        var loaded = await _settings.GetMasterVolumeAsync();
        Assert.NotNull(loaded);
        Assert.Equal(0.85f, loaded.Value, 3);
    }

    [Fact]
    public async Task RepeatMode_RoundTrips()
    {
        Assert.Null(await _settings.GetRepeatModeAsync());

        await _settings.SetRepeatModeAsync(RepeatMode.RepeatOne);
        Assert.Equal(RepeatMode.RepeatOne, await _settings.GetRepeatModeAsync());
    }

    [Fact]
    public async Task Shuffle_RoundTrips()
    {
        Assert.False(await _settings.GetShuffleAsync());

        await _settings.SetShuffleAsync(true);
        Assert.True(await _settings.GetShuffleAsync());
    }

    [Fact]
    public async Task LastTrackId_RoundTrips()
    {
        Assert.Null(await _settings.GetLastTrackIdAsync());

        await _settings.SetLastTrackIdAsync(42);
        Assert.Equal(42, await _settings.GetLastTrackIdAsync());
    }

    [Fact]
    public async Task VisualizerSettings_RoundTrip()
    {
        var original = new VisualizerSettings();
        original.Library.Enabled = false;
        original.Library.Style = VisualizerStyle.RadialWave;
        original.Library.Intensity = 0.33f;
        original.Library.ColorMode = VizColorMode.Rainbow;
        original.Library.ShowPeakRunners = false;
        original.Library.BarCount = 32;

        await _settings.SaveVisualizerSettingsAsync(original);

        var loaded = new VisualizerSettings();
        await _settings.LoadVisualizerSettingsAsync(loaded);

        Assert.False(loaded.Library.Enabled);
        Assert.Equal(VisualizerStyle.RadialWave, loaded.Library.Style);
        Assert.Equal(0.33f, loaded.Library.Intensity, 3);
        Assert.Equal(VizColorMode.Rainbow, loaded.Library.ColorMode);
        Assert.False(loaded.Library.ShowPeakRunners);
        Assert.Equal(32, loaded.Library.BarCount);
        Assert.True(loaded.Tab.Enabled);
    }

    [Fact]
    public async Task VisualizerSettings_FrameRateRoundTrips()
    {
        var original = new VisualizerSettings { FrameRate = 15 };
        await _settings.SaveVisualizerSettingsAsync(original);

        var loaded = new VisualizerSettings();
        await _settings.LoadVisualizerSettingsAsync(loaded);

        Assert.Equal(15, loaded.FrameRate);
    }

    [Fact]
    public async Task VisualizerSettings_FrameRateZeroMeansMonitorRate()
    {
        var original = new VisualizerSettings { FrameRate = 0 };
        await _settings.SaveVisualizerSettingsAsync(original);

        var loaded = new VisualizerSettings();
        await _settings.LoadVisualizerSettingsAsync(loaded);

        Assert.Equal(0, loaded.FrameRate);
    }

    [Fact]
    public async Task VisualizerSettings_LoadWithNoSavedData_KeepsDefaults()
    {
        var s = new VisualizerSettings();
        await _settings.LoadVisualizerSettingsAsync(s);

        Assert.True(s.Library.Enabled);
        Assert.Equal(VisualizerStyle.Bars, s.Library.Style);
        Assert.Equal(0.75f, s.Library.Intensity, 3);
        Assert.Equal(64, s.Library.BarCount);
        Assert.Equal(30, s.FrameRate);
    }

    [Fact]
    public async Task VisualizerSettings_OverlayEnabledSeed_UsedWhenKeyAbsent()
    {
        var s = new VisualizerSettings();
        await _settings.LoadVisualizerSettingsAsync(s, overlayEnabledDefault: false);

        Assert.False(s.Overlay.Enabled);
        Assert.True(s.Library.Enabled);
        Assert.True(s.Tab.Enabled);
    }

    [Fact]
    public async Task VisualizerSettings_OverlayEnabledSeed_IgnoredWhenKeyPresent()
    {
        var saved = new VisualizerSettings();
        saved.Overlay.Enabled = true;
        await _settings.SaveVisualizerSettingsAsync(saved);

        var s = new VisualizerSettings();
        await _settings.LoadVisualizerSettingsAsync(s, overlayEnabledDefault: false);

        Assert.True(s.Overlay.Enabled);
    }
}
