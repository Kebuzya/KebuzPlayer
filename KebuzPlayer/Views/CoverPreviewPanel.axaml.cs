using Avalonia.Controls;
using Avalonia.Input;
using KebuzPlayer.ViewModels;

namespace KebuzPlayer.Views;

public partial class CoverPreviewPanel : UserControl
{
    public CoverPreviewPanel()
    {
        InitializeComponent();
    }

    private void Variant_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is CoverPreviewViewModel vm &&
            (sender as Control)?.DataContext is CoverVariantItem item)
        {
            vm.SelectVariant(item);
            e.Handled = true;
        }
    }
}
