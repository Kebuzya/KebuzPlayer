using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KebuzPlayer.Controls;
using KebuzPlayer.ViewModels;
using KebuzPlayer.Lang;

namespace KebuzPlayer.Views;

public partial class PlaylistsView : UserControl
{
    public PlaylistsView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is PlaylistsViewModel { Detail: null } vm)
            vm.ReloadCommand.Execute(null);
    }

    private async void Create_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PlaylistsViewModel vm || sender is not Control anchor) return;
        var name = await PromptFlyout.ShowAsync(anchor, Loc.T("Playlist.NamePrompt"));
        if (name != null)
            await vm.CreateAsync(name);
    }

    private async void Rename_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PlaylistsViewModel vm) return;
        if (sender is not MenuItem { DataContext: PlaylistCardViewModel card } ||
            ContextMenuOwner.Find(this, sender) is not { } anchor) return;
        var name = await PromptFlyout.ShowAsync(anchor, Loc.T("Playlists.RenamePrompt"), card.Name);
        if (name != null)
            await vm.RenameAsync(card, name);
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PlaylistsViewModel vm) return;
        if (sender is not MenuItem { DataContext: PlaylistCardViewModel card } ||
            ContextMenuOwner.Find(this, sender) is not { } anchor) return;
        var ok = await PromptFlyout.ConfirmAsync(anchor, Loc.F("Playlists.DeleteConfirm", card.Name));
        if (ok)
            await vm.DeleteAsync(card);
    }

    private void Card_Tapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PlaylistsViewModel vm &&
            sender is Border { DataContext: PlaylistCardViewModel card })
        {
            vm.Open(card);
        }
    }
}
