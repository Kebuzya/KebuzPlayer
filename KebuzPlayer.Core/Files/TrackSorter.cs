using KebuzPlayer.Models;

namespace KebuzPlayer.Services;

public static class TrackSorter
{
    public static IEnumerable<Track> Sort(IEnumerable<Track> tracks, TrackSortMode mode, bool descending)
    {
        IOrderedEnumerable<Track> ordered = mode switch
        {
            TrackSortMode.Artist    => tracks.OrderBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase)
                                             .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase),
            TrackSortMode.Album     => tracks.OrderBy(t => t.Album, StringComparer.CurrentCultureIgnoreCase)
                                             .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase),
            TrackSortMode.DateAdded => tracks.OrderBy(t => t.DateAdded),
            TrackSortMode.Duration  => tracks.OrderBy(t => t.DurationSeconds),
            _                       => tracks.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase),
        };
        return descending ? Reverse(ordered) : ordered;
    }

    private static IEnumerable<Track> Reverse(IOrderedEnumerable<Track> ordered)
    {
        var list = ordered.ToList();
        list.Reverse();
        return list;
    }
}
