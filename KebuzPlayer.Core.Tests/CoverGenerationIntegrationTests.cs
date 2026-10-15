using KebuzPlayer.Data;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class CoverGenerationIntegrationTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly LibraryService _library;

    public CoverGenerationIntegrationTests()
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

    private string CreateWav(string name, double freq, int seconds = 12)
    {
        const int rate = 44100;
        int total = rate * seconds;
        var path = Path.Combine(_dir, "music", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);
        int dataSize = total * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataSize);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataSize);
        for (int i = 0; i < total; i++)
            w.Write((short)(Math.Sin(2 * Math.PI * freq * i / rate) * 0.4 * short.MaxValue));
        return path;
    }

    [Fact]
    public async Task GenerateMissingCovers_FillsAllBlobs_Deterministically()
    {
        CreateWav("a.wav", 220);
        CreateWav("b.wav", 880);
        await _library.ScanFolderAsync(Path.Combine(_dir, "music"));

        var tracks = await _db.GetAllTracksAsync();
        Assert.Equal(2, tracks.Count);
        Assert.Equal(2, (await _db.GetTrackIdsWithoutCoverAsync()).Count);

        int generated = await _library.GenerateMissingCoversAsync();
        Assert.Equal(2, generated);
        Assert.Empty(await _db.GetTrackIdsWithoutCoverAsync());

        var cover1 = await _db.GetCoverArtAsync(tracks[0].Id);
        Assert.NotNull(cover1);
        Assert.True(cover1.Length > 1000);

        await _library.GenerateCoverArtAsync(tracks[0]);
        var cover1Again = await _db.GetCoverArtAsync(tracks[0].Id);
        Assert.Equal(cover1, cover1Again);
    }

    [Fact]
    public async Task GenerateCoverArt_UnreadableFile_FallsBackToMetaCover()
    {
        var path = Path.Combine(_dir, "music", "broken.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

        var id = await _db.InsertOrUpdateTrackAsync(new KebuzPlayer.Models.Track
        {
            Path = path,
            Title = "Broken",
            Artist = "X",
            DateAdded = DateTime.UtcNow,
        });
        var track = await _db.GetTrackByIdAsync(id);

        await _library.GenerateCoverArtAsync(track!);
        var cover = await _db.GetCoverArtAsync(id);
        Assert.NotNull(cover);
        Assert.True(cover.Length > 500);
    }
}
