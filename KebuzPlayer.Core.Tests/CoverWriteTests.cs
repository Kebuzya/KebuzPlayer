using KebuzPlayer.Data;
using KebuzPlayer.Models;
using KebuzPlayer.Services;
using KebuzPlayer.Tags;

namespace KebuzPlayer.Core.Tests;

public class CoverWriteTests : IDisposable
{
    private static readonly byte[] Art = [0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7, 0xFF, 0xD9];

    private readonly string _dir;
    private readonly Database _db;
    private readonly LibraryService _library;

    public CoverWriteTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "test.db"));
        _library = new LibraryService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private async Task<Track> AddTrackAsync(string name, byte[]? cover)
    {
        var path = Path.Combine(_dir, name);
        using (var w = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(8000, 16, 1)))
            w.Write(new byte[8000], 0, 8000);
        var track = new Track { Path = path, Title = name, DurationSeconds = 1, DateAdded = DateTime.UtcNow };
        track.Id = await _db.InsertOrUpdateTrackAsync(track);
        if (cover != null)
            await _db.UpdateCoverArtAsync(track.Id, cover);
        return track;
    }

    [Fact]
    public async Task WritesCoverIntoFileWithoutOne()
    {
        var track = await AddTrackAsync("plain.wav", Art);

        var result = await _library.WriteCoversToFilesAsync([track]);

        Assert.Equal(1, result.Written);
        Assert.Empty(result.Failed);
        Assert.Equal(Art, TagService.TryGetEmbeddedCoverBytes(track.Path));
    }

    [Fact]
    public async Task LeavesExistingEmbeddedCoverUntouched()
    {
        var track = await AddTrackAsync("embedded.wav", Art);
        byte[] foreign = [0x89, 0x50, 0x4E, 0x47, 1, 1, 1];
        using (var tf = TagLib.File.Create(track.Path))
        {
            tf.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(foreign)) { Type = TagLib.PictureType.FrontCover }];
            tf.Save();
        }

        var result = await _library.WriteCoversToFilesAsync([track]);

        Assert.Equal(0, result.Written);
        Assert.Equal(1, result.AlreadyEmbedded);
        Assert.Equal(foreign, TagService.TryGetEmbeddedCoverBytes(track.Path));
    }

    [Fact]
    public async Task ReplacesItsOwnOlderCoverAfterRegeneration()
    {
        var track = await AddTrackAsync("regenerated.wav", Art);
        await _library.WriteCoversToFilesAsync([track]);
        byte[] regenerated = [0xFF, 0xD8, 0xFF, 0xE0, 5, 5, 5, 0xFF, 0xD9];
        await _db.UpdateCoverArtAsync(track.Id, regenerated);

        var result = await _library.WriteCoversToFilesAsync([track]);

        Assert.Equal(1, result.Written);
        Assert.Equal(regenerated, TagService.TryGetEmbeddedCoverBytes(track.Path));
    }

    [Fact]
    public async Task RewritesItsOwnCoverToRefreshTheTag()
    {
        var track = await AddTrackAsync("own.wav", Art);
        TagService.WriteCover(track.Path, Art);

        var result = await _library.WriteCoversToFilesAsync([track]);

        Assert.Equal(1, result.Written);
        Assert.Equal(0, result.AlreadyEmbedded);
        Assert.Equal(Art, TagService.TryGetEmbeddedCoverBytes(track.Path));
    }

    [Fact]
    public async Task SkipsTracksWithoutCoverAndReportsFailures()
    {
        var bare    = await AddTrackAsync("bare.wav", null);
        var missing = await AddTrackAsync("missing.wav", Art);
        File.Delete(missing.Path);

        var result = await _library.WriteCoversToFilesAsync([bare, missing]);

        Assert.Equal(0, result.Written);
        Assert.Equal(1, result.NoCover);
        Assert.Single(result.Failed);
        Assert.StartsWith("missing.wav", result.Failed[0]);
        Assert.Null(TagService.TryGetEmbeddedCoverBytes(bare.Path));
    }
}
