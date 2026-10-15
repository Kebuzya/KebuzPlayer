using KebuzPlayer.Models;
using Microsoft.Data.Sqlite;

namespace KebuzPlayer.Data;

public sealed record TrackMove(int TrackId, string NewPath, bool FileExists);

public class Database : IDisposable
{
    private readonly SqliteConnection _conn;

    public string DbPath { get; }

    public Database() : this(DefaultDbPath()) { }

    public Database(string dbPath)
    {
        DbPath = dbPath;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();

        using var pragmas = _conn.CreateCommand();
        pragmas.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragmas.ExecuteNonQuery();

        ApplyMigrations();
    }

    private static string DefaultDbPath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KebuzPlayer");
        return Path.Combine(appData, "kebuzplayer.db");
    }

    private void ApplyMigrations()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Tracks (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    Path             TEXT NOT NULL UNIQUE,
    Title            TEXT NOT NULL,
    Artist           TEXT,
    Album            TEXT,
    DurationSeconds  INTEGER,
    ReplayGainTarget REAL,
    UserGainOffset   REAL NOT NULL DEFAULT 0,
    IsFavorite       INTEGER NOT NULL DEFAULT 0,
    PlayCount        INTEGER NOT NULL DEFAULT 0,
    DateAdded        TEXT NOT NULL,
    CoverArtBlob     BLOB
);
CREATE TABLE IF NOT EXISTS Playlists (
    Id   INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS PlaylistTracks (
    PlaylistId INTEGER NOT NULL REFERENCES Playlists(Id) ON DELETE CASCADE,
    TrackId    INTEGER NOT NULL REFERENCES Tracks(Id)    ON DELETE CASCADE,
    [Order]    INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (PlaylistId, TrackId)
);
CREATE TABLE IF NOT EXISTS Settings (
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS WatchedFolders (
    Path TEXT PRIMARY KEY
);
CREATE TABLE IF NOT EXISTS PlayHistory (
    TrackId  INTEGER NOT NULL REFERENCES Tracks(Id) ON DELETE CASCADE,
    PlayedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_tracks_playcount  ON Tracks(PlayCount DESC);
CREATE INDEX IF NOT EXISTS idx_tracks_dateadded  ON Tracks(DateAdded DESC);
CREATE INDEX IF NOT EXISTS idx_tracks_artist     ON Tracks(Artist);
CREATE INDEX IF NOT EXISTS idx_playhistory_playedat ON PlayHistory(PlayedAt DESC);
";
        cmd.ExecuteNonQuery();

        RunAlter("ALTER TABLE Playlists     ADD COLUMN CoverArtBlob BLOB");
        RunAlter("ALTER TABLE PlaylistTracks ADD COLUMN DateAdded   TEXT");
        RunAlter("ALTER TABLE WatchedFolders ADD COLUMN IsActive    INTEGER NOT NULL DEFAULT 1");
        RunAlter("ALTER TABLE WatchedFolders ADD COLUMN LastScanned TEXT");
        RunAlter("ALTER TABLE Tracks         ADD COLUMN IsHidden    INTEGER NOT NULL DEFAULT 0");
        RunAlter("ALTER TABLE Tracks         ADD COLUMN DetachedAt  TEXT");
    }

    private void RunAlter(string sql)
    {
        try { using var c = _conn.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
        catch (SqliteException) { }
    }

    public async Task<int> InsertOrUpdateTrackAsync(Track track)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Tracks (Path, Title, Artist, Album, DurationSeconds,
                    ReplayGainTarget, UserGainOffset, IsFavorite, PlayCount, DateAdded, CoverArtBlob)
VALUES (@path, @title, @artist, @album, @dur,
        @rg, @gain, @fav, @pc, @added, @art)
ON CONFLICT(Path) DO UPDATE SET
    Title=CASE WHEN @tags THEN excluded.Title ELSE Tracks.Title END,
    Artist=CASE WHEN @tags THEN excluded.Artist ELSE Tracks.Artist END,
    Album=CASE WHEN @tags THEN excluded.Album ELSE Tracks.Album END,
    DurationSeconds=CASE WHEN @tags THEN excluded.DurationSeconds ELSE Tracks.DurationSeconds END,
    UserGainOffset=CASE WHEN Tracks.UserGainOffset = 0 AND Tracks.ReplayGainTarget IS NULL AND excluded.ReplayGainTarget IS NOT NULL
                        THEN excluded.UserGainOffset ELSE Tracks.UserGainOffset END,
    ReplayGainTarget=COALESCE(excluded.ReplayGainTarget, Tracks.ReplayGainTarget),
    DetachedAt=NULL
RETURNING Id;";
        cmd.Parameters.AddWithValue("@path",  track.Path);
        cmd.Parameters.AddWithValue("@title", track.Title);
        cmd.Parameters.AddWithValue("@artist", (object?)track.Artist ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@album",  (object?)track.Album  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dur",    track.DurationSeconds);
        cmd.Parameters.AddWithValue("@rg",     (object?)track.ReplayGainTarget ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gain",   track.UserGainOffset);
        cmd.Parameters.AddWithValue("@fav",    track.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@pc",     track.PlayCount);
        cmd.Parameters.AddWithValue("@added",  track.DateAdded.ToString("o"));
        cmd.Parameters.AddWithValue("@art",    (object?)null ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tags",   track.TagsRead ? 1 : 0);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateCoverArtAsync(int trackId, byte[] artBytes)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Tracks SET CoverArtBlob=@art WHERE Id=@id";
        cmd.Parameters.AddWithValue("@art", artBytes);
        cmd.Parameters.AddWithValue("@id",  trackId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ClearCoverArtAsync(int trackId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Tracks SET CoverArtBlob=NULL WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", trackId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DetachTrackAsync(int trackId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Tracks SET DetachedAt=@at WHERE Id=@id";
        cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@id", trackId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ReviveTrackAsync(int trackId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Tracks SET DetachedAt=NULL WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", trackId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Track>> GetDetachedTracksAsync()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id,Path,Title,Artist,Album,DurationSeconds,ReplayGainTarget,UserGainOffset,IsFavorite,PlayCount,DateAdded FROM Tracks WHERE DetachedAt IS NOT NULL";
        return await ReadTracksAsync(cmd);
    }

    public async Task RelocateWatchedFolderAsync(string oldPath, string newPath, IReadOnlyList<TrackMove> moves)
    {
        using var tx = _conn.BeginTransaction();

        async Task<object?> Run(string sql, params (string Name, object Value)[] args)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value);
            return await cmd.ExecuteScalarAsync();
        }

        try
        {
            foreach (var move in moves)
            {
                var dup = await Run("SELECT Id FROM Tracks WHERE Path=@p COLLATE NOCASE AND Id<>@id",
                    ("@p", move.NewPath), ("@id", move.TrackId));
                if (dup is long dupId)
                {
                    await Run(@"
UPDATE Tracks SET
    PlayCount    = PlayCount + (SELECT PlayCount FROM Tracks WHERE Id=@dup),
    IsFavorite   = MAX(IsFavorite, (SELECT IsFavorite FROM Tracks WHERE Id=@dup)),
    CoverArtBlob = COALESCE(CoverArtBlob, (SELECT CoverArtBlob FROM Tracks WHERE Id=@dup))
WHERE Id=@id", ("@dup", dupId), ("@id", move.TrackId));
                    await Run("UPDATE PlayHistory SET TrackId=@id WHERE TrackId=@dup",
                        ("@dup", dupId), ("@id", move.TrackId));
                    await Run(@"
INSERT OR IGNORE INTO PlaylistTracks (PlaylistId, TrackId, [Order], DateAdded)
SELECT PlaylistId, @id, [Order], DateAdded FROM PlaylistTracks WHERE TrackId=@dup",
                        ("@dup", dupId), ("@id", move.TrackId));
                    await Run("DELETE FROM Tracks WHERE Id=@dup", ("@dup", dupId));
                }
                await Run(move.FileExists
                        ? "UPDATE Tracks SET Path=@p, DetachedAt=NULL WHERE Id=@id"
                        : "UPDATE Tracks SET Path=@p WHERE Id=@id",
                    ("@p", move.NewPath), ("@id", move.TrackId));
            }

            var target = await Run("SELECT Path FROM WatchedFolders WHERE Path=@p COLLATE NOCASE", ("@p", newPath));
            if (target is string)
                await Run("DELETE FROM WatchedFolders WHERE Path=@p COLLATE NOCASE", ("@p", oldPath));
            else
                await Run("UPDATE WatchedFolders SET Path=@new WHERE Path=@old COLLATE NOCASE",
                    ("@new", newPath), ("@old", oldPath));

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<int> PurgeDetachedTracksAsync(DateTime olderThanUtc)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Tracks WHERE DetachedAt IS NOT NULL AND DetachedAt < @at";
        cmd.Parameters.AddWithValue("@at", olderThanUtc.ToString("o"));
        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(int Count, long CoverBytes)> GetDetachedSummaryAsync()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(*), COALESCE(SUM(LENGTH(CoverArtBlob)), 0)
FROM Tracks WHERE DetachedAt IS NOT NULL";
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (0, 0);
        return (reader.GetInt32(0), reader.GetInt64(1));
    }

    public async Task<Track?> GetTrackByIdAsync(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id,Path,Title,Artist,Album,DurationSeconds,ReplayGainTarget,UserGainOffset,IsFavorite,PlayCount,DateAdded FROM Tracks WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        var list = await ReadTracksAsync(cmd);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<Track?> GetTrackByPathAsync(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id,Path,Title,Artist,Album,DurationSeconds,ReplayGainTarget,UserGainOffset,IsFavorite,PlayCount,DateAdded FROM Tracks WHERE Path=@p COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@p", path);
        var list = await ReadTracksAsync(cmd);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<List<Track>> GetAllTracksAsync()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id,Path,Title,Artist,Album,DurationSeconds,ReplayGainTarget,UserGainOffset,IsFavorite,PlayCount,DateAdded,IsHidden FROM Tracks WHERE DetachedAt IS NULL ORDER BY Title";
        return await ReadTracksAsync(cmd);
    }

    public async Task<List<Track>> GetRecentTracksAsync(int count = 20)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id,Path,Title,Artist,Album,DurationSeconds,ReplayGainTarget,UserGainOffset,IsFavorite,PlayCount,DateAdded FROM Tracks WHERE DetachedAt IS NULL ORDER BY DateAdded DESC LIMIT @count";
        cmd.Parameters.AddWithValue("@count", count);
        return await ReadTracksAsync(cmd);
    }

    public async Task<List<Track>> GetFrequentTracksAsync(int count = 20)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id,Path,Title,Artist,Album,DurationSeconds,ReplayGainTarget,UserGainOffset,IsFavorite,PlayCount,DateAdded FROM Tracks WHERE PlayCount > 0 AND DetachedAt IS NULL ORDER BY PlayCount DESC LIMIT @count";
        cmd.Parameters.AddWithValue("@count", count);
        return await ReadTracksAsync(cmd);
    }

    public async Task<List<Track>> SearchTracksAsync(string query)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT Id,Path,Title,Artist,Album,DurationSeconds,ReplayGainTarget,UserGainOffset,IsFavorite,PlayCount,DateAdded
                            FROM Tracks WHERE DetachedAt IS NULL
                              AND (Title LIKE @q OR Artist LIKE @q OR Album LIKE @q)
                            ORDER BY Title";
        cmd.Parameters.AddWithValue("@q", $"%{query}%");
        return await ReadTracksAsync(cmd);
    }

    public async Task<List<int>> GetTrackIdsWithoutCoverAsync()
    {
        var list = new List<int>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Tracks WHERE CoverArtBlob IS NULL AND DetachedAt IS NULL";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(reader.GetInt32(0));
        return list;
    }

    public async Task<byte[]?> GetCoverArtAsync(int trackId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT CoverArtBlob FROM Tracks WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", trackId);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : (byte[])result;
    }

    public async Task UpdateTrackMetadataAsync(int id, string title, string artist, string album)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Tracks SET Title=@t, Artist=@a, Album=@al WHERE Id=@id";
        cmd.Parameters.AddWithValue("@t",  title);
        cmd.Parameters.AddWithValue("@a",  string.IsNullOrWhiteSpace(artist) ? DBNull.Value : artist);
        cmd.Parameters.AddWithValue("@al", string.IsNullOrWhiteSpace(album)  ? DBNull.Value : album);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateUserGainAsync(int id, float gainDb)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Tracks SET UserGainOffset=@g WHERE Id=@id";
        cmd.Parameters.AddWithValue("@g",  gainDb);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateTrackPathAsync(int id, string newPath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Tracks SET Path=@p WHERE Id=@id";
        cmd.Parameters.AddWithValue("@p",  newPath);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateIsFavoriteAsync(int id, bool isFavorite)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Tracks SET IsFavorite=@f WHERE Id=@id";
        cmd.Parameters.AddWithValue("@f",  isFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateIsHiddenAsync(int id, bool isHidden)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Tracks SET IsHidden=@h WHERE Id=@id";
        cmd.Parameters.AddWithValue("@h", isHidden ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task IncrementPlayCountAsync(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Tracks SET PlayCount=PlayCount+1 WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteTrackAsync(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Tracks WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddPlayHistoryAsync(int trackId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO PlayHistory (TrackId, PlayedAt) VALUES (@id, @at)";
        cmd.Parameters.AddWithValue("@id", trackId);
        cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> GetPlayHistoryCountAsync(int trackId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM PlayHistory WHERE TrackId=@id";
        cmd.Parameters.AddWithValue("@id", trackId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<List<Track>> GetRecentlyPlayedTracksAsync(int count = 20)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT t.Id,t.Path,t.Title,t.Artist,t.Album,t.DurationSeconds,
       t.ReplayGainTarget,t.UserGainOffset,t.IsFavorite,t.PlayCount,t.DateAdded
FROM Tracks t
JOIN PlayHistory ph ON ph.TrackId=t.Id
WHERE t.DetachedAt IS NULL
GROUP BY t.Id
ORDER BY MAX(ph.PlayedAt) DESC
LIMIT @count";
        cmd.Parameters.AddWithValue("@count", count);
        return await ReadTracksAsync(cmd);
    }

    public async Task<List<Track>> GetMostPlayedTracksAsync(int count, DateTime since)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT t.Id,t.Path,t.Title,t.Artist,t.Album,t.DurationSeconds,
       t.ReplayGainTarget,t.UserGainOffset,t.IsFavorite,t.PlayCount,t.DateAdded
FROM Tracks t
JOIN PlayHistory ph ON ph.TrackId=t.Id
WHERE ph.PlayedAt >= @since AND t.DetachedAt IS NULL
GROUP BY t.Id
ORDER BY COUNT(*) DESC, MAX(ph.PlayedAt) DESC
LIMIT @count";
        cmd.Parameters.AddWithValue("@count", count);
        cmd.Parameters.AddWithValue("@since", since.ToUniversalTime().ToString("o"));
        return await ReadTracksAsync(cmd);
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key=@k";
        cmd.Parameters.AddWithValue("@k", key);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : (string)result;
    }

    public async Task SetSettingAsync(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Settings (Key, Value) VALUES (@k, @v) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetWatchedFoldersAsync()
    {
        var list = new List<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Path FROM WatchedFolders";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(reader.GetString(0));
        return list;
    }

    public async Task<List<WatchedFolder>> GetWatchedFolderDetailsAsync()
    {
        var list = new List<WatchedFolder>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Path, IsActive, LastScanned FROM WatchedFolders ORDER BY Path";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new WatchedFolder
            {
                Path        = reader.GetString(0),
                IsActive    = reader.GetInt32(1) != 0,
                LastScanned = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
            });
        return list;
    }

    public async Task AddWatchedFolderAsync(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO WatchedFolders (Path) SELECT @p WHERE NOT EXISTS (SELECT 1 FROM WatchedFolders WHERE Path=@p COLLATE NOCASE)";
        cmd.Parameters.AddWithValue("@p", path);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SetWatchedFolderActiveAsync(string path, bool isActive)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE WatchedFolders SET IsActive=@a WHERE Path=@p COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@p", path);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateWatchedFolderScannedAsync(string path, DateTime when)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE WatchedFolders SET LastScanned=@t WHERE Path=@p COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@t", when.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@p", path);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveWatchedFolderAsync(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM WatchedFolders WHERE Path=@p COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@p", path);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Playlist>> GetPlaylistsAsync()
    {
        var list = new List<Playlist>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT p.Id, p.Name, COUNT(t.Id) AS TrackCount
FROM Playlists p
LEFT JOIN PlaylistTracks pt ON pt.PlaylistId = p.Id
LEFT JOIN Tracks t ON t.Id = pt.TrackId AND t.DetachedAt IS NULL
GROUP BY p.Id
ORDER BY p.Name";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new Playlist
            {
                Id         = reader.GetInt32(0),
                Name       = reader.GetString(1),
                TrackCount = reader.GetInt32(2),
            });
        return list;
    }

    public async Task<int> CreatePlaylistAsync(string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Playlists (Name) VALUES (@n) RETURNING Id";
        cmd.Parameters.AddWithValue("@n", name.Trim());
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task RenamePlaylistAsync(int id, string newName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Playlists SET Name=@n WHERE Id=@id";
        cmd.Parameters.AddWithValue("@n",  newName.Trim());
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeletePlaylistAsync(int id)
    {
        using var tx = _conn.BeginTransaction();
        try
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM PlaylistTracks WHERE PlaylistId=@id";
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM Playlists WHERE Id=@id";
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<bool> AddTrackToPlaylistAsync(int playlistId, int trackId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO PlaylistTracks (PlaylistId, TrackId, [Order], DateAdded)
VALUES (@pid, @tid,
    (SELECT COALESCE(MAX([Order]), 0) + 1 FROM PlaylistTracks WHERE PlaylistId = @pid),
    @date)
ON CONFLICT DO NOTHING";
        cmd.Parameters.AddWithValue("@pid",  playlistId);
        cmd.Parameters.AddWithValue("@tid",  trackId);
        cmd.Parameters.AddWithValue("@date", DateTime.UtcNow.ToString("o"));
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task RemoveTrackFromPlaylistAsync(int playlistId, int trackId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM PlaylistTracks WHERE PlaylistId=@pid AND TrackId=@tid";
        cmd.Parameters.AddWithValue("@pid", playlistId);
        cmd.Parameters.AddWithValue("@tid", trackId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Track>> GetPlaylistTracksAsync(int playlistId, string sort = "manual")
    {
        string orderBy = sort switch
        {
            "title"     => "t.Title ASC",
            "artist"    => "t.Artist ASC, t.Title ASC",
            "dateAdded" => "COALESCE(pt.DateAdded,'') ASC, pt.[Order] ASC",
            _           => "pt.[Order] ASC",
        };
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $@"
SELECT t.Id,t.Path,t.Title,t.Artist,t.Album,t.DurationSeconds,
       t.ReplayGainTarget,t.UserGainOffset,t.IsFavorite,t.PlayCount,t.DateAdded
FROM Tracks t
JOIN PlaylistTracks pt ON pt.TrackId=t.Id
WHERE pt.PlaylistId=@pid AND t.DetachedAt IS NULL
ORDER BY {orderBy}";
        cmd.Parameters.AddWithValue("@pid", playlistId);
        return await ReadTracksAsync(cmd);
    }

    public async Task SavePlaylistOrderAsync(int playlistId, IList<int> trackIds)
    {
        using var tx = _conn.BeginTransaction();
        try
        {
            for (int i = 0; i < trackIds.Count; i++)
            {
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE PlaylistTracks SET [Order]=@o WHERE PlaylistId=@pid AND TrackId=@tid";
                cmd.Parameters.AddWithValue("@o",   i);
                cmd.Parameters.AddWithValue("@pid", playlistId);
                cmd.Parameters.AddWithValue("@tid", trackIds[i]);
                await cmd.ExecuteNonQueryAsync();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task UpdatePlaylistCoverAsync(int playlistId, byte[]? cover)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE Playlists SET CoverArtBlob=@art WHERE Id=@id";
        cmd.Parameters.AddWithValue("@art", (object?)cover ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id",  playlistId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<byte[]?> GetPlaylistCoverAsync(int playlistId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT CoverArtBlob FROM Playlists WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", playlistId);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : (byte[])result;
    }

    private static async Task<List<Track>> ReadTracksAsync(SqliteCommand cmd)
    {
        var list = new List<Track>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(MapRow(reader));
        return list;
    }

    private static Track MapRow(SqliteDataReader r) => new()
    {
        Id              = r.GetInt32(0),
        Path            = r.GetString(1),
        Title           = r.GetString(2),
        Artist          = r.IsDBNull(3) ? "" : r.GetString(3),
        Album           = r.IsDBNull(4) ? "" : r.GetString(4),
        DurationSeconds = r.IsDBNull(5) ? 0  : r.GetInt32(5),
        ReplayGainTarget= r.IsDBNull(6) ? null : (float?)r.GetDouble(6),
        UserGainOffset  = r.IsDBNull(7) ? 0f : (float)r.GetDouble(7),
        IsFavorite      = r.GetInt32(8) != 0,
        PlayCount       = r.GetInt32(9),
        DateAdded       = DateTime.Parse(r.GetString(10)),
        IsHidden        = r.FieldCount > 11 && !r.IsDBNull(11) && r.GetInt32(11) != 0,
    };

    public void Dispose()
    {
        _conn.Dispose();
    }
}
