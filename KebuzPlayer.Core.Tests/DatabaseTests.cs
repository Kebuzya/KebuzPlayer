using KebuzPlayer.Data;
using KebuzPlayer.Models;

namespace KebuzPlayer.Core.Tests;

public class DatabaseTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;

    public DatabaseTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KebuzTests_" + Guid.NewGuid().ToString("N"));
        _db = new Database(Path.Combine(_dir, "test.db"));
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static Track NewTrack(string path, string title, string artist = "", string album = "") => new()
    {
        Path = path,
        Title = title,
        Artist = artist,
        Album = album,
        DurationSeconds = 180,
        DateAdded = DateTime.UtcNow,
    };

    [Fact]
    public async Task InsertTrack_ReturnsId_AndRoundTrips()
    {
        var track = NewTrack(@"C:\music\a.mp3", "Alpha", "Artist1", "Album1");
        track.UserGainOffset = -3.5f;
        track.ReplayGainTarget = -8.2f;

        var id = await _db.InsertOrUpdateTrackAsync(track);
        Assert.True(id > 0);

        var loaded = await _db.GetTrackByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal(track.Path, loaded.Path);
        Assert.Equal("Alpha", loaded.Title);
        Assert.Equal("Artist1", loaded.Artist);
        Assert.Equal("Album1", loaded.Album);
        Assert.Equal(180, loaded.DurationSeconds);
        Assert.Equal(-3.5f, loaded.UserGainOffset);
        Assert.NotNull(loaded.ReplayGainTarget);
        Assert.Equal(-8.2f, loaded.ReplayGainTarget.Value, 3);
        Assert.Equal(track.DateAdded, loaded.DateAdded.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AddPlayHistory_IsCountedPerTrack()
    {
        var id = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\music\hist.mp3", "Hist"));

        await _db.AddPlayHistoryAsync(id);
        await _db.AddPlayHistoryAsync(id);

        Assert.Equal(2, await _db.GetPlayHistoryCountAsync(id));
    }

    [Fact]
    public async Task RecentlyPlayed_OrdersByLastPlay_Distinct()
    {
        var a = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\a.mp3", "A"));
        var b = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\b.mp3", "B"));

        await _db.AddPlayHistoryAsync(a);
        await _db.AddPlayHistoryAsync(b);
        await _db.AddPlayHistoryAsync(a);

        var recent = await _db.GetRecentlyPlayedTracksAsync(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal(a, recent[0].Id);
        Assert.Equal(b, recent[1].Id);
    }

    [Fact]
    public async Task MostPlayed_RanksByCountWithinPeriod()
    {
        var a = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\a.mp3", "A"));
        var b = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\b.mp3", "B"));

        await _db.AddPlayHistoryAsync(a);
        await _db.AddPlayHistoryAsync(b);
        await _db.AddPlayHistoryAsync(b);

        var top = await _db.GetMostPlayedTracksAsync(10, DateTime.UtcNow.AddDays(-1));
        Assert.Equal(2, top.Count);
        Assert.Equal(b, top[0].Id);
        Assert.Equal(a, top[1].Id);

        var none = await _db.GetMostPlayedTracksAsync(10, DateTime.UtcNow.AddDays(1));
        Assert.Empty(none);
    }

    [Fact]
    public async Task InsertSamePath_UpdatesInsteadOfDuplicating()
    {
        var id1 = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\same.mp3", "Old"));
        var id2 = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\same.mp3", "New"));

        Assert.Equal(id1, id2);
        var all = await _db.GetAllTracksAsync();
        Assert.Single(all);
        Assert.Equal("Old", all[0].Title);
    }

    [Fact]
    public async Task Upsert_NullReplayGain_PreservesExistingValue()
    {
        var track = NewTrack(@"C:\m\rg.mp3", "T");
        track.ReplayGainTarget = -7f;
        var id = await _db.InsertOrUpdateTrackAsync(track);

        var again = NewTrack(@"C:\m\rg.mp3", "T");
        again.ReplayGainTarget = null;
        await _db.InsertOrUpdateTrackAsync(again);

        var loaded = await _db.GetTrackByIdAsync(id);
        Assert.NotNull(loaded?.ReplayGainTarget);
        Assert.Equal(-7f, loaded.ReplayGainTarget.Value, 3);
    }

    [Fact]
    public async Task GetTrackById_Missing_ReturnsNull()
    {
        Assert.Null(await _db.GetTrackByIdAsync(999));
    }

    [Fact]
    public async Task GetAllTracks_OrderedByTitle()
    {
        await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\1.mp3", "Bravo"));
        await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\2.mp3", "Alpha"));

        var all = await _db.GetAllTracksAsync();
        Assert.Equal(new[] { "Alpha", "Bravo" }, all.Select(t => t.Title));
    }

    [Fact]
    public async Task SearchTracks_MatchesTitleArtistAlbum()
    {
        await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\1.mp3", "Sunrise", "Nobody", "X"));
        await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\2.mp3", "Other", "Sunset Crew", "Y"));
        await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\3.mp3", "Third", "Z", "Sunday Album"));
        await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\4.mp3", "Moon", "M", "N"));

        var found = await _db.SearchTracksAsync("sun");
        Assert.Equal(3, found.Count);
    }

    [Fact]
    public async Task IncrementPlayCount_And_FrequentTracks()
    {
        var id1 = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\1.mp3", "Played"));
        await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\2.mp3", "Never"));

        await _db.IncrementPlayCountAsync(id1);
        await _db.IncrementPlayCountAsync(id1);

        var frequent = await _db.GetFrequentTracksAsync(10);
        Assert.Single(frequent);
        Assert.Equal("Played", frequent[0].Title);
        Assert.Equal(2, frequent[0].PlayCount);
    }

    [Fact]
    public async Task UpdateIsFavorite_RoundTrips()
    {
        var id = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\f.mp3", "Fav"));
        await _db.UpdateIsFavoriteAsync(id, true);
        Assert.True((await _db.GetTrackByIdAsync(id))!.IsFavorite);

        await _db.UpdateIsFavoriteAsync(id, false);
        Assert.False((await _db.GetTrackByIdAsync(id))!.IsFavorite);
    }

    [Fact]
    public async Task UpdateTrackMetadata_EmptyArtistStoredAsNull_ReadBackAsEmpty()
    {
        var id = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\meta.mp3", "T", "A", "Al"));
        await _db.UpdateTrackMetadataAsync(id, "T2", "", "");

        var loaded = await _db.GetTrackByIdAsync(id);
        Assert.Equal("T2", loaded!.Title);
        Assert.Equal("", loaded.Artist);
        Assert.Equal("", loaded.Album);
    }

    [Fact]
    public async Task UpdateTrackPath_And_Delete()
    {
        var id = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\old.mp3", "T"));
        await _db.UpdateTrackPathAsync(id, @"D:\new\place.mp3");
        Assert.Equal(@"D:\new\place.mp3", (await _db.GetTrackByIdAsync(id))!.Path);

        await _db.DeleteTrackAsync(id);
        Assert.Null(await _db.GetTrackByIdAsync(id));
    }

    [Fact]
    public async Task CoverArt_RoundTrips()
    {
        var id = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\c.mp3", "T"));
        Assert.Null(await _db.GetCoverArtAsync(id));

        var art = new byte[] { 1, 2, 3, 4 };
        await _db.UpdateCoverArtAsync(id, art);
        Assert.Equal(art, await _db.GetCoverArtAsync(id));
    }

    [Fact]
    public async Task Settings_SetGetOverwrite()
    {
        Assert.Null(await _db.GetSettingAsync("missing"));

        await _db.SetSettingAsync("k", "v1");
        Assert.Equal("v1", await _db.GetSettingAsync("k"));

        await _db.SetSettingAsync("k", "v2");
        Assert.Equal("v2", await _db.GetSettingAsync("k"));
    }

    [Fact]
    public async Task WatchedFolders_DuplicateIgnored()
    {
        await _db.AddWatchedFolderAsync(@"C:\music");
        await _db.AddWatchedFolderAsync(@"C:\music");
        await _db.AddWatchedFolderAsync(@"D:\more");

        var folders = await _db.GetWatchedFoldersAsync();
        Assert.Equal(2, folders.Count);
    }

    [Fact]
    public async Task WatchedFolder_DefaultsActive_AndTogglesPersist()
    {
        await _db.AddWatchedFolderAsync(@"C:\music");

        var details = await _db.GetWatchedFolderDetailsAsync();
        Assert.Single(details);
        Assert.True(details[0].IsActive);
        Assert.Null(details[0].LastScanned);

        await _db.SetWatchedFolderActiveAsync(@"C:\music", false);
        details = await _db.GetWatchedFolderDetailsAsync();
        Assert.False(details[0].IsActive);
    }

    [Fact]
    public async Task WatchedFolder_RemoveDropsEntry()
    {
        await _db.AddWatchedFolderAsync(@"C:\music");
        await _db.RemoveWatchedFolderAsync(@"C:\music");
        Assert.Empty(await _db.GetWatchedFoldersAsync());
    }

    [Fact]
    public async Task Track_HiddenFlagRoundTrips()
    {
        var id = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\music\h.mp3", "Hidden"));
        await _db.UpdateIsHiddenAsync(id, true);

        var all = await _db.GetAllTracksAsync();
        Assert.True(all.Single(t => t.Id == id).IsHidden);
    }

    [Fact]
    public async Task Playlist_CreateRenameDelete()
    {
        var pid = await _db.CreatePlaylistAsync("  My List  ");
        var lists = await _db.GetPlaylistsAsync();
        Assert.Single(lists);
        Assert.Equal("My List", lists[0].Name);
        Assert.Equal(0, lists[0].TrackCount);

        await _db.RenamePlaylistAsync(pid, "Renamed");
        Assert.Equal("Renamed", (await _db.GetPlaylistsAsync())[0].Name);

        await _db.DeletePlaylistAsync(pid);
        Assert.Empty(await _db.GetPlaylistsAsync());
    }

    [Fact]
    public async Task PlaylistTracks_AddDuplicate_ReturnsFalse()
    {
        var pid = await _db.CreatePlaylistAsync("P");
        var tid = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\1.mp3", "T"));

        Assert.True(await _db.AddTrackToPlaylistAsync(pid, tid));
        Assert.False(await _db.AddTrackToPlaylistAsync(pid, tid));
    }

    [Fact]
    public async Task PlaylistTracks_KeepInsertionOrder_AndReorder()
    {
        var pid = await _db.CreatePlaylistAsync("P");
        var t1 = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\1.mp3", "First"));
        var t2 = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\2.mp3", "Second"));
        var t3 = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\3.mp3", "Third"));

        await _db.AddTrackToPlaylistAsync(pid, t1);
        await _db.AddTrackToPlaylistAsync(pid, t2);
        await _db.AddTrackToPlaylistAsync(pid, t3);

        var manual = await _db.GetPlaylistTracksAsync(pid);
        Assert.Equal(new[] { t1, t2, t3 }, manual.Select(t => t.Id));

        await _db.SavePlaylistOrderAsync(pid, new[] { t3, t1, t2 });
        var reordered = await _db.GetPlaylistTracksAsync(pid);
        Assert.Equal(new[] { t3, t1, t2 }, reordered.Select(t => t.Id));
    }

    [Fact]
    public async Task PlaylistTracks_SortByTitle()
    {
        var pid = await _db.CreatePlaylistAsync("P");
        var t1 = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\1.mp3", "Zeta"));
        var t2 = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\2.mp3", "Alpha"));
        await _db.AddTrackToPlaylistAsync(pid, t1);
        await _db.AddTrackToPlaylistAsync(pid, t2);

        var byTitle = await _db.GetPlaylistTracksAsync(pid, "title");
        Assert.Equal(new[] { "Alpha", "Zeta" }, byTitle.Select(t => t.Title));
    }

    [Fact]
    public async Task DeletingTrack_RemovesItFromPlaylists()
    {
        var pid = await _db.CreatePlaylistAsync("P");
        var tid = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\1.mp3", "T"));
        await _db.AddTrackToPlaylistAsync(pid, tid);

        await _db.DeleteTrackAsync(tid);
        Assert.Empty(await _db.GetPlaylistTracksAsync(pid));
    }

    [Fact]
    public async Task RemoveTrackFromPlaylist_KeepsTrackInLibrary()
    {
        var pid = await _db.CreatePlaylistAsync("P");
        var tid = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\1.mp3", "T"));
        await _db.AddTrackToPlaylistAsync(pid, tid);

        await _db.RemoveTrackFromPlaylistAsync(pid, tid);
        Assert.Empty(await _db.GetPlaylistTracksAsync(pid));
        Assert.NotNull(await _db.GetTrackByIdAsync(tid));
    }

    [Fact]
    public async Task DeletePlaylist_RemovesMembershipRows()
    {
        var pid = await _db.CreatePlaylistAsync("P");
        var t1 = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\1.mp3", "T1"));
        var t2 = await _db.InsertOrUpdateTrackAsync(NewTrack(@"C:\m\2.mp3", "T2"));
        await _db.AddTrackToPlaylistAsync(pid, t1);
        await _db.AddTrackToPlaylistAsync(pid, t2);

        await _db.DeletePlaylistAsync(pid);
        Assert.Empty(await _db.GetPlaylistTracksAsync(pid));

        var pid2 = await _db.CreatePlaylistAsync("P2");
        var lists = await _db.GetPlaylistsAsync();
        Assert.Single(lists);
        Assert.Equal(0, lists[0].TrackCount);
        Assert.Empty(await _db.GetPlaylistTracksAsync(pid2));
    }

    [Fact]
    public async Task PlaylistCover_RoundTripsAndClears()
    {
        var pid = await _db.CreatePlaylistAsync("P");
        Assert.Null(await _db.GetPlaylistCoverAsync(pid));

        var art = new byte[] { 9, 8, 7 };
        await _db.UpdatePlaylistCoverAsync(pid, art);
        Assert.Equal(art, await _db.GetPlaylistCoverAsync(pid));

        await _db.UpdatePlaylistCoverAsync(pid, null);
        Assert.Null(await _db.GetPlaylistCoverAsync(pid));
    }
}
