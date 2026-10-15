using Avalonia;
using Avalonia.Controls;

namespace KebuzPlayer.Controls;

public static class TableHeaderSync
{
    public static void Attach(ListBox list, Grid header, double left, double right)
    {
        list.TemplateApplied += (_, _) =>
        {
            if (list.Scroll is not ScrollViewer scroll)
                return;
            scroll.PropertyChanged += (_, e) =>
            {
                if (e.Property == ScrollViewer.ViewportProperty)
                    Sync(list, scroll, header, left, right);
            };
            Sync(list, scroll, header, left, right);
        };
    }

    private static void Sync(ListBox list, ScrollViewer scroll, Grid header, double left, double right)
    {
        var gap = list.Bounds.Width - scroll.Viewport.Width;
        if (gap < 0 || double.IsNaN(gap))
            gap = 0;
        header.Margin = new Thickness(left, 0, right + gap, 0);
    }
}
