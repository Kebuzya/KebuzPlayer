using System.Collections.Generic;
using KebuzPlayer.Services;

namespace KebuzPlayer.Home;

public sealed class HomeWidgetLayoutEntry
{
    public HomeWidgetType Type { get; set; }
    public int Cols { get; set; }
    public int Rows { get; set; }
    public int Row { get; set; } = -1;
    public int Col { get; set; } = -1;
    public int? PlaylistId { get; set; }
    public int PeriodDays { get; set; } = 30;
    public PanelVisualizerSettings? Viz { get; set; }
}

public sealed class HomeWidgetLayout
{
    public List<HomeWidgetLayoutEntry> Widgets { get; set; } = [];
}
