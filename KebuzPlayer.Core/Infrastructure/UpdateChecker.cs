using System.Net.Http.Headers;
using System.Text.Json;

namespace KebuzPlayer.Infrastructure;

public sealed record UpdateInfo(Version Version, string Url);

public static class UpdateChecker
{
    public const string Repository   = "Kebuzya/KebuzPlayer";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";

    private const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
    private const string TrustedUrlPrefix = "https://github.com/" + Repository + "/";

    public static readonly TimeSpan CheckInterval  = TimeSpan.FromDays(1);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public static async Task<UpdateInfo?> FetchLatestAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = RequestTimeout };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KebuzPlayer", "1.0"));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.GetAsync(LatestReleaseApi, ct);
        if (!response.IsSuccessStatusCode) return null;
        return ParseRelease(await response.Content.ReadAsStringAsync(ct));
    }

    public static UpdateInfo? ParseRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) return null;
            if (ParseVersion(tag.GetString()) is not { } version) return null;

            string url = root.TryGetProperty("html_url", out var html)
                         && html.ValueKind == JsonValueKind.String
                         && html.GetString() is { } link
                         && link.StartsWith(TrustedUrlPrefix, StringComparison.OrdinalIgnoreCase)
                ? link
                : ReleasesPage;
            return new UpdateInfo(version, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) s = s[..cut];
        if (!s.Contains('.')) s += ".0";
        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    public static Version Normalize(Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    public static bool IsNewer(Version candidate, Version current) =>
        Normalize(candidate) > Normalize(current);

    public static bool ShouldCheck(DateTime? lastCheckUtc, DateTime nowUtc) =>
        lastCheckUtc is not { } last || last > nowUtc || nowUtc - last >= CheckInterval;

    public static bool IsAvailable(string? available, Version current) =>
        ParseVersion(available) is { } v && IsNewer(v, current);

    public static bool ShouldShowBanner(string? available, string? dismissed, Version current)
    {
        if (ParseVersion(available) is not { } v || !IsNewer(v, current)) return false;
        return ParseVersion(dismissed) is not { } d || IsNewer(v, d);
    }
}
