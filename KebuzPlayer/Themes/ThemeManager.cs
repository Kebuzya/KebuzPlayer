using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace KebuzPlayer.Themes;

public sealed record ThemeInfo(string Id, string DisplayName, bool IsLight);

public static class ThemeManager
{
    public static readonly ThemeInfo[] Themes =
    [
        new("MonoNoir", "Mono Noir", false),
        new("GraphitePhosphor", "Graphite Phosphor", false),
        new("InkRust", "Ink Rust", false),
        new("SlateScope", "Slate Scope", false),
        new("TerminalGreen", "Terminal Green", false),
        new("IvoryMoss", "Ivory Moss", true),
        new("VellumBlue", "Vellum Blue", true),
    ];

    public static string CurrentThemeId { get; private set; } = "";

    private static ResourceDictionary? _current;

    public static void Apply(string themeId)
    {
        var theme = Themes.FirstOrDefault(t => t.Id == themeId) ?? Themes[0];
        if (theme.Id == CurrentThemeId) return;

        var uri = new Uri($"avares://KebuzPlayer/Themes/{theme.Id}.axaml");
        var dict = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);

        var app = Application.Current!;
        if (_current != null)
            app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Add(dict);
        _current = dict;

        app.RequestedThemeVariant = theme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
        CurrentThemeId = theme.Id;
    }
}
