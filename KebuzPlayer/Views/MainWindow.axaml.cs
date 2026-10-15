using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.ViewModels;
using KebuzPlayer.Lang;

namespace KebuzPlayer.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _vizHintTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private WindowState _stateBeforeViz = WindowState.Normal;
    private WindowState _restoreState = WindowState.Normal;

    public WindowState RestoreState => _restoreState;

    public WindowState PersistentState =>
        IsVizFullscreen ? _stateBeforeViz
        : WindowState == WindowState.Minimized ? _restoreState
        : WindowState;

    public MainWindow()
    {
        InitializeComponent();
        _vizHintTimer.Tick += (_, _) =>
        {
            _vizHintTimer.Stop();
            VizHintBox.Opacity = 0;
        };
        ShellNotifier.VisualizerFullscreenRequested += EnterVizFullscreen;
        Closed += (_, _) => ShellNotifier.VisualizerFullscreenRequested -= EnterVizFullscreen;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty
            && change.GetNewValue<WindowState>() is var state and not WindowState.Minimized)
            _restoreState = state;
    }

    private bool IsVizFullscreen => DataContext is MainWindowViewModel { VizFullscreen: true };

    private void EnterVizFullscreen()
    {
        if (DataContext is not MainWindowViewModel vm || vm.VizFullscreen) return;
        _stateBeforeViz = WindowState;
        WindowState = WindowState.FullScreen;
        vm.VizFullscreen = true;
        ShowVizHint();
    }

    private void ExitVizFullscreen()
    {
        if (DataContext is not MainWindowViewModel vm || !vm.VizFullscreen) return;
        vm.VizFullscreen = false;
        WindowState = _stateBeforeViz;
        _vizHintTimer.Stop();
    }

    private void ShowVizHint()
    {
        VizHintBox.Opacity = 1;
        _vizHintTimer.Stop();
        _vizHintTimer.Start();
    }

    private void VizFullscreen_DoubleTapped(object? sender, TappedEventArgs e) => ExitVizFullscreen();

    private void VizFullscreen_PointerMoved(object? sender, PointerEventArgs e) => ShowVizHint();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            e.Handled = true;
            if (IsVizFullscreen) ExitVizFullscreen();
            else EnterVizFullscreen();
            return;
        }
        if (e.Key == Key.Escape && IsVizFullscreen)
        {
            e.Handled = true;
            ExitVizFullscreen();
            return;
        }
        base.OnKeyDown(e);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
            ToggleMaximize();
        else
            BeginMoveDrag(e);
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        bool overTaskbar = DataContext is MainWindowViewModel vm && vm.MaximizeOverTaskbar;
        var target = overTaskbar ? WindowState.FullScreen : WindowState.Maximized;
        WindowState = WindowState == target ? WindowState.Normal : target;
    }

    private void Cover_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Player.OpenPreviewCommand.Execute(null);
    }

    private void NowPlayingPreviewCover_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Player.CurrentTrack == null) return;
        vm.Player.OpenPreviewCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => CoverButton.Flyout?.ShowAt(CoverButton));
    }

    private async void NowPlayingEditMetadata_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Player.CurrentTrack is not { } track) return;
        var edit = await Controls.MetadataFlyout.ShowAsync(CoverButton, track);
        if (edit != null)
            await vm.Player.EditCurrentTrackAsync(edit.FileName, edit.Title, edit.Artist, edit.Album);
    }

    private async void NowPlayingAddToPlaylist_SubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem parent || DataContext is not MainWindowViewModel vm ||
            vm.Player.CurrentTrack == null)
            return;

        var playlists = await vm.Player.GetPlaylistsAsync();
        parent.Items.Clear();
        foreach (var p in playlists)
        {
            var item = new MenuItem { Header = p.Name };
            var id = p.Id;
            item.Click += async (_, _) => await vm.Player.AddCurrentTrackToPlaylistAsync(id);
            parent.Items.Add(item);
        }
        if (playlists.Count > 0)
            parent.Items.Add(new Separator());
        var create = new MenuItem { Header = Loc.T("Playlist.NewMenu") };
        create.Click += async (_, _) =>
        {
            var name = await Controls.PromptFlyout.ShowAsync(CoverButton, Loc.T("Playlist.NamePrompt"));
            if (name != null)
                await vm.Player.CreatePlaylistWithCurrentTrackAsync(name);
        };
        parent.Items.Add(create);
    }

    private async void NowPlayingAddToLibrary_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.Player.AddCurrentTrackToLibraryAsync();
    }

    private void NowPlayingShowInFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Player.ShowCurrentTrackInFolder();
    }

    private async void NowPlayingHideTrack_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.Player.SetCurrentTrackHiddenAsync(hidden: true);
    }

    private async void NowPlayingUnhideTrack_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.Player.SetCurrentTrackHiddenAsync(hidden: false);
    }

    private async void NowPlayingDeleteTrack_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Player.CurrentTrack is not { } track) return;
        if (await Controls.PromptFlyout.ConfirmAsync(CoverButton, Loc.F("Confirm.DeleteOne", track.Title)))
            await vm.Player.DeleteCurrentTrackAsync();
    }

    private void Gain_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (e.Delta.Y > 0)
            vm.Player.GainUpCommand.Execute(null);
        else
            vm.Player.GainDownCommand.Execute(null);
        e.Handled = true;
    }
}
