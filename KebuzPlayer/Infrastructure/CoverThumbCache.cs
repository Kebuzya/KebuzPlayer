using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using KebuzPlayer.Services;

namespace KebuzPlayer.Infrastructure;

public sealed class CoverThumbCache
{
    private const int Capacity           = 200;
    private const int PlaylistCapacity   = 50;
    private const int TrackThumbWidth    = 160;
    private const int PlaylistCoverWidth = 280;

    private readonly LibraryService _library;
    private readonly object _lock = new();
    private readonly Dictionary<int, LinkedListNode<(int Id, Bitmap Bmp)>> _index = new();
    private readonly LinkedList<(int Id, Bitmap Bmp)> _order = new();
    private readonly Dictionary<int, LinkedListNode<(int Id, Bitmap Bmp)>> _playlistIndex = new();
    private readonly LinkedList<(int Id, Bitmap Bmp)> _playlistOrder = new();
    private readonly SemaphoreSlim _decodeGate = new(2);

    public CoverThumbCache(LibraryService library) => _library = library;

    public async Task<Bitmap?> GetTrackThumbAsync(int trackId, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_index.TryGetValue(trackId, out var hit))
            {
                _order.Remove(hit);
                _order.AddFirst(hit);
                return hit.Value.Bmp;
            }
        }

        var bytes = await _library.GetCoverArtAsync(trackId);
        if (bytes == null || bytes.Length == 0 || ct.IsCancellationRequested)
            return null;

        Bitmap bmp;
        await _decodeGate.WaitAsync(ct);
        try
        {
            bmp = await Task.Run(() =>
            {
                using var ms = new MemoryStream(bytes);
                return Bitmap.DecodeToWidth(ms, TrackThumbWidth);
            }, ct);
        }
        finally
        {
            _decodeGate.Release();
        }

        lock (_lock)
        {
            if (_index.TryGetValue(trackId, out var raced))
            {
                bmp.Dispose();
                _order.Remove(raced);
                _order.AddFirst(raced);
                return raced.Value.Bmp;
            }
            var node = new LinkedListNode<(int Id, Bitmap Bmp)>((trackId, bmp));
            _order.AddFirst(node);
            _index[trackId] = node;
            if (_index.Count > Capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _index.Remove(last.Value.Id);
            }
            return bmp;
        }
    }

    public async Task<Bitmap?> GetTrackCoverAsync(int trackId, int width, CancellationToken ct = default)
    {
        var bytes = await _library.GetCoverArtAsync(trackId);
        if (bytes == null || bytes.Length == 0 || ct.IsCancellationRequested)
            return null;

        await _decodeGate.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
                using var ms = new MemoryStream(bytes);
                return Bitmap.DecodeToWidth(ms, width);
            }, ct);
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    public async Task<Bitmap?> GetPlaylistCoverAsync(int playlistId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_playlistIndex.TryGetValue(playlistId, out var hit))
            {
                _playlistOrder.Remove(hit);
                _playlistOrder.AddFirst(hit);
                return hit.Value.Bmp;
            }
        }

        var bytes = await _library.GetPlaylistCoverAsync(playlistId);
        if (bytes == null || bytes.Length == 0 || ct.IsCancellationRequested)
            return null;

        var bmp = await Task.Run(() =>
        {
            using var ms = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(ms, PlaylistCoverWidth);
        }, ct);

        lock (_lock)
        {
            if (_playlistIndex.Remove(playlistId, out var stale))
                _playlistOrder.Remove(stale);
            var node = new LinkedListNode<(int Id, Bitmap Bmp)>((playlistId, bmp));
            _playlistOrder.AddFirst(node);
            _playlistIndex[playlistId] = node;
            if (_playlistIndex.Count > PlaylistCapacity)
            {
                var last = _playlistOrder.Last!;
                _playlistOrder.RemoveLast();
                _playlistIndex.Remove(last.Value.Id);
            }
        }
        return bmp;
    }

    public void InvalidateTrack(int trackId)
    {
        lock (_lock)
        {
            if (!_index.TryGetValue(trackId, out var node)) return;
            _order.Remove(node);
            _index.Remove(trackId);
        }
    }

    public void InvalidatePlaylist(int playlistId)
    {
        lock (_lock)
        {
            if (_playlistIndex.Remove(playlistId, out var node))
                _playlistOrder.Remove(node);
        }
    }
}
