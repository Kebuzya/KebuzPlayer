namespace KebuzPlayer.Files;

public static class FileNameHelper
{
    public static string BuildFileName(string artist, string title, string extension)
    {
        var raw = string.IsNullOrEmpty(artist) ? title : $"{artist} - {title}";
        return SanitizeFileName(raw) + extension;
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
