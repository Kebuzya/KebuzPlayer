using System;
using Avalonia;
using Avalonia.Controls;

namespace KebuzPlayer.Controls;

public class JustifiedWrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var childConstraint = new Size(availableSize.Width, double.PositiveInfinity);
        double lineWidth = 0, lineHeight = 0, totalHeight = 0, maxLineWidth = 0;
        foreach (var child in Children)
        {
            child.Measure(childConstraint);
            var size = child.DesiredSize;
            if (lineWidth > 0 && lineWidth + size.Width > availableSize.Width)
            {
                totalHeight += lineHeight;
                maxLineWidth = Math.Max(maxLineWidth, lineWidth);
                lineWidth = 0;
                lineHeight = 0;
            }
            lineWidth += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        totalHeight += lineHeight;
        maxLineWidth = Math.Max(maxLineWidth, lineWidth);
        double width = double.IsInfinity(availableSize.Width) ? maxLineWidth : availableSize.Width;
        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int start = 0;
        double y = 0;
        while (start < Children.Count)
        {
            double lineWidth = 0, lineHeight = 0;
            int count = 0;
            for (int i = start; i < Children.Count; i++)
            {
                var size = Children[i].DesiredSize;
                if (count > 0 && lineWidth + size.Width > finalSize.Width) break;
                lineWidth += size.Width;
                lineHeight = Math.Max(lineHeight, size.Height);
                count++;
            }
            bool fullLine = start + count < Children.Count && count > 1;
            double extra = fullLine ? Math.Max(0, finalSize.Width - lineWidth) / (count - 1) : 0;
            double x = 0;
            for (int i = start; i < start + count; i++)
            {
                var size = Children[i].DesiredSize;
                Children[i].Arrange(new Rect(x, y, size.Width, lineHeight));
                x += size.Width + extra;
            }
            y += lineHeight;
            start += count;
        }
        return new Size(finalSize.Width, y);
    }
}
