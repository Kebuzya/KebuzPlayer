using System;
using System.Reflection;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KebuzPlayer.Lang;
using KebuzPlayer.Services;

namespace KebuzPlayer.ViewModels;

public sealed class TabDef
{
    private readonly Func<ViewModelBase> _factory;
    private readonly string _titleKey;
    private ViewModelBase? _page;

    public string Title => Loc.T(_titleKey);
    public Geometry Icon { get; }
    public ViewModelBase Page => _page ??= _factory();

    public TabDef(string titleKey, Geometry icon, Func<ViewModelBase> factory)
    {
        _titleKey = titleKey;
        Icon = icon;
        _factory = factory;
    }

    public void DisposePage()
    {
        (_page as IDisposable)?.Dispose();
        _page = null;
    }
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly JsonSettingsService _settings;
    private readonly LibraryService _library;
    private readonly AudioService _audio;
    private readonly Infrastructure.CoverThumbCache _thumbs;

    [ObservableProperty]
    private TabDef[] _tabs;

    [ObservableProperty]
    private TabDef _selectedTab;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private bool _vizFullscreen;

    public string AppVersion { get; } = "v" + (Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? "0");

    public bool MaximizeOverTaskbar => _settings.Current.MaximizeOverTaskbar;

    public PlayerViewModel Player { get; }

    public UpdateViewModel Updates { get; }

    public MainWindowViewModel(JsonSettingsService settings, LibraryService library, AudioService audio)
    {
        _settings = settings;
        _library = library;
        _audio = audio;
        _thumbs = new Infrastructure.CoverThumbCache(library);
        Player = new PlayerViewModel(audio, library, settings, _thumbs);
        Updates = new UpdateViewModel(settings);

        _tabs = BuildTabs();
        var tabIndex = Math.Clamp(settings.Current.ActiveTabIndex, 0, _tabs.Length - 1);
        _selectedTab = _tabs[tabIndex];
        _currentPage = _tabs[tabIndex].Page;
        LanguageManager.Changed += OnLanguageChanged;
        _ = Updates.CheckAsync();
    }

    private void OnLanguageChanged()
    {
        var old = Tabs;
        int index = Math.Max(0, Array.IndexOf(old, SelectedTab));
        Tabs = BuildTabs();
        SelectedTab = Tabs[index];
        foreach (var tab in old)
            tab.DisposePage();
        Player.RefreshTexts();
        Updates.RefreshTexts();
    }

    private TabDef[] BuildTabs()
    {
        var library = _library;
        var audio = _audio;
        var settings = _settings;
        var thumbs = _thumbs;

        TabDef playlistsTab = null!;
        playlistsTab = new TabDef("Tab.Playlists",
            Geometry.Parse("M1,7 L7,7 7,9 3,9 3,15 9,15 9,11 11,11 11,17 1,17 Z M7,1 L17,1 17,11 7,11 Z M9,3 L15,3 15,9 9,9 Z"),
            () => new PlaylistsViewModel(library, audio, settings, thumbs));

        return
        [
            new("Tab.Home",
                Geometry.Parse("M2,11 L6,11 6,17 2,17 Z M7,6 L11,6 11,17 7,17 Z M12,1 L16,1 16,17 12,17 Z"),
                () => new HomeViewModel(library, audio, thumbs, settings, id => OpenPlaylist(playlistsTab, id))),
            new("Tab.Library",
                Geometry.Parse("M1,3 L17,3 17,5.4 1,5.4 Z M1,7.8 L12,7.8 12,10.2 1,10.2 Z M1,12.6 L15,12.6 15,15 1,15 Z"),
                () => new LibraryViewModel(library, audio, settings, thumbs)),
            playlistsTab,
            new("Tab.Visualizer",
                Geometry.Parse("M7,7 L11,7 11,11 7,11 Z M8,0 L10,0 10,5 8,5 Z M8,13 L10,13 10,18 8,18 Z M0,8 L5,8 5,10 0,10 Z M13,8 L18,8 18,10 13,10 Z"),
                () => new VisualizerViewModel()),
            new("Tab.Explorer",
                Geometry.Parse("M1,3 L7,3 8.5,5 17,5 17,15 1,15 Z"),
                () => new FolderExplorerViewModel(library, audio, settings)),
            new("Tab.Settings",
                Geometry.Parse("M3,9 A6,6 0 1 0 15,9 A6,6 0 1 0 3,9 Z M5.5,9 A3.5,3.5 0 1 0 12.5,9 A3.5,3.5 0 1 0 5.5,9 Z M8,0 L10,0 10,3 8,3 Z M8,15 L10,15 10,18 8,18 Z M0,8 L3,8 3,10 0,10 Z M15,8 L18,8 18,10 15,10 Z"),
                () => new SettingsViewModel(settings, library, Updates)),
        ];
    }

    private void OpenPlaylist(TabDef playlistsTab, int playlistId)
    {
        SelectedTab = playlistsTab;
        if (playlistsTab.Page is PlaylistsViewModel playlists)
            _ = playlists.OpenByIdAsync(playlistId);
    }

    partial void OnSelectedTabChanged(TabDef value)
    {
        if (value is null) return;
        CurrentPage = value.Page;
        _settings.Current.ActiveTabIndex = Array.IndexOf(Tabs, value);
    }
}
