using KebuzPlayer.Models;
using KebuzPlayer.Services;

namespace KebuzPlayer.Core.Tests;

public class TrackSorterTests
{
    private static List<Track> Sample() =>
    [
        new() { Title = "Beta",  Artist = "Zed",   Album = "Two", DurationSeconds = 200, DateAdded = new DateTime(2026, 1, 3) },
        new() { Title = "alpha", Artist = "amber", Album = "One", DurationSeconds = 100, DateAdded = new DateTime(2026, 1, 1) },
        new() { Title = "Gamma", Artist = "Mick",  Album = "Two", DurationSeconds = 300, DateAdded = new DateTime(2026, 1, 2) },
    ];

    [Fact]
    public void Title_IsCaseInsensitiveAscending()
    {
        var result = TrackSorter.Sort(Sample(), TrackSortMode.Title, descending: false).ToList();
        Assert.Equal(["alpha", "Beta", "Gamma"], result.Select(t => t.Title));
    }

    [Fact]
    public void Descending_ReversesOrder()
    {
        var result = TrackSorter.Sort(Sample(), TrackSortMode.Title, descending: true).ToList();
        Assert.Equal(["Gamma", "Beta", "alpha"], result.Select(t => t.Title));
    }

    [Fact]
    public void DateAdded_SortsChronologically()
    {
        var result = TrackSorter.Sort(Sample(), TrackSortMode.DateAdded, descending: false).ToList();
        Assert.Equal(["alpha", "Gamma", "Beta"], result.Select(t => t.Title));
    }

    [Fact]
    public void Duration_SortsNumerically()
    {
        var result = TrackSorter.Sort(Sample(), TrackSortMode.Duration, descending: false).ToList();
        Assert.Equal([100, 200, 300], result.Select(t => t.DurationSeconds));
    }

    [Fact]
    public void Artist_TieBreaksByTitle()
    {
        var result = TrackSorter.Sort(Sample(), TrackSortMode.Artist, descending: false).ToList();
        Assert.Equal(["amber", "Mick", "Zed"], result.Select(t => t.Artist));
    }
}
