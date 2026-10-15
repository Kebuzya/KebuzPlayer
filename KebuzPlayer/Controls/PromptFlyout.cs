using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using KebuzPlayer.Lang;

namespace KebuzPlayer.Controls;

public static class PromptFlyout
{
    public static Task<string?> ShowAsync(Control anchor, string title, string initial = "")
    {
        var tcs = new TaskCompletionSource<string?>();
        var box = new TextBox { Text = initial, Width = 240, FontSize = 13 };
        var ok = MakeButton(Loc.T("Common.Ok"), "primary");
        var cancel = MakeButton(Loc.T("Common.Cancel"), "ghost");
        var flyout = BuildFlyout(title, [box, Buttons(ok, cancel)]);

        void Commit()
        {
            var text = box.Text?.Trim();
            tcs.TrySetResult(string.IsNullOrEmpty(text) ? null : text);
            flyout.Hide();
        }

        ok.Click += (_, _) => Commit();
        cancel.Click += (_, _) => flyout.Hide();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { flyout.Hide(); e.Handled = true; }
        };
        flyout.Closed += (_, _) => tcs.TrySetResult(null);
        flyout.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        flyout.ShowAt(anchor);
        return tcs.Task;
    }

    public static Task<bool> ConfirmAsync(Control anchor, string message, string? action = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        var yes = MakeButton(action ?? Loc.T("Common.Delete"), "primary");
        var no = MakeButton(Loc.T("Common.Cancel"), "ghost");
        var flyout = BuildFlyout(message, [Buttons(yes, no)]);

        yes.Click += (_, _) => { tcs.TrySetResult(true); flyout.Hide(); };
        no.Click += (_, _) => flyout.Hide();
        flyout.Closed += (_, _) => tcs.TrySetResult(false);
        flyout.ShowAt(anchor);
        return tcs.Task;
    }

    private static Flyout BuildFlyout(string title, Control[] rows)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(4), MaxWidth = 280 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        foreach (var row in rows)
            panel.Children.Add(row);
        return new Flyout { Content = panel, Placement = PlacementMode.Bottom };
    }

    private static Button MakeButton(string text, string cls)
    {
        var button = new Button { Content = text, FontSize = 12 };
        button.Classes.Add(cls);
        return button;
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        foreach (var b in buttons)
            row.Children.Add(b);
        return row;
    }
}
