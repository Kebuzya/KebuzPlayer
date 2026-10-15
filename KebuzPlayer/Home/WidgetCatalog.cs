using System.Collections.Generic;
using Avalonia.Media;
using KebuzPlayer.Lang;

namespace KebuzPlayer.Home;

public sealed record WidgetCatalogEntry(HomeWidgetType Type, string NameKey, string DescriptionKey,
                                        Geometry Icon, IReadOnlyList<WidgetSize> AllowedSizes)
{
    public string DisplayName => Loc.T(NameKey);
    public string Description => Loc.T(DescriptionKey);
}

public static class WidgetCatalog
{
    public static readonly IReadOnlyList<WidgetCatalogEntry> Entries =
    [
        new(HomeWidgetType.Recent, "Widget.Recent", "Widget.Recent.Desc",
            Geometry.Parse("M9,1 A8,8 0 1 0 9,17 A8,8 0 1 0 9,1 Z M9,3 A6,6 0 1 1 9,15 A6,6 0 1 1 9,3 Z M8.25,4.5 L9.75,4.5 9.75,8.9 12.8,10.5 12.1,11.9 8.25,9.9 Z"),
            [new(2, 1), new(2, 2), new(2, 3)]),
        new(HomeWidgetType.Frequent, "Widget.Frequent", "Widget.Frequent.Desc",
            Geometry.Parse("M2,10 L4.8,10 4.8,17 2,17 Z M7.6,5 L10.4,5 10.4,17 7.6,17 Z M13.2,1 L16,1 16,17 13.2,17 Z"),
            [new(2, 1), new(2, 2), new(2, 3)]),
        new(HomeWidgetType.Playlist, "Widget.Playlist", "Widget.Playlist.Desc",
            Geometry.Parse("M1,7 L7,7 7,9 3,9 3,15 9,15 9,11 11,11 11,17 1,17 Z M7,1 L17,1 17,11 7,11 Z M9,3 L15,3 15,9 9,9 Z"),
            [new(1, 1), new(2, 1), new(2, 2), new(2, 3)]),
        new(HomeWidgetType.Resume, "Widget.Resume", "Widget.Resume.Desc",
            Geometry.Parse("M4,1 L15,7.5 4,14 Z M2,16 L9,16 9,17.5 2,17.5 Z M10.5,16.4 L16,16.4 16,17.1 10.5,17.1 Z"),
            [new(2, 1), new(2, 2)]),
        new(HomeWidgetType.Visualizer, "Widget.Visualizer", "Widget.Visualizer.Desc",
            Geometry.Parse("M1,9 L3,9 3,14 1,14 Z M4.75,5 L6.75,5 6.75,14 4.75,14 Z M8.5,7 L10.5,7 10.5,14 8.5,14 Z M12.25,2 L14.25,2 14.25,14 12.25,14 Z M16,6 L18,6 18,14 16,14 Z"),
            [new(2, 1), new(3, 1), new(2, 2), new(3, 2)]),
    ];

    public static WidgetCatalogEntry Get(HomeWidgetType type)
    {
        foreach (var entry in Entries)
            if (entry.Type == type)
                return entry;
        return Entries[0];
    }

    public static WidgetSize Coerce(HomeWidgetType type, WidgetSize size)
    {
        var allowed = Get(type).AllowedSizes;
        foreach (var s in allowed)
            if (s == size)
                return size;
        return allowed[0];
    }
}
