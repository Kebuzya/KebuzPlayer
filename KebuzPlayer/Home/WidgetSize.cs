namespace KebuzPlayer.Home;

public readonly record struct WidgetSize(int Cols, int Rows)
{
    public string Label => $"{Cols}x{Rows}";
    public double PreviewWidth => Cols * 13;
    public double PreviewHeight => Rows * 13;
}
