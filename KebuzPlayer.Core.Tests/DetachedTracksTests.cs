using KebuzPlayer.Data;
using KebuzPlayer.Models;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class DetachedTracksTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly LibraryService _library;

    public DetachedTracksTests()
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

    private async Task<int> AddTrackAsync(string path, int playCount = 7)
    {
        int id = await _db.InsertOrUpdateTrackAsync(new Track
        {
            Path = path,
            Title = Path.GetFileNameWithoutExtension(path),
            Artist = "Artist",
            Album = "Album",
            DurationSeconds = 120,
            PlayCount = playCount,
            DateAdded = DateTime.UtcNow,
        });
        await _db.UpdateCoverArtAsync(id, [1, 2, 3, 4]);
        return id;
    }

    [Fact]
    public async Task RemovedFolder_HidesTracksButKeepsTheirData()
    {
        string folder = Path.Combine(_dir, "music");
        int id = await AddTrackAsync(Path.Combine(folder, "song.mp3"));
        await _db.AddWatchedFolderAsync(folder);

        int touched = await _library.RemoveWatchedFolderAsync(folder);

        Assert.Equal(1, touched);
        Assert.Empty(await _db.GetAllTracksAsync());
        var kept = await _db.GetTrackByIdAsync(id);
        Assert.NotNull(kept);
        Assert.Equal(7, kept!.PlayCount);
        Assert.NotNull(await _db.GetCoverArtAsync(id));
    }

    [Fact]
    public async Task ReaddedFolder_RevivesTheSameRowWithItsStats()
    {
        string folder = Path.Combine(_dir, "music");
        string file = Path.Combine(folder, "song.mp3");
        int id = await AddTrackAsync(file);
        await _library.RemoveWatchedFolderAsync(folder);

        int revived = await _db.InsertOrUpdateTrackAsync(new Track
        {
            Path = file,
            Title = "song",
            DurationSeconds = 120,
            DateAdded = DateTime.UtcNow,
        });

        Assert.Equal(id, revived);
        var tracks = await _db.GetAllTracksAsync();
        Assert.Single(tracks);
        Assert.Equal(7, tracks[0].PlayCount);
        Assert.NotNull(await _db.GetCoverArtAsync(id));
    }

    [Fact]
    public async Task DetachedTracks_StayOutOfEveryListing()
    {
        string folder = Path.Combine(_dir, "music");
        int id = await AddTrackAsync(Path.Combine(folder, "song.mp3"));
        await _db.AddPlayHistoryAsync(id);
        int playlistId = await _db.CreatePlaylistAsync("list");
        await _db.AddTrackToPlaylistAsync(playlistId, id);

        await _library.RemoveWatchedFolderAsync(folder);

        Assert.Empty(await _db.GetAllTracksAsync());
        Assert.Empty(await _db.GetRecentTracksAsync(10));
        Assert.Empty(await _db.GetFrequentTracksAsync(10));
        Assert.Empty(await _db.SearchTracksAsync("song"));
        Assert.Empty(await _db.GetRecentlyPlayedTracksAsync(10));
        Assert.Empty(await _db.GetMostPlayedTracksAsync(10, DateTime.UtcNow.AddDays(-30)));
        Assert.Empty(await _db.GetPlaylistTracksAsync(playlistId));
        Assert.Empty(await _db.GetTrackIdsWithoutCoverAsync());
    }

    [Fact]
    public async Task MissingFile_IsHiddenButKept_AndComesBackWithTheFile()
    {
        string folder = Path.Combine(_dir, "music");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "song.mp3");
        File.WriteAllText(file, "audio");
        int id = await AddTrackAsync(file);
        await _db.AddWatchedFolderAsync(folder);

        File.Delete(file);
        var (visible, detached) = await _library.GetValidTracksAsync();
        Assert.Empty(visible);
        Assert.Equal(1, detached);
        Assert.Equal(7, (await _db.GetTrackByIdAsync(id))!.PlayCount);

        File.WriteAllText(file, "audio");
        var (back, _) = await _library.GetValidTracksAsync();
        Assert.Single(back);
        Assert.Equal(7, back[0].PlayCount);
        Assert.Equal(0, (await _library.GetDetachedSummaryAsync()).Count);
    }

    [Fact]
    public async Task RelocatedFolder_KeepsStatsAndFindsTheFilesAgain()
    {
        string oldFolder = Path.Combine(_dir, "old");
        string newFolder = Path.Combine(_dir, "new", "music");
        Directory.CreateDirectory(Path.Combine(oldFolder, "album"));
        Directory.CreateDirectory(Path.Combine(newFolder, "album"));
        string oldFile = Path.Combine(oldFolder, "album", "song.mp3");
        string newFile = Path.Combine(newFolder, "album", "song.mp3");
        File.WriteAllText(oldFile, "audio");
        int id = await AddTrackAsync(oldFile);
        await _db.AddWatchedFolderAsync(oldFolder);

        File.Move(oldFile, newFile);
        await _library.GetValidTracksAsync();

        int moved = await _library.RelocateWatchedFolderAsync(oldFolder, newFolder);

        Assert.Equal(1, moved);
        var tracks = await _db.GetAllTracksAsync();
        Assert.Single(tracks);
        Assert.Equal(id, tracks[0].Id);
        Assert.Equal(newFile, tracks[0].Path);
        Assert.Equal(7, tracks[0].PlayCount);
        Assert.NotNull(await _db.GetCoverArtAsync(id));
        Assert.Contains(newFolder, await _db.GetWatchedFoldersAsync());
    }

    [Fact]
    public async Task Purge_DropsOnlyRowsOlderThanRetention()
    {
        string folder = Path.Combine(_dir, "music");
        await AddTrackAsync(Path.Combine(folder, "song.mp3"));
        await _library.RemoveWatchedFolderAsync(folder);

        Assert.Equal(0, await _library.PurgeDetachedTracksAsync(30));
        Assert.Equal(1, (await _library.GetDetachedSummaryAsync()).Count);

        Assert.Equal(1, await _library.PurgeAllDetachedTracksAsync());
        Assert.Equal(0, (await _library.GetDetachedSummaryAsync()).Count);
    }

    [Fact]
    public async Task Purge_WithRetentionOff_KeepsEverything()
    {
        string folder = Path.Combine(_dir, "music");
        await AddTrackAsync(Path.Combine(folder, "song.mp3"));
        await _library.RemoveWatchedFolderAsync(folder);

        Assert.Equal(0, await _library.PurgeDetachedTracksAsync(0));
        Assert.Equal(1, (await _library.GetDetachedSummaryAsync()).Count);
    }

    [Fact]
    public async Task RemoveWithoutKeeping_DeletesTracksOutright()
    {
        string folder = Path.Combine(_dir, "music");
        int id = await AddTrackAsync(Path.Combine(folder, "song.mp3"));

        await _library.RemoveWatchedFolderAsync(folder, keepTrackData: false);

        Assert.Null(await _db.GetTrackByIdAsync(id));
        Assert.Equal(0, (await _library.GetDetachedSummaryAsync()).Count);
    }
}
