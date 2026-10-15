using KebuzPlayer.Infrastructure;
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
