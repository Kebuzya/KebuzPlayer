using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using KebuzPlayer.Services;

namespace KebuzPlayer.Visualizer;

public sealed class VisualizerHub
{
    public static VisualizerHub? Instance { get; private set; }

    public VisualizerSettings    Settings { get; } = new();
    public VisualizerFrameSource Frames   { get; }

    private readonly AppSettingsService _store;
    private DispatcherTimer? _saveTimer;

    private VisualizerHub(AudioService audio, AppSettingsService store)
    {
        _store = store;
        Frames = new VisualizerFrameSource(audio, Settings);
    }

    public static void Initialize(AudioService audio, AppSettingsService store, bool overlayEnabledSeed)
    {
        var hub = new VisualizerHub(audio, store);
        Instance = hub;
        _ = hub.LoadAsync(overlayEnabledSeed);
    }

    private async Task LoadAsync(bool overlayEnabledSeed)
    {
        try
        {
            await _store.LoadVisualizerSettingsAsync(Settings, overlayEnabledSeed);
        }
        catch
        {
            return;
        }
        Dispatcher.UIThread.Post(() => Settings.NotifyChanged());
    }

    public void NotifyChanged()
    {
        Settings.NotifyChanged();
        RequestSave();
    }

    public void RequestSave()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _saveTimer.Tick += async (_, _) =>
            {
                _saveTimer.Stop();
                try { await _store.SaveVisualizerSettingsAsync(Settings); } catch { }
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
