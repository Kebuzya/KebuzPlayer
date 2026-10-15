using Avalonia;
using Avalonia.Controls;
using KebuzPlayer.ViewModels;

namespace KebuzPlayer.Controls;

public partial class TrackCardControl : UserControl
{
    private TrackRow? _row;

    public TrackCardControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_row == null || ReferenceEquals(_row, DataContext)) return;
            _row.OnCardDetached();
            _row = DataContext as TrackRow;
            _row?.OnCardAttached();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _row = DataContext as TrackRow;
        _row?.OnCardAttached();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _row?.OnCardDetached();
        _row = null;
    }
}
