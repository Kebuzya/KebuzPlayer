using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KebuzPlayer.Infrastructure;

namespace KebuzPlayer.Views;

public partial class VisualizerView : UserControl
{
    private readonly DispatcherTimer _buttonTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    public VisualizerView()
    {
        InitializeComponent();

        _buttonTimer.Tick += (_, _) =>
        {
            _buttonTimer.Stop();
            if (!Root.IsPointerOver)
                FullscreenButton.Classes.Remove("shown");
        };

        Root.PointerEntered += (_, _) => ShowButton(sticky: true);
        Root.PointerExited += (_, _) =>
        {
            _buttonTimer.Stop();
            FullscreenButton.Classes.Remove("shown");
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ShowButton(sticky: false);
    }

    private void ShowButton(bool sticky)
    {
        FullscreenButton.Classes.Add("shown");
        _buttonTimer.Stop();
        if (!sticky)
            _buttonTimer.Start();
    }

    private void Fullscreen_Click(object? sender, RoutedEventArgs e) =>
        ShellNotifier.RaiseVisualizerFullscreen();
}
