using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace KebuzPlayer.Controls;

public class WidgetGridPanel : Panel
{
    public static readonly AttachedProperty<int> ColumnSpanProperty =
        AvaloniaProperty.RegisterAttached<WidgetGridPanel, Control, int>("ColumnSpan", 1);

    public static readonly AttachedProperty<int> RowSpanProperty =
        AvaloniaProperty.RegisterAttached<WidgetGridPanel, Control, int>("RowSpan", 1);

    public static readonly AttachedProperty<int> RowProperty =
        AvaloniaProperty.RegisterAttached<WidgetGridPanel, Control, int>("Row");

    public static readonly AttachedProperty<int> ColProperty =
        AvaloniaProperty.RegisterAttached<WidgetGridPanel, Control, int>("Col");

    static WidgetGridPanel()
    {
        AffectsParentMeasure<WidgetGridPanel>(ColumnSpanProperty, RowSpanProperty, RowProperty, ColProperty);
        AffectsParentArrange<WidgetGridPanel>(ColumnSpanProperty, RowSpanProperty, RowProperty, ColProperty);
    }

    public static int GetColumnSpan(Control control) => control.GetValue(ColumnSpanProperty);
    public static void SetColumnSpan(Control control, int value) => control.SetValue(ColumnSpanProperty, value);

    public static int GetRowSpan(Control control) => control.GetValue(RowSpanProperty);
    public static void SetRowSpan(Control control, int value) => control.SetValue(RowSpanProperty, value);

    public static int GetRow(Control control) => control.GetValue(RowProperty);
    public static void SetRow(Control control, int value) => control.SetValue(RowProperty, value);

    public static int GetCol(Control control) => control.GetValue(ColProperty);
    public static void SetCol(Control control, int value) => control.SetValue(ColProperty, value);

    public const double CellSize = 150;
    public const double Gap = 12;

    public const double MinColumnWidth = 120;

    private const int MinColumns = 2;
    private const int FallbackColumns = 6;

    private readonly Dictionary<Control, (int Row, int Col)> _placed = new();

    public int CurrentColumns { get; private set; } = MinColumns;

    public bool TryGetCell(Control child, out int row, out int col)
    {
        if (_placed.TryGetValue(child, out var cell))
        {
            (row, col) = cell;
            return true;
        }
        row = col = -1;
        return false;
    }
    public double CurrentColumnWidth { get; private set; } = CellSize;

    public static int ColumnsFor(double width) =>
        double.IsInfinity(width) || width <= 0
            ? FallbackColumns
            : Math.Max((int)((width + Gap) / (CellSize + Gap)), MinColumns);

    public static double ColumnWidthFor(double width, int cols) =>
        double.IsInfinity(width) || width <= 0 || cols <= 0
            ? CellSize
            : Math.Max(MinColumnWidth, (width - (cols - 1) * Gap) / cols);

    public Rect CellRect(int row, int col, int spanCols, int spanRows) =>
        new(col * (CurrentColumnWidth + Gap),
            row * (CellSize + Gap),
            spanCols * CurrentColumnWidth + (spanCols - 1) * Gap,
            spanRows * CellSize + (spanRows - 1) * Gap);

    protected override Size MeasureOverride(Size availableSize)
    {
        var (rects, bottomRow) = Layout(availableSize.Width);
        for (int i = 0; i < Children.Count; i++)
            Children[i].Measure(rects[i].Size);

        double width = double.IsInfinity(availableSize.Width)
            ? CurrentColumns * (CellSize + Gap) - Gap
            : availableSize.Width;
        double height = (bottomRow + 1) * (CellSize + Gap);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (rects, _) = Layout(finalSize.Width);
        for (int i = 0; i < Children.Count; i++)
            Children[i].Arrange(rects[i]);
        return finalSize;
    }

    private (List<Rect> Rects, int BottomRow) Layout(double availableWidth)
    {
        CurrentColumns = ColumnsFor(availableWidth);
        CurrentColumnWidth = ColumnWidthFor(availableWidth, CurrentColumns);

        var order = new List<int>(Children.Count);
        for (int i = 0; i < Children.Count; i++) order.Add(i);
        order.Sort((a, b) =>
        {
            int rowA = Math.Max(0, GetRow(Children[a]));
            int rowB = Math.Max(0, GetRow(Children[b]));
            return rowA != rowB ? rowA.CompareTo(rowB) : GetCol(Children[a]).CompareTo(GetCol(Children[b]));
        });

        var rects = new Rect[Children.Count];
        var taken = new List<bool[]>();
        int bottomRow = 0;
        _placed.Clear();

        foreach (int i in order)
        {
            var child = Children[i];
            int spanCols = Math.Clamp(GetColumnSpan(child), 1, CurrentColumns);
            int spanRows = Math.Max(1, GetRowSpan(child));
            int wantRow = Math.Max(0, GetRow(child));
            int wantCol = Math.Clamp(GetCol(child), 0, CurrentColumns - spanCols);

            var (row, col) = FindSlot(taken, wantRow, wantCol, spanCols, spanRows);
            Occupy(taken, row, col, spanCols, spanRows);
            _placed[child] = (row, col);
            rects[i] = CellRect(row, col, spanCols, spanRows);
            bottomRow = Math.Max(bottomRow, row + spanRows - 1);
        }

        return (new List<Rect>(rects), bottomRow);
    }

    private (int Row, int Col) FindSlot(List<bool[]> taken, int wantRow, int wantCol, int spanCols, int spanRows)
    {
        if (IsFree(taken, wantRow, wantCol, spanCols, spanRows)) return (wantRow, wantCol);

        for (int row = 0; ; row++)
            for (int col = 0; col + spanCols <= CurrentColumns; col++)
                if (IsFree(taken, row, col, spanCols, spanRows))
                    return (row, col);
    }

    private bool IsFree(List<bool[]> taken, int row, int col, int spanCols, int spanRows)
    {
        if (col < 0 || col + spanCols > CurrentColumns) return false;

        for (int r = row; r < row + spanRows; r++)
        {
            if (r >= taken.Count) continue;
            for (int c = col; c < col + spanCols; c++)
                if (taken[r][c]) return false;
        }
        return true;
    }

    private void Occupy(List<bool[]> taken, int row, int col, int spanCols, int spanRows)
    {
        while (taken.Count < row + spanRows) taken.Add(new bool[CurrentColumns]);

        for (int r = row; r < row + spanRows; r++)
            for (int c = col; c < col + spanCols; c++)
                taken[r][c] = true;
    }
}
