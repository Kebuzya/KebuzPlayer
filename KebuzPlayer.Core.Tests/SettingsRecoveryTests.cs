using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class SettingsRecoveryTests : IDisposable
{
    private readonly string _dir;

    public SettingsRecoveryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void CorruptFile_IsKeptAsideBeforeDefaultsOverwriteIt()
    {
        string file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, "{ \"Theme\": \"InkRust\", broken");

        var svc = new JsonSettingsService(file);
        svc.Save();

        var backups = Directory.GetFiles(_dir, "settings.json.bad-*");
        Assert.Single(backups);
        Assert.Equal("{ \"Theme\": \"InkRust\", broken", File.ReadAllText(backups[0]));
        Assert.Equal("MonoNoir", svc.Current.Theme);
    }

    [Fact]
    public void UnknownEnumNumber_FallsBackToDefaultInsteadOfBreakingSettings()
    {
        string file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, "{ \"Theme\": \"InkRust\", \"OverlayStyle\": 7, \"FileTransferMode\": 9 }");

        var svc = new JsonSettingsService(file);

        Assert.Equal("InkRust", svc.Current.Theme);
        Assert.Equal(OverlayStyle.Modest, svc.Current.OverlayStyle);
        Assert.Equal(FileTransferMode.Copy, svc.Current.FileTransferMode);
    }
}
