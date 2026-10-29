using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;

namespace KebuzPlayer.Tags;

public static class TagService
{
    static TagService()
    {
        TagLib.Id3v2.Tag.DefaultVersion      = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;
    }

    public static Track ReadTags(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var replayGain = ReplayGain(tagFile.Tag);
            return new Track
            {
                Path            = filePath,
                Title           = NonEmpty(tagFile.Tag.Title)          ?? Path.GetFileNameWithoutExtension(filePath),
                Artist          = NonEmpty(tagFile.Tag.FirstPerformer) ?? "",
                Album           = NonEmpty(tagFile.Tag.Album)          ?? "",
                DurationSeconds = (int)tagFile.Properties.Duration.TotalSeconds,
                DateAdded       = DateTime.UtcNow,
                TagsRead        = true,
                ReplayGainTarget = replayGain,
                UserGainOffset  = replayGain is { } gain ? Math.Clamp(MathF.Round(gain * 10f) / 10f, -20f, 20f) : 0f,
            };
        }
        catch
        {
            return new Track
            {
                Path      = filePath,
                Title     = Path.GetFileNameWithoutExtension(filePath),
                DateAdded = DateTime.UtcNow,
            };
        }
    }

    public const string OwnCoverDescription = "KebuzPlayer";

    public static byte[]? TryGetEmbeddedCoverBytes(string filePath, bool skipOwn = false)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var pic = tagFile.Tag.Pictures.FirstOrDefault(p => !skipOwn || p.Description != OwnCoverDescription);
            return pic?.Data?.Data is { Length: > 0 } bytes ? bytes : null;
        }
        catch { return null; }
    }

    public static bool HasForeignCover(string filePath, byte[]? ownCover)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            return tagFile.Tag.Pictures.Any(p =>
                p.Description != OwnCoverDescription &&
                p.Data?.Data is { Length: > 0 } bytes &&
                (ownCover == null || !bytes.AsSpan().SequenceEqual(ownCover)));
        }
        catch { return false; }
    }

    public static void WriteCover(string filePath, byte[] art)
    {
        using (var tagFile = TagLib.File.Create(filePath))
        {
            tagFile.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(art)) { Type = TagLib.PictureType.FrontCover, Description = OwnCoverDescription }];
            tagFile.Save();
        }
        if (TryGetEmbeddedCoverBytes(filePath) == null)
            throw new NotSupportedException(CoreText.Format("Error.CoverNotStored", "формат файла не хранит обложку"));
    }

    public static void WriteMetadata(string filePath, string title, string artist, string album)
    {
        using var tagFile = TagLib.File.Create(filePath);
        tagFile.Tag.Title      = NonEmpty(title);
        tagFile.Tag.Performers = string.IsNullOrWhiteSpace(artist)
            ? Array.Empty<string>() : [artist.Trim()];
        tagFile.Tag.Album      = NonEmpty(album);
        tagFile.Save();
    }

    private static float? ReplayGain(TagLib.Tag tag)
    {
        double gain = tag.ReplayGainTrackGain;
        if (double.IsNaN(gain)) gain = tag.ReplayGainAlbumGain;
        return double.IsNaN(gain) || double.IsInfinity(gain) ? null : (float)gain;
    }

    private static string? NonEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
