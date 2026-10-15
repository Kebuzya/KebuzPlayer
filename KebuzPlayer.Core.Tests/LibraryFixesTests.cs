using KebuzPlayer.Data;
using KebuzPlayer.Models;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class LibraryFixesTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly LibraryService _library;

    public LibraryFixesTests()
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

    private string Folder(string name)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteWav(string path, string? title = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var w = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(8000, 16, 1)))
            w.Write(new byte[8000], 0, 8000);
        if (title != null)
        {
            using var tf = TagLib.File.Create(path);
            tf.Tag.Title = title;
            tf.Save();
        }
        return path;
    }

    private async Task<int> AddTrackAsync(string path, int playCount = 7)
    {
        int id = await _db.InsertOrUpdateTrackAsync(new Track
        {
            Path = path,
            Title = Path.GetFileNameWithoutExtension(path),
            DurationSeconds = 1,
            PlayCount = playCount,
            DateAdded = DateTime.UtcNow,
        });
        await _db.UpdateCoverArtAsync(id, [1, 2, 3]);
        return id;
    }

    [Fact]
    public async Task RemovedFolder_StaysRemovedWhileItsFilesExist()
    {
        string folder = Folder("music");
        string file = Path.Combine(folder, "song.mp3");
        File.WriteAllText(file, "x");
        await _db.AddWatchedFolderAsync(folder);
        int id = await AddTrackAsync(file);

        await _library.RemoveWatchedFolderAsync(folder);
        var (visible, _) = await _library.GetValidTracksAsync();

        Assert.Empty(visible);
        Assert.Equal(7, (await _db.GetTrackByIdAsync(id))!.PlayCount);
    }

    [Fact]
    public async Task RemovedNestedFolder_KeepsTracksCoveredByOuterFolder()
    {
        string outer = Folder("music");
        string inner = Folder(Path.Combine("music", "rock"));
        string file = Path.Combine(inner, "song.mp3");
        File.WriteAllText(file, "x");
        await _db.AddWatchedFolderAsync(outer);
        await _db.AddWatchedFolderAsync(inner);
        await AddTrackAsync(file);

        await _library.RemoveWatchedFolderAsync(inner);
        var (visible, _) = await _library.GetValidTracksAsync();

        Assert.Single(visible);
    }

    [Fact]
    public async Task MissingFileUnderWatchedFolder_StillComesBack()
    {
        string folder = Folder("music");
        string file = Path.Combine(folder, "song.mp3");
        File.WriteAllText(file, "x");
        await _db.AddWatchedFolderAsync(folder);
        await AddTrackAsync(file);

        File.Delete(file);
        await _library.GetValidTracksAsync();
        File.WriteAllText(file, "x");
        var (visible, _) = await _library.GetValidTracksAsync();

        Assert.Single(visible);
    }

    [Fact]
    public async Task Relocate_OntoAlreadyScannedFiles_MergesInsteadOfFailing()
    {
        string oldF = Path.Combine(_dir, "old");
        string newF = Folder("new");
        await _db.AddWatchedFolderAsync(oldF);
        int a = await AddTrackAsync(Path.Combine(oldF, "1.mp3"), playCount: 3);
        int b = await AddTrackAsync(Path.Combine(oldF, "2.mp3"), playCount: 5);
        int playlist = await _db.CreatePlaylistAsync("p");
        await _db.AddTrackToPlaylistAsync(playlist, b);
        File.WriteAllText(Path.Combine(newF, "1.mp3"), "x");
        File.WriteAllText(Path.Combine(newF, "2.mp3"), "x");
        await _db.AddWatchedFolderAsync(newF);
        int fresh = await _db.InsertOrUpdateTrackAsync(new Track
        {
            Path = Path.Combine(newF, "2.mp3"), Title = "2", PlayCount = 1, DateAdded = DateTime.UtcNow,
        });
        await _db.AddPlayHistoryAsync(fresh);

        int moved = await _library.RelocateWatchedFolderAsync(oldF, newF);

        Assert.Equal(2, moved);
        var tracks = await _db.GetAllTracksAsync();
        Assert.Equal(2, tracks.Count);
        var two = tracks.Single(t => t.Path == Path.Combine(newF, "2.mp3"));
        Assert.Equal(b, two.Id);
        Assert.Equal(6, two.PlayCount);
        Assert.Equal(1, await _db.GetPlayHistoryCountAsync(b));
        Assert.Single(await _db.GetPlaylistTracksAsync(playlist));
        Assert.Equal(a, tracks.Single(t => t.Path == Path.Combine(newF, "1.mp3")).Id);
        Assert.Equal([newF], await _db.GetWatchedFoldersAsync());
    }

    [Fact]
    public async Task Relocate_IntoItsOwnSubfolder_IsRejectedWithoutChanges()
    {
        string oldF = Folder("old");
        string file = Path.Combine(oldF, "1.mp3");
        await _db.AddWatchedFolderAsync(oldF);
        await AddTrackAsync(file);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _library.RelocateWatchedFolderAsync(oldF, Path.Combine(oldF, "sub")));

        Assert.Equal(file, (await _db.GetAllTracksAsync()).Single().Path);
        Assert.Equal([oldF], await _db.GetWatchedFoldersAsync());
    }

    [Fact]
    public async Task PlaylistCount_IgnoresDetachedTracks()
    {
        string folder = Path.Combine(_dir, "music");
        int id = await AddTrackAsync(Path.Combine(folder, "a.mp3"));
        int keep = await AddTrackAsync(Path.Combine(_dir, "other", "b.mp3"));
        int playlist = await _db.CreatePlaylistAsync("p");
        await _db.AddTrackToPlaylistAsync(playlist, id);
        await _db.AddTrackToPlaylistAsync(playlist, keep);

        await _library.RemoveWatchedFolderAsync(folder);

        Assert.Equal(1, (await _db.GetPlaylistsAsync()).Single().TrackCount);
    }

    [Fact]
    public async Task DeleteToRecycleBin_FileInUse_KeepsTheRow()
    {
        string file = WriteWav(Path.Combine(Folder("music"), "song.wav"));
        int id = await AddTrackAsync(file);
        var track = (await _db.GetTrackByIdAsync(id))!;

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAnyAsync<IOException>(() => _library.DeleteTrackFileAsync(track));

        Assert.NotNull(await _db.GetTrackByIdAsync(id));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task EditTags_FileInUse_ReportsAndLeavesDatabaseAlone()
    {
        string file = WriteWav(Path.Combine(Folder("music"), "song.wav"), "Original");
        int id = await AddTrackAsync(file);
        await _db.UpdateTrackMetadataAsync(id, "Original", "", "");
        var track = (await _db.GetTrackByIdAsync(id))!;

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAnyAsync<IOException>(() =>
                _library.UpdateTrackMetadataAsync(track, "Edited", "", ""));

        Assert.Equal("Original", (await _db.GetTrackByIdAsync(id))!.Title);
    }

    [Fact]
    public async Task Rescan_PicksUpTagsChangedOutside()
    {
        string folder = Folder("music");
        string file = WriteWav(Path.Combine(folder, "song.wav"), "Before");
        await _db.AddWatchedFolderAsync(folder);
        await _library.ScanFolderAsync(folder);

        using (var tf = TagLib.File.Create(file))
        {
            tf.Tag.Title = "After";
            tf.Save();
        }
        await _library.ScanFolderAsync(folder);

        Assert.Equal("After", (await _db.GetAllTracksAsync()).Single().Title);
    }

    [Fact]
    public async Task Rescan_UnreadableTags_KeepsStoredTitle()
    {
        string folder = Folder("music");
        string file = Path.Combine(folder, "broken.mp3");
        File.WriteAllText(file, "not audio at all");
        int id = await AddTrackAsync(file);
        await _db.UpdateTrackMetadataAsync(id, "Stored Title", "Stored Artist", "");

        await _library.ScanFolderAsync(folder);

        var t = (await _db.GetTrackByIdAsync(id))!;
        Assert.Equal("Stored Title", t.Title);
        Assert.Equal("Stored Artist", t.Artist);
    }

    [Fact]
    public async Task RestoredQueue_KeepsSavedOrderAndSkipsGoneAndDisabled()
    {
        string on = Folder("on"), off = Folder("off");
        await _db.AddWatchedFolderAsync(on);
        await _db.AddWatchedFolderAsync(off);
        await _db.SetWatchedFolderActiveAsync(off, false);
        int a = await AddTrackAsync(Path.Combine(on, "a.mp3"));
        int b = await AddTrackAsync(Path.Combine(on, "b.mp3"));
        int hidden = await AddTrackAsync(Path.Combine(on, "h.mp3"));
        await _db.UpdateIsHiddenAsync(hidden, true);
        int disabled = await AddTrackAsync(Path.Combine(off, "d.mp3"));
        int gone = await AddTrackAsync(Path.Combine(on, "g.mp3"));
        await _db.DetachTrackAsync(gone);

        var saved = await _library.GetQueueTracksAsync([b, gone, hidden, disabled, a]);
        Assert.Equal([b, hidden, a], saved.Select(t => t.Id));

        var fallback = await _library.GetQueueTracksAsync(null);
        Assert.Equal([a, b], fallback.Select(t => t.Id).Order());
    }

    [Fact]
    public async Task UnreachableFolder_HidesTracksWithoutStartingRetention()
    {
        string folder = Folder("usb");
        string file = Path.Combine(folder, "song.mp3");
        File.WriteAllText(file, "x");
        await _db.AddWatchedFolderAsync(folder);
        int id = await AddTrackAsync(file);

        Directory.Move(folder, folder + "_unplugged");
        var (offline, detached) = await _library.GetValidTracksAsync();

        Assert.Empty(offline);
        Assert.Equal(0, detached);
        Assert.Equal(0, (await _library.GetDetachedSummaryAsync()).Count);

        Directory.Move(folder + "_unplugged", folder);
        var (back, _) = await _library.GetValidTracksAsync();
        Assert.Equal([id], back.Select(t => t.Id));
    }

    private static void WriteReplayGain(string file, double gain)
    {
        using var tf = TagLib.File.Create(file);
        tf.GetTag(TagLib.TagTypes.Id3v2, true);
        tf.Tag.ReplayGainTrackGain = gain;
        tf.Save();
    }

    [Fact]
    public async Task ReplayGainTag_BecomesTheStartingTrackGain()
    {
        string folder = Folder("music");
        string file = WriteWav(Path.Combine(folder, "song.wav"), "Song");
        WriteReplayGain(file, -6.54);
        await _db.AddWatchedFolderAsync(folder);

        await _library.ScanFolderAsync(folder);

        var t = (await _db.GetAllTracksAsync()).Single();
        Assert.Equal(-6.5f, t.UserGainOffset, 2);
        Assert.Equal(-6.54f, t.ReplayGainTarget!.Value, 2);
    }

    [Fact]
    public async Task ReplayGainTag_FoundLater_FillsUntouchedGainButNeverOverridesTheUser()
    {
        string folder = Folder("music");
        string untouched = WriteWav(Path.Combine(folder, "a.wav"), "A");
        string adjusted = WriteWav(Path.Combine(folder, "b.wav"), "B");
        await _db.AddWatchedFolderAsync(folder);
        await _library.ScanFolderAsync(folder);
        var b = (await _db.GetTrackByPathAsync(adjusted))!;
        await _library.UpdateTrackGainAsync(b.Id, 3f);

        WriteReplayGain(untouched, -4.0);
        WriteReplayGain(adjusted, -4.0);
        await _library.ScanFolderAsync(folder);

        Assert.Equal(-4f, (await _db.GetTrackByPathAsync(untouched))!.UserGainOffset, 2);
        Assert.Equal(3f, (await _db.GetTrackByPathAsync(adjusted))!.UserGainOffset, 2);

        await _library.UpdateTrackGainAsync(b.Id, 0f);
        await _library.ScanFolderAsync(folder);
        Assert.Equal(0f, (await _db.GetTrackByPathAsync(adjusted))!.UserGainOffset, 2);
    }

    [Fact]
    public async Task WatchedFolders_IgnoreCaseOfThePath()
    {
        string folder = Folder("Music");
        await _db.AddWatchedFolderAsync(folder);
        await _db.AddWatchedFolderAsync(folder.ToUpperInvariant());
        Assert.Single(await _db.GetWatchedFoldersAsync());

        await _db.SetWatchedFolderActiveAsync(folder.ToLowerInvariant(), false);
        Assert.False((await _db.GetWatchedFolderDetailsAsync()).Single().IsActive);

        await _db.RemoveWatchedFolderAsync(folder.ToLowerInvariant());
        Assert.Empty(await _db.GetWatchedFoldersAsync());
    }
}
