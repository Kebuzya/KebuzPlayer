using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KebuzPlayer.Services;

public sealed record RadioCoverVariants(
    TrackFeatures Features, float Chaos, int BatchSize, int FirstVariant, string Title, string Artist);

public sealed class RadioCoverStore(string directory)
{
    private static string StationKey(string address) =>
        "station-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address)));

    public byte[]? Load(string address) => Read(StationKey(address) + ".jpg");

    public RadioCoverVariants? LoadVariants(string address)
    {
        try
        {
            var data = Read(StationKey(address) + ".json");
            if (data == null) return null;
            var variants = JsonSerializer.Deserialize<RadioCoverVariants>(data);
            return variants is { Features: not null, Title: not null, Artist: not null,
                                BatchSize: 1 or 4 or 9 or 16, FirstVariant: > 0 }
                && float.IsFinite(variants.Chaos) ? variants : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private byte[]? Read(string fileName)
    {
        try { return File.ReadAllBytes(Path.Combine(directory, fileName)); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void Save(string address, byte[] bytes) => Write(StationKey(address) + ".jpg", bytes);

    public void SaveVariants(string address, RadioCoverVariants variants) =>
        Write(StationKey(address) + ".json", JsonSerializer.SerializeToUtf8Bytes(variants));

    private void Write(string fileName, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public void KeepOnly(IEnumerable<string> addresses)
    {
        if (!Directory.Exists(directory)) return;

        var retained = addresses.Select(StationKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "station-*"))
        {
            if (Path.GetExtension(path) is not (".jpg" or ".json")) continue;
            var key = Path.GetFileNameWithoutExtension(path);
            if (key.Length == 72 && !retained.Contains(key) && key.AsSpan(8).IndexOfAnyExcept("0123456789ABCDEF") < 0)
                File.Delete(path);
        }
    }
}
