using System;

namespace KebuzPlayer.Infrastructure;

public static class LibraryNotifier
{
    public static event Action? FoldersChanged;

    public static void RaiseFoldersChanged() => FoldersChanged?.Invoke();

    public static event Action<int>? PlaylistDeleted;

    public static void RaisePlaylistDeleted(int playlistId) => PlaylistDeleted?.Invoke(playlistId);
}
