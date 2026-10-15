using KebuzPlayer.Infrastructure;
using Microsoft.Win32;

namespace KebuzPlayer.Core.Tests;

public class DefaultAppRegistrationTests : IDisposable
{
    private const string RegisteredAppsPath = @"Software\RegisteredApplications";
    private const string CapabilitiesPath = @"Software\Clients\Media\KebuzPlayer\Capabilities";
    private const string ProgIdPath = @"Software\Classes\KebuzPlayer.AudioFile";

    public void Dispose()
    {
        try { DefaultAppRegistration.Unregister(); } catch { }
    }

    [Fact]
    public void Register_CreatesExpectedRegistryEntries()
    {
        DefaultAppRegistration.Register(@"C:\fake\KebuzPlayer.exe");

        Assert.True(DefaultAppRegistration.IsRegistered());

        using var registered = Registry.CurrentUser.OpenSubKey(RegisteredAppsPath);
        Assert.Equal(CapabilitiesPath, registered?.GetValue("KebuzPlayer") as string);

        using var caps = Registry.CurrentUser.OpenSubKey(CapabilitiesPath);
        Assert.NotNull(caps);
        Assert.Equal("KebuzPlayer", caps!.GetValue("ApplicationName"));

        using var assoc = Registry.CurrentUser.OpenSubKey(CapabilitiesPath + @"\FileAssociations");
        Assert.NotNull(assoc);
        Assert.Equal("KebuzPlayer.AudioFile", assoc!.GetValue(".mp3"));

        using var progOpen = Registry.CurrentUser.OpenSubKey(ProgIdPath + @"\shell\open\command");
        Assert.Equal("\"C:\\fake\\KebuzPlayer.exe\" \"%1\"", progOpen?.GetValue(""));
    }

    [Fact]
    public void Unregister_RemovesEntries()
    {
        DefaultAppRegistration.Register(@"C:\fake\KebuzPlayer.exe");
        DefaultAppRegistration.Unregister();

        Assert.False(DefaultAppRegistration.IsRegistered());
        Assert.Null(Registry.CurrentUser.OpenSubKey(CapabilitiesPath));
        Assert.Null(Registry.CurrentUser.OpenSubKey(ProgIdPath));
    }

    [Fact]
    public void IsRegisteredTo_MatchesOnlyTheRegisteredExecutable()
    {
        DefaultAppRegistration.Register(@"C:\fake\KebuzPlayer.exe");

        Assert.True(DefaultAppRegistration.IsRegisteredTo(@"c:\FAKE\KebuzPlayer.exe"));
        Assert.False(DefaultAppRegistration.IsRegisteredTo(@"D:\Portable\KebuzPlayer.exe"));

        DefaultAppRegistration.Unregister();
        Assert.False(DefaultAppRegistration.IsRegisteredTo(@"C:\fake\KebuzPlayer.exe"));
    }

    [Fact]
    public void IsRegistered_FalseWhenNeverRegistered()
    {
        DefaultAppRegistration.Unregister();
        Assert.False(DefaultAppRegistration.IsRegistered());
    }
}
