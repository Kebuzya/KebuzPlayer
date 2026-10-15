using KebuzPlayer.Data;
using KebuzPlayer.Models;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class PlaylistServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly LibraryService _library;

    public PlaylistServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        _db = new Database(Path.Combine(_dir, "test.db"));
        _library = new LibraryService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private async Task<int> NewTrackAsync(string path, string title) =>
        await _db.InsertOrUpdateTrackAsync(new Track
        {
            Path = path,
            Title = title,
            DurationSeconds = 180,
            DateAdded = DateTime.UtcNow,
        });

    [Fact]
    public async Task CreatePlaylist_TrimsName()
    {
        await _library.CreatePlaylistAsync("  Мой лист  ");
        var lists = await _library.GetPlaylistsAsync();
        Assert.Equal("Мой лист", lists[0].Name);
    }

    [Fact]
    public async Task CreatePlaylist_EmptyNameThrows()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _library.CreatePlaylistAsync("   "));
    }

    [Fact]
    public async Task RenamePlaylist_EmptyNameThrows()
    {
        var pid = await _library.CreatePlaylistAsync("P");
        await Assert.ThrowsAsync<ArgumentException>(() => _library.RenamePlaylistAsync(pid, ""));
    }

    [Fact]
    public async Task AddTracksToPlaylist_ReturnsAddedCount_SkipsDuplicates()
    {
        var pid = await _library.CreatePlaylistAsync("P");
        var t1 = await NewTrackAsync(@"C:\m\1.mp3", "T1");
        var t2 = await NewTrackAsync(@"C:\m\2.mp3", "T2");
        var t3 = await NewTrackAsync(@"C:\m\3.mp3", "T3");
        await _library.AddTrackToPlaylistAsync(pid, t2);

        int added = await _library.AddTracksToPlaylistAsync(pid, new[] { t1, t2, t3 });

        Assert.Equal(2, added);
        var tracks = await _library.GetPlaylistTracksAsync(pid);
        Assert.Equal(3, tracks.Count);
    }

    [Fact]
    public async Task DeletePlaylist_ClearsMemberships()
    {
        var pid = await _library.CreatePlaylistAsync("P");
        var t1 = await NewTrackAsync(@"C:\m\1.mp3", "T1");
        await _library.AddTrackToPlaylistAsync(pid, t1);

        await _library.DeletePlaylistAsync(pid);

        Assert.Empty(await _library.GetPlaylistsAsync());
        Assert.Empty(await _library.GetPlaylistTracksAsync(pid));
    }
}
