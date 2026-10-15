using KebuzPlayer.Services;
using Microsoft.Win32;

namespace KebuzPlayer.Infrastructure;

public static class DefaultAppRegistration
{
    private const string AppKey = "KebuzPlayer";
    private const string ProgId = "KebuzPlayer.AudioFile";
    private const string CapabilitiesPath = @"Software\Clients\Media\KebuzPlayer\Capabilities";
    private const string ClientMediaPath = @"Software\Clients\Media\KebuzPlayer";
    private const string ClassesProgIdPath = @"Software\Classes\" + ProgId;
    private const string RegisteredAppsPath = @"Software\RegisteredApplications";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegisteredAppsPath);
        return key?.GetValue(AppKey) as string == CapabilitiesPath;
    }

    public static bool IsRegisteredTo(string exePath)
    {
        if (!IsRegistered()) return false;
        using var key = Registry.CurrentUser.OpenSubKey(ClientMediaPath + @"\shell\open\command");
        return string.Equals(key?.GetValue("") as string, $"\"{exePath}\"", StringComparison.OrdinalIgnoreCase);
    }

    public static void Register(string exePath)
    {
        using (var caps = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
        {
            caps.SetValue("ApplicationName", "KebuzPlayer");
            caps.SetValue("ApplicationDescription", CoreText.Format("App.Description", "Локальный музыкальный плеер"));
            caps.SetValue("ApplicationIcon", $"{exePath},0");

            using var assoc = caps.CreateSubKey("FileAssociations");
            foreach (var ext in LibraryService.SupportedExtensions)
                assoc.SetValue(ext, ProgId);
        }

        using (var open = Registry.CurrentUser.CreateSubKey(ClientMediaPath + @"\shell\open\command"))
            open.SetValue("", $"\"{exePath}\"");

        using (var progOpen = Registry.CurrentUser.CreateSubKey(ClassesProgIdPath + @"\shell\open\command"))
            progOpen.SetValue("", $"\"{exePath}\" \"%1\"");

        using (var progIcon = Registry.CurrentUser.CreateSubKey(ClassesProgIdPath + @"\DefaultIcon"))
            progIcon.SetValue("", $"{exePath},0");

        using (var registered = Registry.CurrentUser.CreateSubKey(RegisteredAppsPath))
            registered.SetValue(AppKey, CapabilitiesPath);
    }

    public static void Unregister()
    {
        using (var registered = Registry.CurrentUser.OpenSubKey(RegisteredAppsPath, writable: true))
            registered?.DeleteValue(AppKey, throwOnMissingValue: false);

        Registry.CurrentUser.DeleteSubKeyTree(ClientMediaPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(ClassesProgIdPath, throwOnMissingSubKey: false);
    }
}
