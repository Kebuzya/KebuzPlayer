using System.Collections.Generic;
using KebuzPlayer.Services;
using KebuzPlayer.Lang;

namespace KebuzPlayer.ViewModels;

public sealed record TrackSortOption(TrackSortMode Mode, string Key)
{
    public string Name => Loc.T(Key);
}

public static class TrackSortOptions
{
    public static readonly IReadOnlyList<TrackSortOption> Library =
    [
        new(TrackSortMode.Title,     "Sort.Title"),
        new(TrackSortMode.Artist,    "Sort.Artist"),
        new(TrackSortMode.Album,     "Sort.Album"),
        new(TrackSortMode.DateAdded, "Sort.DateAdded"),
        new(TrackSortMode.Duration,  "Sort.Duration"),
    ];

    public static readonly IReadOnlyList<TrackSortOption> Playlist =
    [
        new(TrackSortMode.Manual,    "Sort.Manual"),
        new(TrackSortMode.Title,     "Sort.Title"),
        new(TrackSortMode.Artist,    "Sort.Artist"),
        new(TrackSortMode.Album,     "Sort.Album"),
        new(TrackSortMode.DateAdded, "Sort.DateAdded"),
        new(TrackSortMode.Duration,  "Sort.Duration"),
    ];
}
