using System.Globalization;

namespace KebuzPlayer.Infrastructure;

public static class CoreText
{
    public static Func<string, string?>? Lookup { get; set; }

    public static string Format(string key, string fallback, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Lookup?.Invoke(key) ?? fallback, args);
}
