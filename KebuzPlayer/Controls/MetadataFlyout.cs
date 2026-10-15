using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Track = KebuzPlayer.Models.Track;
using KebuzPlayer.Lang;

namespace KebuzPlayer.Controls;

public sealed record MetadataEdit(string FileName, string Title, string Artist, string Album);

public static class MetadataFlyout
{
    public static Task<MetadataEdit?> ShowAsync(Control anchor, Track track)
    {
        var tcs = new TaskCompletionSource<MetadataEdit?>();
        var fileName = Field(System.IO.Path.GetFileName(track.Path));
        var title    = Field(track.Title);
        var artist   = Field(track.Artist);
        var album    = Field(track.Album);
        var ok       = MakeButton(Loc.T("Common.Save"), "primary");
        var cancel   = MakeButton(Loc.T("Common.Cancel"), "ghost");

        var flyout = BuildFlyout(Loc.T("Meta.Title"),
        [
            Labeled(Loc.T("Meta.FileName"), fileName),
            Labeled(Loc.T("Meta.TrackTitle"), title),
            Labeled(Loc.T("Meta.Artist"), artist),
            Labeled(Loc.T("Meta.Album"), album),
            Buttons(ok, cancel),
        ]);

        void Commit()
        {
            tcs.TrySetResult(new MetadataEdit(
                fileName.Text?.Trim() ?? "",
                title.Text?.Trim() ?? "",
                artist.Text?.Trim() ?? "",
                album.Text?.Trim() ?? ""));
            flyout.Hide();
        }

        ok.Click += (_, _) => Commit();
        cancel.Click += (_, _) => flyout.Hide();
        AttachKeys(fileName, Commit, flyout);
        AttachKeys(title, Commit, flyout);
        AttachKeys(artist, Commit, flyout);
        AttachKeys(album, Commit, flyout);
        flyout.Closed += (_, _) => tcs.TrySetResult(null);
        flyout.Opened += (_, _) => { title.Focus(); title.SelectAll(); };
        flyout.ShowAt(anchor);
        return tcs.Task;
    }

    public static Task<(string Artist, string Album)?> ShowBatchAsync(Control anchor, int count)
    {
        var tcs = new TaskCompletionSource<(string, string)?>();
        var artist = Field("");
        var album  = Field("");
        var ok     = MakeButton(Loc.T("Common.Save"), "primary");
        var cancel = MakeButton(Loc.T("Common.Cancel"), "ghost");

        var flyout = BuildFlyout(Loc.F("Meta.BatchTitle", count),
        [
            Labeled(Loc.T("Meta.Artist"), artist),
            Labeled(Loc.T("Meta.Album"), album),
            Buttons(ok, cancel),
        ]);

        void Commit()
        {
            tcs.TrySetResult((artist.Text?.Trim() ?? "", album.Text?.Trim() ?? ""));
            flyout.Hide();
        }

        ok.Click += (_, _) => Commit();
        cancel.Click += (_, _) => flyout.Hide();
        AttachKeys(artist, Commit, flyout);
        AttachKeys(album, Commit, flyout);
        flyout.Closed += (_, _) => tcs.TrySetResult(null);
        flyout.Opened += (_, _) => artist.Focus();
        flyout.ShowAt(anchor);
        return tcs.Task;
    }

    private static void AttachKeys(TextBox box, System.Action commit, Flyout flyout)
    {
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { flyout.Hide(); e.Handled = true; }
        };
    }

    private static TextBox Field(string initial) =>
        new() { Text = initial, Width = 300, FontSize = 13 };

    private static StackPanel Labeled(string caption, Control field)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = caption, FontSize = 11, Opacity = 0.7 });
        panel.Children.Add(field);
        return panel;
    }

    private static Flyout BuildFlyout(string title, Control[] rows)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(4), MaxWidth = 340 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
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
