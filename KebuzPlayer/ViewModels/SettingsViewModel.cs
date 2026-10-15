using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KebuzPlayer.Controls;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Services;
using KebuzPlayer.Themes;
using KebuzPlayer.Lang;

namespace KebuzPlayer.ViewModels;

public sealed record OverlayStyleOption(OverlayStyle Value, string Key)
{
    public string DisplayName => Loc.T(Key);
}

public sealed record FileTransferModeOption(FileTransferMode Value, string Key)
{
    public string DisplayName => Loc.T(Key);
}

public sealed record VizFrameRateOption(int Value, string Key)
{
    public string DisplayName => Loc.T(Key);
}

public sealed record RetentionOption(int Days, string Key)
{
    public string DisplayName => Loc.T(Key);
}

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    public void Dispose()
    {
        LibraryViz?.Detach();
        TabViz?.Detach();
        OverlayViz?.Detach();
        HomeViz?.Detach();
    }

    private readonly JsonSettingsService _settings;
    private readonly LibraryService _library;

    public RetentionOption[] RetentionOptions { get; } =
    [
        new(30,  "Retention.30"),
        new(90,  "Retention.90"),
        new(365, "Retention.365"),
        new(0,   "Retention.Forever"),
    ];

    [ObservableProperty]
    private RetentionOption _selectedRetention;

    [ObservableProperty]
    private string _detachedSummary = "";

    public ThemeInfo[] Themes => ThemeManager.Themes;

    [ObservableProperty]
    private ThemeInfo _selectedTheme;

    [ObservableProperty]
    private bool _wheelDropdowns;

    [ObservableProperty]
    private bool _maximizeOverTaskbar;

    [ObservableProperty]
    private bool _checkForUpdates;

    public LanguageInfo[] Languages { get; } = LanguageManager.Languages;

    [ObservableProperty]
    private LanguageInfo _selectedLanguage;

    public UpdateViewModel Updates { get; }

    [ObservableProperty]
    private bool _isRegisteredWithWindows;

    public OverlayStyleOption[] OverlayStyleOptions { get; } =
    [
        new(OverlayStyle.Modest, "OverlayStyle.Modest"),
        new(OverlayStyle.Vivid, "OverlayStyle.Vivid"),
    ];

    [ObservableProperty]
    private OverlayStyleOption _selectedOverlayStyle;

    public int[] PreviewBatchOptions { get; } = [1, 4, 9, 16];

    [ObservableProperty]
    private int _selectedPreviewBatch;

    public FileTransferModeOption[] FileTransferModeOptions { get; } =
    [
        new(FileTransferMode.Copy, "Transfer.Copy"),
        new(FileTransferMode.Move, "Transfer.Move"),
    ];

    [ObservableProperty]
    private FileTransferModeOption _selectedFileTransferMode;

    public bool IsPortableMode => PathProvider.IsPortable;

    public VizFrameRateOption[] FrameRateOptions { get; } =
    [
        new(15,  "Fps.15"),
        new(24,  "Fps.24"),
        new(30,  "Fps.30"),
        new(45,  "Fps.45"),
        new(60,  "Fps.60"),
        new(90,  "Fps.90"),
        new(120, "Fps.120"),
        new(0,   "Fps.Monitor"),
    ];

    [ObservableProperty]
    private VizFrameRateOption _selectedFrameRate;

    public VizPanelSettingsViewModel? LibraryViz { get; }
    public VizPanelSettingsViewModel? TabViz     { get; }
    public VizPanelSettingsViewModel? OverlayViz { get; }
    public VizPanelSettingsViewModel? HomeViz    { get; }

    public SettingsViewModel(JsonSettingsService settings, LibraryService library, UpdateViewModel updates)
    {
        _settings = settings;
        _library = library;
        Updates = updates;
        _checkForUpdates = settings.Current.CheckForUpdates;
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == LanguageManager.Current) ?? Languages[0];
        _selectedRetention = RetentionOptions.FirstOrDefault(o => o.Days == settings.Current.DetachedRetentionDays)
            ?? RetentionOptions[1];
        _ = RefreshDetachedSummaryAsync();
        if (Visualizer.VisualizerHub.Instance is { } hub)
        {
            LibraryViz = new VizPanelSettingsViewModel(hub, hub.Settings.Library, VizFormFactors.WideStrip, Loc.T("VizPanel.Library"));
            TabViz     = new VizPanelSettingsViewModel(hub, hub.Settings.Tab,     VizFormFactors.Full,      Loc.T("VizPanel.Tab"));
            OverlayViz = new VizPanelSettingsViewModel(hub, hub.Settings.Overlay, VizFormFactors.Compact,   Loc.T("VizPanel.Overlay"));
            HomeViz    = new VizPanelSettingsViewModel(hub, hub.Settings.Home,    VizFormFactors.WideStrip, Loc.T("VizPanel.Home"));
        }
        _selectedFrameRate = FrameRateOptions.FirstOrDefault(
            o => o.Value == (Visualizer.VisualizerHub.Instance?.Settings.FrameRate ?? 30))
            ?? FrameRateOptions[2];
        _selectedTheme = Themes.FirstOrDefault(t => t.Id == settings.Current.Theme) ?? Themes[0];
        _wheelDropdowns = settings.Current.WheelSelectsInDropdowns;
        _maximizeOverTaskbar = settings.Current.MaximizeOverTaskbar;
        _isRegisteredWithWindows = DefaultAppRegistration.IsRegistered();
        _selectedOverlayStyle = OverlayStyleOptions.First(o => o.Value == settings.Current.OverlayStyle);
        _selectedPreviewBatch = PreviewBatchOptions.Contains(settings.Current.CoverPreviewBatch)
            ? settings.Current.CoverPreviewBatch
            : 9;
        _selectedFileTransferMode = FileTransferModeOptions.First(o => o.Value == settings.Current.FileTransferMode);
    }

    partial void OnSelectedFrameRateChanged(VizFrameRateOption value)
    {
        if (Visualizer.VisualizerHub.Instance is not { } hub) return;
        hub.Settings.FrameRate = value.Value;
        hub.NotifyChanged();
    }

    partial void OnSelectedRetentionChanged(RetentionOption value)
    {
        _settings.Current.DetachedRetentionDays = value.Days;
        _settings.Save();
    }

    [RelayCommand]
    private async Task PurgeDetachedAsync()
    {
        await _library.PurgeAllDetachedTracksAsync();
        await RefreshDetachedSummaryAsync();
    }

    private async Task RefreshDetachedSummaryAsync()
    {
        try
        {
            var (count, bytes) = await _library.GetDetachedSummaryAsync();
            DetachedSummary = count == 0
                ? Loc.T("Settings.NoDetached")
                : Loc.F("Settings.DetachedSummary", count, (bytes / 1024 / 1024.0).ToString("0.0"));
        }
        catch
        {
            DetachedSummary = "";
        }
    }

    partial void OnSelectedThemeChanged(ThemeInfo value)
    {
        ThemeManager.Apply(value.Id);
        _settings.Current.Theme = value.Id;
        _settings.Save();
    }

    partial void OnWheelDropdownsChanged(bool value)
    {
        KebuzComboBox.WheelSelectionEnabled = value;
        _settings.Current.WheelSelectsInDropdowns = value;
        _settings.Save();
    }

    partial void OnMaximizeOverTaskbarChanged(bool value)
    {
        _settings.Current.MaximizeOverTaskbar = value;
        _settings.Save();
    }

    partial void OnSelectedLanguageChanged(LanguageInfo value)
    {
        if (value is null || value.Code == LanguageManager.Current) return;
        _settings.Current.Language = value.Code;
        _settings.Save();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LanguageManager.Apply(value.Code));
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        _settings.Current.CheckForUpdates = value;
        _settings.Save();
        Updates.Refresh();
        _ = Updates.CheckAsync();
    }

    partial void OnSelectedOverlayStyleChanged(OverlayStyleOption value)
    {
        _settings.Current.OverlayStyle = value.Value;
        _settings.Save();
    }

    partial void OnSelectedPreviewBatchChanged(int value)
    {
        _settings.Current.CoverPreviewBatch = value;
        _settings.Save();
    }

    partial void OnSelectedFileTransferModeChanged(FileTransferModeOption value)
    {
        _settings.Current.FileTransferMode = value.Value;
        _settings.Save();
    }

    [RelayCommand]
    private void ToggleWindowsRegistration()
    {
        try
        {
            if (IsRegisteredWithWindows)
                DefaultAppRegistration.Unregister();
            else if (Environment.ProcessPath is { } exePath)
                DefaultAppRegistration.Register(exePath);
        }
        catch { }
        IsRegisteredWithWindows = DefaultAppRegistration.IsRegistered();
    }
}
