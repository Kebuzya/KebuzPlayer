using Avalonia.Controls;
using Avalonia.Input;
using KebuzPlayer.ViewModels;

namespace KebuzPlayer.Views;

public partial class EqualizerPanel : UserControl
{
    public EqualizerPanel()
    {
        InitializeComponent();
    }

    private void Band_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is EqualizerBandViewModel band)
        {
            band.Gain = 0;
            e.Handled = true;
        }
    }
}
