namespace KebuzPlayer.Models;

public sealed record RadioStation(string Address, string Name, float GainDb = 0f)
{
    internal static float NormalizeGain(float gainDb) =>
        float.IsFinite(gainDb) ? Math.Clamp(gainDb, -20f, 20f) : 0f;

    public static Uri? ParseAddress(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.Host.Length > 0 && uri.UserInfo.Length == 0
            ? uri
            : null;
}
