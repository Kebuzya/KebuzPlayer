using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class JsonSettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public JsonSettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string SettingsPath => Path.Combine(_dir, "sub", "settings.json");

    [Fact]
    public void MissingFile_YieldsDefaults()
    {
        var svc = new JsonSettingsService(SettingsPath);

        Assert.Equal("MonoNoir", svc.Current.Theme);
        Assert.Equal(0.8f, svc.Current.MasterVolume);
        Assert.False(svc.Current.IsShuffleOn);
        Assert.Equal(RepeatMode.None, svc.Current.RepeatMode);
        Assert.Null(svc.Current.LastTrackId);
        Assert.Empty(svc.Current.RecentRadioStations);
    }

    [Fact]
    public void SaveAndReload_RoundTrips()
    {
        var svc = new JsonSettingsService(SettingsPath);
        svc.Current.Theme = "TerminalGreen";
        svc.Current.MasterVolume = 0.35f;
        svc.Current.IsShuffleOn = true;
        svc.Current.RepeatMode = RepeatMode.RepeatOne;
        svc.Current.LastTrackId = 17;
        svc.Current.WindowWidth = 999;
        svc.Save();

        var reloaded = new JsonSettingsService(SettingsPath);
        Assert.Equal("TerminalGreen", reloaded.Current.Theme);
        Assert.Equal(0.35f, reloaded.Current.MasterVolume, 3);
        Assert.True(reloaded.Current.IsShuffleOn);
        Assert.Equal(RepeatMode.RepeatOne, reloaded.Current.RepeatMode);
        Assert.Equal(17, reloaded.Current.LastTrackId);
        Assert.Equal(999, reloaded.Current.WindowWidth);
    }

    [Fact]
    public void ViewModes_RoundTrip()
    {
        var svc = new JsonSettingsService(SettingsPath);
        svc.Current.LibraryViewMode = TrackViewMode.Grid;
        svc.Current.PlaylistViewModes[3] = TrackViewMode.Compact;
        svc.Current.PlaylistViewModes[7] = TrackViewMode.Grid;
        svc.Save();

        var reloaded = new JsonSettingsService(SettingsPath);
        Assert.Equal(TrackViewMode.Grid, reloaded.Current.LibraryViewMode);
        Assert.Equal(TrackViewMode.Compact, reloaded.Current.PlaylistViewModes[3]);
        Assert.Equal(TrackViewMode.Grid, reloaded.Current.PlaylistViewModes[7]);
    }

    [Fact]
    public void RecentRadioStations_ReplayMovesToFrontAndRemovalSurvivesRestart()
    {
        var svc = new JsonSettingsService(SettingsPath);
        var first = new RadioStation("https://example.test/first", "First");
        var second = new RadioStation("https://example.test/second", "Second");
        svc.RememberRadioStation(first);
        svc.RememberRadioStation(second);
        svc.RememberRadioStation(first);

        var reloaded = new JsonSettingsService(SettingsPath);
        Assert.Equal([first, second], reloaded.Current.RecentRadioStations);

        reloaded.RemoveRadioStation(first.Address);

        Assert.Equal(second, Assert.Single(new JsonSettingsService(SettingsPath).Current.RecentRadioStations));
    }

    [Fact]
    public void RecentRadioStations_LoadKeepsValidUniqueAddressesWithinLimit()
    {
        var svc = new JsonSettingsService(SettingsPath);
        svc.Current.RecentRadioStations =
        [
            new("https://EXAMPLE.test/radio#now", ""),
            new("https://example.test/radio", "Duplicate"),
            new("file:///local.mp3", "Invalid"),
            .. Enumerable.Range(1, 22).Select(i => new RadioStation($"https://example.test/radio/{i}", $"Station {i}")),
        ];
        svc.Save();

        var recent = new JsonSettingsService(SettingsPath).Current.RecentRadioStations;
        Assert.Equal(20, recent.Count);
        Assert.Equal(new RadioStation("https://example.test/radio", "example.test"), recent[0]);
        Assert.Equal("Station 19", recent[^1].Name);
    }

    [Fact]
    public void RecentRadioStations_ExplicitNullLoadsAsEmptyList()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{"RecentRadioStations": null}""");

        Assert.Empty(new JsonSettingsService(SettingsPath).Current.RecentRadioStations);
    }

    [Fact]
    public void RadioStationGain_SavesIndependentlyAndKeepsHistoryOrder()
    {
        var svc = new JsonSettingsService(SettingsPath);
        var first = new RadioStation("https://example.test/first", "First");
        var second = new RadioStation("https://example.test/second", "Second");
        svc.RememberRadioStation(first);
        svc.RememberRadioStation(second);

        svc.SetRadioStationGain(first.Address, -5.5f);
        svc.SetRadioStationGain(second.Address, 40f);
        svc.SetRadioStationGain("https://example.test/unknown", 5f);

        var saved = new JsonSettingsService(SettingsPath).Current.RecentRadioStations;
        Assert.Equal([second with { GainDb = 20f }, first with { GainDb = -5.5f }], saved);
    }

    [Fact]
    public void RadioStationGain_OlderHistoryLoadsAtZeroDb()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            {"RecentRadioStations": [{"Address": "https://example.test/radio", "Name": "Station"}]}
            """);

        var station = Assert.Single(new JsonSettingsService(SettingsPath).Current.RecentRadioStations);
        Assert.Equal(0f, station.GainDb);
    }

    [Fact]
    public void RemoveRadioStation_WhenSaveFailsKeepsStationInMemoryAndOnDisk()
    {
        var svc = new JsonSettingsService(SettingsPath);
        var station = new RadioStation("https://example.test/stream", "Station");
        svc.RememberRadioStation(station);
        Directory.CreateDirectory(SettingsPath + ".tmp");

        var error = Record.Exception(() => svc.RemoveRadioStation(station.Address));

        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(station, Assert.Single(svc.Current.RecentRadioStations));
        Assert.Equal(station, Assert.Single(new JsonSettingsService(SettingsPath).Current.RecentRadioStations));
    }

    [Fact]
    public void MissingViewModeFields_UseDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{\"Theme\":\"MonoNoir\"}");

        var svc = new JsonSettingsService(SettingsPath);
        Assert.Equal(TrackViewMode.Table, svc.Current.LibraryViewMode);
        Assert.NotNull(svc.Current.PlaylistViewModes);
        Assert.Empty(svc.Current.PlaylistViewModes);
    }

    [Fact]
    public void CorruptedFile_YieldsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{not valid json!!");

        var svc = new JsonSettingsService(SettingsPath);
        Assert.Equal("MonoNoir", svc.Current.Theme);
    }

    [Fact]
    public void UnknownEnumValue_YieldsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{\"RepeatMode\":\"Bogus\"}");

        var svc = new JsonSettingsService(SettingsPath);
        Assert.Equal(RepeatMode.None, svc.Current.RepeatMode);
    }

    [Fact]
    public void PathProvider_DefaultPaths_PointToKebuzFolders()
    {
        PathProvider.DetectPortableMode(_dir);
        Assert.False(PathProvider.IsPortable);
        Assert.Contains(Path.Combine("Kebuz", "KebuzPlayer"), PathProvider.RoamingDir);
        Assert.Contains(Path.Combine("Kebuz", "KebuzPlayer"), PathProvider.LocalDir);
        Assert.EndsWith("settings.json", PathProvider.SettingsFilePath);
        Assert.EndsWith("library.db", PathProvider.DatabasePath);
    }

    [Fact]
    public void PathProvider_PortableMarker_RedirectsBothPaths()
    {
        File.WriteAllText(Path.Combine(_dir, PathProvider.PortableMarkerName), "");
        PathProvider.DetectPortableMode(_dir);

        try
        {
            Assert.True(PathProvider.IsPortable);
            Assert.StartsWith(Path.Combine(_dir, "Data"), PathProvider.RoamingDir);
            Assert.StartsWith(Path.Combine(_dir, "Data"), PathProvider.LocalDir);
            Assert.NotEqual(PathProvider.RoamingDir, PathProvider.LocalDir);
        }
        finally
        {
            PathProvider.DetectPortableMode(Path.GetTempPath());
        }
    }
}
