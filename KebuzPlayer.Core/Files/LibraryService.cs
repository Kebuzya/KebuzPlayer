using KebuzPlayer.Data;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Models;
using KebuzPlayer.Tags;
using TagLib;

namespace KebuzPlayer.Services;

public class LibraryService
{
    internal static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ogg", ".opus", ".aac", ".m4a", ".wav", ".wma"
    };

    private static readonly EnumerationOptions DeepScan = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible    = true,
        AttributesToSkip      = 0,
    };

    private readonly Database _db;

    public LibraryService(Database db) => _db = db;

    public AudioService? Playback { get; set; }

    public async Task<int> ScanFolderAsync(
        string folderPath,
        IProgress<(int done, int total)>? progress = null,
        CancellationToken ct = default)
    {
        var files = Directory
            .EnumerateFiles(folderPath, "*.*", DeepScan)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        int done = 0, added = 0;
        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var track = ReadTags(filePath);
                var id = await _db.InsertOrUpdateTrackAsync(track);
                track.Id = id;
                added++;

                if (track.Id > 0)
                    await TrySaveCoverArtAsync(filePath, track);
            }
            catch (CorruptFileException) { }
            catch (UnsupportedFormatException) { }

            done++;
            progress?.Report((done, files.Count));
        }
        await _db.UpdateWatchedFolderScannedAsync(folderPath, DateTime.UtcNow);
        return added;
    }

    public static bool IsUnderFolder(string folder, string path)
    {
        var f = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (path.Equals(f, StringComparison.OrdinalIgnoreCase)) return true;
        return path.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(f + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static Track ReadTags(string filePath) => TagService.ReadTags(filePath);

    private async Task TrySaveCoverArtAsync(string filePath, Track track)
    {
        if (await _db.GetCoverArtAsync(track.Id) is { Length: > 0 }) return;
        var bytes = TagService.TryGetEmbeddedCoverBytes(filePath);
        if (bytes != null)
            await _db.UpdateCoverArtAsync(track.Id, bytes);
    }

    public async Task GenerateCoverArtAsync(Track track)
    {
        var chaos = await GetCoverChaosAsync();
        var art = await Task.Run(() => RenderCover(track, chaos));
        await _db.UpdateCoverArtAsync(track.Id, art);
    }

    private static byte[] RenderCover(Track track, float chaos)
    {
        var features = TrackAnalyzer.Analyze(track.Path);
        if (features.IsFallback)
            return MetaCoverArtGenerator.Generate(track, 0, chaos);

        var seed  = CoverArtGenerator.FeatureSeed(features);
        var style = CoverArtGenerator.AutoSelectStyle(features, seed, chaos);
        return CoverArtGenerator.Generate(style, seed, features, chaos);
    }

    public Task<int> GenerateMissingCoversAsync(
        IProgress<(int done, int total)>? progress = null,
        CancellationToken ct = default) =>
        GenerateCoversForAsync(trackIds: null, forceRegenerate: false, progress, ct);

    public async Task<int> GenerateCoversForAsync(
        IReadOnlyList<int>? trackIds,
        bool forceRegenerate,
        IProgress<(int done, int total)>? progress = null,
        CancellationToken ct = default)
    {
        using var db = new Database(_db.DbPath);
        var ids = trackIds ?? (forceRegenerate
            ? (await db.GetAllTracksAsync()).Select(t => t.Id).ToList()
            : await db.GetTrackIdsWithoutCoverAsync());
        var chaos = ParseChaos(await db.GetSettingAsync("CoverChaos"));
        int done = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var track = await db.GetTrackByIdAsync(id);
            if (track != null && System.IO.File.Exists(track.Path))
            {
                try
                {
                    var art = await Task.Run(() => RenderCover(track, chaos), ct);
                    await db.UpdateCoverArtAsync(track.Id, art);
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            done++;
            progress?.Report((done, ids.Count));
        }
        return done;
    }

    public async Task<float> GetCoverChaosAsync() =>
        ParseChaos(await _db.GetSettingAsync("CoverChaos"));

    private static float ParseChaos(string? s) =>
        s != null && int.TryParse(s, out int pct) ? Math.Clamp(pct, 0, 100) / 100f : 0f;

    public async Task<Track?> AddFileAsync(string filePath)
    {
        if (!SupportedExtensions.Contains(Path.GetExtension(filePath))) return null;
        var track = ReadTags(filePath);
        track.Id = await _db.InsertOrUpdateTrackAsync(track);
        if (track.Id > 0)
            await TrySaveCoverArtAsync(filePath, track);
        return track;
    }

    public static Track? LoadAdHocTrack(string filePath)
    {
        if (!SupportedExtensions.Contains(Path.GetExtension(filePath))) return null;
        try { return ReadTags(filePath); }
        catch (CorruptFileException) { return null; }
        catch (UnsupportedFormatException) { return null; }
        catch (IOException) { return null; }
    }

    public Task<Track?> GetTrackByIdAsync(int id)         => _db.GetTrackByIdAsync(id);
    public Task<byte[]?> GetCoverArtAsync(int trackId)    => _db.GetCoverArtAsync(trackId);
    public Task SaveCoverArtAsync(int trackId, byte[] art) => _db.UpdateCoverArtAsync(trackId, art);
    public Task ClearCoverArtAsync(int trackId)           => _db.ClearCoverArtAsync(trackId);
    public Task<List<string>> GetWatchedFoldersAsync()    => _db.GetWatchedFoldersAsync();
    public Task<List<WatchedFolder>> GetWatchedFolderDetailsAsync() => _db.GetWatchedFolderDetailsAsync();
    public Task AddWatchedFolderAsync(string path)        => _db.AddWatchedFolderAsync(path);
    public Task SetWatchedFolderActiveAsync(string path, bool isActive) =>
        _db.SetWatchedFolderActiveAsync(path, isActive);
    public Task<List<Track>> GetAllTracksAsync()         => _db.GetAllTracksAsync();
    public Task<List<Track>> GetRecentTracksAsync(int n)  => _db.GetRecentTracksAsync(n);
    public Task<List<Track>> GetFrequentTracksAsync(int n) => _db.GetFrequentTracksAsync(n);
    public Task<List<Track>> SearchTracksAsync(string q)  => _db.SearchTracksAsync(q);
    public Task AddPlayHistoryAsync(int trackId)          => _db.AddPlayHistoryAsync(trackId);
    public Task<int> GetPlayHistoryCountAsync(int trackId) => _db.GetPlayHistoryCountAsync(trackId);
    public Task<List<Track>> GetRecentlyPlayedTracksAsync(int count) => _db.GetRecentlyPlayedTracksAsync(count);
    public Task<List<Track>> GetMostPlayedTracksAsync(int count, DateTime since) => _db.GetMostPlayedTracksAsync(count, since);
    public Task<string?> GetSettingAsync(string key)      => _db.GetSettingAsync(key);
    public Task SetSettingAsync(string key, string value) => _db.SetSettingAsync(key, value);

    public async Task<(List<Track> Tracks, int DetachedCount)> GetValidTracksAsync()
    {
        var folders = await _db.GetWatchedFolderDetailsAsync();
        var gone = await _db.GetDetachedTracksAsync();
        var revive = await Task.Run(() => gone
            .Where(t => folders.Any(f => IsUnderFolder(f.Path, t.Path)) && System.IO.File.Exists(t.Path))
            .Select(t => t.Id)
            .ToList());
        foreach (var id in revive)
            await _db.ReviveTrackAsync(id);

        var all = await _db.GetAllTracksAsync();
        var (valid, missing) = await Task.Run(() => Classify(all, folders));
        foreach (var t in missing)
            await _db.DetachTrackAsync(t.Id);
        return (valid, missing.Count);
    }

    private static (List<Track> Valid, List<Track> Missing) Classify(List<Track> all, List<WatchedFolder> folders)
    {
        var inactive = folders.Where(f => !f.IsActive).Select(f => f.Path).ToList();
        var online = folders.Where(f => f.IsActive).ToDictionary(f => f.Path, f => Directory.Exists(f.Path));
        var valid = new List<Track>();
        var missing = new List<Track>();
        foreach (var t in all)
        {
            if (inactive.Any(f => IsUnderFolder(f, t.Path))) continue;
            var covering = online.Where(f => IsUnderFolder(f.Key, t.Path)).ToList();
            if (covering.Count > 0 && covering.All(f => !f.Value)) continue;
            if (System.IO.File.Exists(t.Path)) valid.Add(t);
            else missing.Add(t);
        }
        return (valid, missing);
    }

    public async Task<List<Track>> GetQueueTracksAsync(IReadOnlyList<int>? ids)
    {
        var inactive = (await _db.GetWatchedFolderDetailsAsync())
            .Where(f => !f.IsActive).Select(f => f.Path).ToList();
        var all = (await _db.GetAllTracksAsync())
            .Where(t => !inactive.Any(f => IsUnderFolder(f, t.Path)))
            .ToList();
        if (ids == null)
            return all.Where(t => !t.IsHidden).ToList();

        var byId = all.ToDictionary(t => t.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<int> RelocateWatchedFolderAsync(string oldFolder, string newFolder)
    {
        if (IsUnderFolder(oldFolder, newFolder) || IsUnderFolder(newFolder, oldFolder))
            throw new ArgumentException(CoreText.Format("Error.RelocateNested", "Новое место не может быть внутри старого или содержать его"));

        var all = await _db.GetAllTracksAsync();
        all.AddRange(await _db.GetDetachedTracksAsync());

        var moves = new List<TrackMove>();
        foreach (var t in all)
        {
            if (!IsUnderFolder(oldFolder, t.Path)) continue;
            var movedPath = Path.Combine(newFolder, Path.GetRelativePath(oldFolder, t.Path));
            moves.Add(new TrackMove(t.Id, movedPath, System.IO.File.Exists(movedPath)));
        }

        await _db.RelocateWatchedFolderAsync(oldFolder, newFolder, moves);
        return moves.Count;
    }

    public async Task UpdateTrackGainAsync(int trackId, float gainDb)
    {
        gainDb = Math.Clamp(gainDb, -20f, 20f);
        await _db.UpdateUserGainAsync(trackId, gainDb);
    }

    public Task ToggleFavoriteAsync(int trackId, bool isFavorite) =>
        _db.UpdateIsFavoriteAsync(trackId, isFavorite);

    public Task SetTrackHiddenAsync(int trackId, bool isHidden) =>
        _db.UpdateIsHiddenAsync(trackId, isHidden);

    public async Task<int> RemoveWatchedFolderAsync(string folderPath, bool keepTrackData = true)
    {
        var all = await _db.GetAllTracksAsync();
        var others = (await _db.GetWatchedFoldersAsync())
            .Where(f => !f.Equals(folderPath, StringComparison.OrdinalIgnoreCase)).ToList();
        int removed = 0;
        foreach (var t in all)
            if (IsUnderFolder(folderPath, t.Path) && !others.Any(f => IsUnderFolder(f, t.Path)))
            {
                if (keepTrackData) await _db.DetachTrackAsync(t.Id);
                else await _db.DeleteTrackAsync(t.Id);
                removed++;
            }
        await _db.RemoveWatchedFolderAsync(folderPath);
        return removed;
    }

    public Task<int> PurgeDetachedTracksAsync(int retentionDays) =>
        retentionDays <= 0
            ? Task.FromResult(0)
            : _db.PurgeDetachedTracksAsync(DateTime.UtcNow.AddDays(-retentionDays));

    public Task<int> PurgeAllDetachedTracksAsync() =>
        _db.PurgeDetachedTracksAsync(DateTime.UtcNow.AddDays(1));

    public Task<(int Count, long CoverBytes)> GetDetachedSummaryAsync() =>
        _db.GetDetachedSummaryAsync();

    public static Task<long> GetFolderSizeAsync(string folderPath) => Task.Run(() =>
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath, "*.*", DeepScan))
                if (IsSupportedAudio(file))
                    try { total += new FileInfo(file).Length; } catch { }
        }
        catch { }
        return total;
    });

    public Task IncrementPlayCountAsync(int trackId) =>
        _db.IncrementPlayCountAsync(trackId);

    public async Task UpdateTrackMetadataAsync(Track track, string title, string artist, string album)
    {
        WithFileReleased(track.Path, () => TagService.WriteMetadata(track.Path, title, artist, album));
        await _db.UpdateTrackMetadataAsync(track.Id, title.Trim(), artist.Trim(), album.Trim());
        track.Title  = title.Trim();
        track.Artist = artist.Trim();
        track.Album  = album.Trim();
    }

    public async Task<int> UpdateTracksTagsAsync(IEnumerable<Track> tracks, string artist, string album)
    {
        int changed = 0, failed = 0;
        foreach (var track in tracks)
        {
            var newArtist = string.IsNullOrWhiteSpace(artist) ? track.Artist : artist.Trim();
            var newAlbum  = string.IsNullOrWhiteSpace(album)  ? track.Album  : album.Trim();
            if (newArtist == track.Artist && newAlbum == track.Album) continue;
            try
            {
                WithFileReleased(track.Path, () => TagService.WriteMetadata(track.Path, track.Title, newArtist, newAlbum));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
            {
                failed++;
                continue;
            }
            await _db.UpdateTrackMetadataAsync(track.Id, track.Title, newArtist, newAlbum);
            track.Artist = newArtist;
            track.Album  = newAlbum;
            changed++;
        }
        if (failed > 0)
            throw new IOException(CoreText.Format("Error.TagsPartlyFailed", "Обновлено треков: {0}, не удалось записать теги: {1}", changed, failed));
        return changed;
    }

    public async Task RenameTrackFileAsync(Track track, string newFileName)
    {
        var newPath = Path.Combine(Path.GetDirectoryName(track.Path)!, newFileName);
        if (System.IO.File.Exists(newPath))
            throw new IOException(CoreText.Format("Error.FileExistsInFolder", "Файл «{0}» уже существует в этой папке", newFileName));
        MoveReleased(track.Path, newPath);
        await _db.UpdateTrackPathAsync(track.Id, newPath);
        track.Path = newPath;
    }

    public async Task MoveTrackFileAsync(int trackId, string newFolderPath, string currentPath)
    {
        var fileName = Path.GetFileName(currentPath);
        var newPath  = Path.Combine(newFolderPath, fileName);
        MoveReleased(currentPath, newPath);
        await _db.UpdateTrackPathAsync(trackId, newPath);
    }

    public static bool IsSupportedAudio(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    public async Task<Track?> TransferFileAsync(string sourcePath, string destFolder, bool move)
    {
        var destPath = Path.Combine(destFolder, Path.GetFileName(sourcePath));
        if (string.Equals(destPath, sourcePath, StringComparison.OrdinalIgnoreCase))
            return null;
        if (System.IO.File.Exists(destPath))
            throw new IOException(CoreText.Format("Error.FileExistsInTarget", "Файл «{0}» уже есть в целевой папке", Path.GetFileName(sourcePath)));

        if (move)
        {
            MoveReleased(sourcePath, destPath);
            var existing = await _db.GetTrackByPathAsync(sourcePath);
            if (existing == null) return null;
            await _db.UpdateTrackPathAsync(existing.Id, destPath);
            existing.Path = destPath;
            return existing;
        }

        System.IO.File.Copy(sourcePath, destPath);
        return await AddFileAsync(destPath);
    }

    public Task<Track?> GetTrackByPathAsync(string path) =>
        _db.GetTrackByPathAsync(path);

    public Task RemoveTrackAsync(int trackId) =>
        _db.DeleteTrackAsync(trackId);

    public async Task DeleteTrackFileAsync(Track track)
    {
        RecycleReleased(track.Path);
        await _db.DeleteTrackAsync(track.Id);
    }

    public async Task DeleteFileAsync(string path)
    {
        RecycleReleased(path);
        var existing = await _db.GetTrackByPathAsync(path);
        if (existing != null)
            await _db.DeleteTrackAsync(existing.Id);
    }

    private void WithFileReleased(string path, Action action)
    {
        var hold = Playback?.Release(path);
        try { action(); }
        finally { if (hold != null) Playback!.Restore(hold); }
    }

    private void MoveReleased(string from, string to)
    {
        var hold = Playback?.Release(from);
        string? moved = null;
        try
        {
            System.IO.File.Move(from, to);
            moved = to;
        }
        finally
        {
            if (hold != null) Playback!.Restore(hold, moved);
        }
    }

    private void RecycleReleased(string path)
    {
        if (!System.IO.File.Exists(path)) return;
        var hold = Playback?.Release(path);
        if (Files.RecycleBin.Delete(path))
        {
            if (hold != null) Playback!.Abandon(hold);
            return;
        }
        if (hold != null) Playback!.Restore(hold);
        throw new IOException(CoreText.Format("Error.DeleteFailed", "Не удалось удалить «{0}»: файл занят или нет доступа", Path.GetFileName(path)));
    }

    public Task<List<Playlist>> GetPlaylistsAsync()      => _db.GetPlaylistsAsync();
    public Task RenamePlaylistAsync(int id, string name)  => _db.RenamePlaylistAsync(id, ValidName(name));
    public Task DeletePlaylistAsync(int id)               => _db.DeletePlaylistAsync(id);
    public Task<bool> AddTrackToPlaylistAsync(int playlistId, int trackId) =>
        _db.AddTrackToPlaylistAsync(playlistId, trackId);
    public Task RemoveTrackFromPlaylistAsync(int playlistId, int trackId) =>
        _db.RemoveTrackFromPlaylistAsync(playlistId, trackId);
    public Task<List<Track>> GetPlaylistTracksAsync(int playlistId) =>
        _db.GetPlaylistTracksAsync(playlistId);
    public Task SavePlaylistOrderAsync(int playlistId, IList<int> trackIds) =>
        _db.SavePlaylistOrderAsync(playlistId, trackIds);
    public Task<byte[]?> GetPlaylistCoverAsync(int playlistId) =>
        _db.GetPlaylistCoverAsync(playlistId);

    public Task<int> CreatePlaylistAsync(string name) =>
        _db.CreatePlaylistAsync(ValidName(name));

    public async Task<int> AddTracksToPlaylistAsync(int playlistId, IEnumerable<int> trackIds)
    {
        int added = 0;
        foreach (var id in trackIds)
            if (await _db.AddTrackToPlaylistAsync(playlistId, id))
                added++;
        return added;
    }

    private static string ValidName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Playlist name is empty", nameof(name));
        return trimmed;
    }

    public async Task<byte[]> RegeneratePlaylistCoverAsync(int playlistId)
    {
        var tracks = await _db.GetPlaylistTracksAsync(playlistId);
        var covers = new List<byte[]?>();
        for (int i = 0; i < Math.Min(4, tracks.Count); i++)
            covers.Add(await _db.GetCoverArtAsync(tracks[i].Id));
        var bytes = PlaylistCoverGenerator.Generate(covers);
        await _db.UpdatePlaylistCoverAsync(playlistId, bytes);
        return bytes;
    }
}
