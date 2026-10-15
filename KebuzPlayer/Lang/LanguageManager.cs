using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KebuzPlayer.Infrastructure;

namespace KebuzPlayer.Lang;

public sealed record LanguageInfo(string Code, string DisplayName);

public static class LanguageManager
{
    public static readonly LanguageInfo[] Languages =
    [
        new("ru", "Русский"),
        new("en", "English"),
    ];

    public static string Current { get; private set; } = "";

    public static event Action? Changed;

    private static ResourceDictionary? _current;

    public static string Detect() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";

    public static string Apply(string? code)
    {
        var lang = Languages.FirstOrDefault(l => l.Code == code)
                   ?? Languages.First(l => l.Code == Detect());
        if (lang.Code == Current) return Current;

        var dict = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"avares://KebuzPlayer/Lang/{lang.Code}.axaml"));
        var app = Application.Current!;
        if (_current != null)
            app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Add(dict);
        _current = dict;

        bool first = Current.Length == 0;
        Current = lang.Code;
        CoreText.Lookup = Loc.Find;
        if (!first) Changed?.Invoke();
        return Current;
    }
}

public static class Loc
{
    public static string? Find(string key) =>
        Application.Current is { } app && app.TryGetResource(key, null, out var value) && value is string s
            ? s
            : null;

    public static string T(string key) => Find(key) ?? key;

    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    public static string Tracks(int n)
    {
        string word = LanguageManager.Current == "ru"
            ? RuPlural.Form(n, T("Plural.Track.One"), T("Plural.Track.Few"), T("Plural.Track.Many"))
            : n == 1 ? T("Plural.Track.One") : T("Plural.Track.Many");
        return $"{n} {word}";
    }
}
