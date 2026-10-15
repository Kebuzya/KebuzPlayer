using KebuzPlayer.Tags;

namespace KebuzPlayer.Core.Tests;

public class TagServiceTests : IDisposable
{
    private readonly string _dir;

    public TagServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string CreateWav(string name = "sample.wav", int seconds = 2)
    {
        const short channels = 1;
        const int sampleRate = 8000;
        const short bits = 16;
        int dataSize = sampleRate * seconds * channels * bits / 8;

        var path = Path.Combine(_dir, name);
        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataSize);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * bits / 8);
        w.Write((short)(channels * bits / 8));
        w.Write(bits);
        w.Write("data"u8.ToArray());
        w.Write(dataSize);
        w.Write(new byte[dataSize]);
        return path;
    }

    [Fact]
    public void ReadTags_UntaggedFile_FallsBackToFileName()
    {
        var path = CreateWav("My Song.wav");
        var track = TagService.ReadTags(path);

        Assert.Equal(path, track.Path);
        Assert.Equal("My Song", track.Title);
        Assert.Equal("", track.Artist);
        Assert.Equal("", track.Album);
    }

    [Fact]
    public void ReadTags_ReadsDuration()
    {
        var path = CreateWav(seconds: 3);
        var track = TagService.ReadTags(path);

        Assert.InRange(track.DurationSeconds, 2, 4);
    }

    [Fact]
    public void ReadTags_MissingFile_ReturnsFallbackTrack()
    {
        var path = Path.Combine(_dir, "nope.mp3");
        var track = TagService.ReadTags(path);

        Assert.Equal(path, track.Path);
        Assert.Equal("nope", track.Title);
        Assert.Equal("", track.Artist);
        Assert.Equal(0, track.DurationSeconds);
    }

    [Fact]
    public void WriteMetadata_ThenReadTags_RoundTrips()
    {
        var path = CreateWav();
        TagService.WriteMetadata(path, "Заголовок", "Исполнитель", "Альбом");
        var track = TagService.ReadTags(path);

        Assert.Equal("Заголовок", track.Title);
        Assert.Equal("Исполнитель", track.Artist);
        Assert.Equal("Альбом", track.Album);
    }

    [Fact]
    public void WriteMetadata_TrimsValues()
    {
        var path = CreateWav();
        TagService.WriteMetadata(path, "  Title  ", "  Artist  ", "  Album  ");
        var track = TagService.ReadTags(path);

        Assert.Equal("Title", track.Title);
        Assert.Equal("Artist", track.Artist);
        Assert.Equal("Album", track.Album);
    }

    [Fact]
    public void WriteMetadata_EmptyArtist_ClearsPerformers()
    {
        var path = CreateWav("cleared.wav");
        TagService.WriteMetadata(path, "T", "A", "Al");
        TagService.WriteMetadata(path, "T", "   ", "Al");
        var track = TagService.ReadTags(path);

        Assert.Equal("", track.Artist);
    }

    [Fact]
    public void WriteMetadata_MissingFile_Reports()
    {
        Assert.ThrowsAny<Exception>(() =>
            TagService.WriteMetadata(Path.Combine(_dir, "ghost.mp3"), "T", "A", "Al"));
    }

    [Fact]
    public void TryGetEmbeddedCoverBytes_NoCover_ReturnsNull()
    {
        var path = CreateWav();
        Assert.Null(TagService.TryGetEmbeddedCoverBytes(path));
    }

    [Fact]
    public void TryGetEmbeddedCoverBytes_MissingFile_ReturnsNull()
    {
        Assert.Null(TagService.TryGetEmbeddedCoverBytes(Path.Combine(_dir, "ghost.mp3")));
    }

    [Fact]
    public void ReadTags_GarbageFile_ReturnsFallbackTrack()
    {
        var path = Path.Combine(_dir, "garbage.mp3");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
        var track = TagService.ReadTags(path);

        Assert.Equal("garbage", track.Title);
    }
}
