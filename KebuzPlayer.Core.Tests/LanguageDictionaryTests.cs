using System.Text.RegularExpressions;
using System.Xml.Linq;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class LanguageDictionaryTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string UiProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KebuzPlayer.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "KebuzPlayer");
    }

    private static Dictionary<string, string> Load(string code)
    {
        var doc = XDocument.Load(Path.Combine(UiProject(), "Lang", code + ".axaml"));
        var result = new Dictionary<string, string>();
        foreach (var e in doc.Root!.Elements(X + "String"))
        {
            var key = (string)e.Attribute(X + "Key")!;
            Assert.False(result.ContainsKey(key), $"{code}: duplicate key {key}");
            result[key] = e.Value;
        }
        return result;
    }

    private static string[] Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Distinct().Order().ToArray();

    [Fact]
    public void EnglishAndRussian_HaveTheSameKeysAndPlaceholders()
    {
        var ru = Load("ru");
        var en = Load("en");

        Assert.Empty(ru.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(ru.Keys));
        foreach (var (key, text) in ru)
        {
            Assert.False(string.IsNullOrWhiteSpace(en[key]), $"en: empty {key}");
            Assert.Equal(Placeholders(text), Placeholders(en[key]));
        }
    }

    [Fact]
    public void EveryKeyUsedInCode_ExistsInTheDictionary()
    {
        var ru = Load("ru");
        var prefixes = ru.Keys.Select(k => k[..k.IndexOf('.')]).ToHashSet();
        var keyLiteral = new Regex("\"([A-Z][A-Za-z]+(?:\\.[A-Za-z0-9]+)+)\"");
        var dynamicResource = new Regex(@"\{DynamicResource ([A-Za-z0-9.]+)\}");
        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(UiProject(), "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                continue;
            var text = File.ReadAllText(file);
            if (file.EndsWith(".cs"))
            {
                foreach (Match m in keyLiteral.Matches(text))
                {
                    var key = m.Groups[1].Value;
                    if (prefixes.Contains(key[..key.IndexOf('.')]) && !ru.ContainsKey(key))
                        missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
            else if (file.EndsWith(".axaml") && !file.Contains(Path.DirectorySeparatorChar + "Lang" + Path.DirectorySeparatorChar))
            {
                foreach (Match m in dynamicResource.Matches(text))
                {
                    var key = m.Groups[1].Value;
                    if (key.Contains('.') && !ru.ContainsKey(key))
                        missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }

        foreach (VisualizerStyle style in Enum.GetValues<VisualizerStyle>())
            if (!ru.ContainsKey("VizStyle." + style))
                missing.Add("VizStyle." + style);

        Assert.Empty(missing);
    }
}
