namespace KebuzPlayer.Infrastructure;

public static class PathProvider
{
    public const string PortableMarkerName = "kebuz.portable";

    private static string? _portableRoot;

    public static bool IsPortable => _portableRoot != null;

    public static void DetectPortableMode(string exeDirectory)
    {
        _portableRoot = File.Exists(Path.Combine(exeDirectory, PortableMarkerName))
            ? Path.Combine(exeDirectory, "Data")
            : null;
    }

    public static string RoamingDir => _portableRoot != null
        ? Path.Combine(_portableRoot, "Roaming")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kebuz", "KebuzPlayer");

    public static string LocalDir => _portableRoot != null
        ? Path.Combine(_portableRoot, "Local")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kebuz", "KebuzPlayer");

    public static string SettingsFilePath => Path.Combine(RoamingDir, "settings.json");

    public static string DatabasePath => Path.Combine(LocalDir, "library.db");
}
