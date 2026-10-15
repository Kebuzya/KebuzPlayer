using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KebuzPlayer.Data;
using KebuzPlayer.Infrastructure;
using KebuzPlayer.Services;
using KebuzPlayer.Themes;
using KebuzPlayer.ViewModels;
using KebuzPlayer.Views;
using KebuzPlayer.Lang;

namespace KebuzPlayer;

public partial class App : Application
{
    public static string[] LaunchFilePaths { get; set; } = [];

    private Database? _db;
    private AudioService? _audio;
    private JsonSettingsService? _settings;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            PathProvider.DetectPortableMode(AppContext.BaseDirectory);
            MainWindowViewModel? mainViewModel = null;
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                ErrorLog.Write(e.Exception, "ui");
                e.Handled = true;
                mainViewModel?.Player.ShowError(Loc.F("Common.ErrorWith", e.Exception.Message));
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                ErrorLog.Write(e.ExceptionObject as Exception, "fatal");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ErrorLog.Write(e.Exception, "task");
                e.SetObserved();
            };

            _settings = new JsonSettingsService(PathProvider.SettingsFilePath);
            _db = new Database(PathProvider.DatabasePath);
            _audio = new AudioService();
            var library = new LibraryService(_db) { Playback = _audio };

            var launches = new LaunchBatcher();
            void OpenFiles(IReadOnlyList<string> files, DateTime startedUtc)
            {
                var tracks = files.Select(LibraryService.LoadAdHocTrack).OfType<Models.Track>().ToList();
                if (tracks.Count == 0) return;
                if (launches.Joins(startedUtc) && _audio.CurrentTrack != null)
                {
                    foreach (var t in tracks) _audio.Enqueue(t);
                    return;
                }
                _audio.SetQueue(tracks, 0);
                _audio.Play(tracks[0]);
            }
            OpenFiles(LaunchFilePaths, System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime());

            var language = LanguageManager.Apply(_settings.Current.Language);
            if (_settings.Current.Language != language)
            {
                _settings.Current.Language = language;
                _settings.Save();
            }
            try
            {
                if (Environment.ProcessPath is { } exePath && DefaultAppRegistration.IsRegisteredTo(exePath))
                    DefaultAppRegistration.Register(exePath);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "default app");
            }
            ThemeManager.Apply(_settings.Current.Theme);
            Controls.KebuzComboBox.WheelSelectionEnabled = _settings.Current.WheelSelectsInDropdowns;
            Visualizer.VisualizerHub.Initialize(_audio, new AppSettingsService(_db), _settings.Current.OverlayVisualizerEnabled);

            _ = library.PurgeDetachedTracksAsync(_settings.Current.DetachedRetentionDays);

            var shellViewModel = new MainWindowViewModel(_settings, library, _audio);
            mainViewModel = shellViewModel;
            var window = new MainWindow
            {
                DataContext = shellViewModel,
                Width = _settings.Current.WindowWidth,
                Height = _settings.Current.WindowHeight,
            };

            desktop.MainWindow = window;

            var frame = new Interop.CaptionlessFrame(window);

            Interop.GlobalHotkeys? hotkeys = null;
            Interop.MediaSession? mediaSession = null;
            OverlayWindow? overlay = null;
            OverlayWindow Overlay() => overlay ??= new OverlayWindow
            {
                DataContext = new OverlayViewModel(shellViewModel.Player, _settings),
            };
            void OnMediaButton(Interop.MediaButton button)
            {
                switch (button)
                {
                    case Interop.MediaButton.Play when _audio.State != NAudio.Wave.PlaybackState.Playing:
                    case Interop.MediaButton.Pause when _audio.State == NAudio.Wave.PlaybackState.Playing:
                        _audio.PlayPause();
                        break;
                    case Interop.MediaButton.Next:
                        _audio.Next();
                        break;
                    case Interop.MediaButton.Previous:
                        _audio.Previous();
                        break;
                    default:
                        return;
                }
                Overlay().ShowTransient();
            }
            window.Opened += (_, _) =>
            {
                if (mediaSession == null && window.TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero)
                {
                    mediaSession = Interop.MediaSession.TryCreate(hwnd,
                        button => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnMediaButton(button)),
                        ex => ErrorLog.Write(ex, "media session"));
                    if (mediaSession is { } session)
                    {
                        _audio.TrackChanged += (_, t) => session.SetTrack(t.Title, t.Artist);
                        _audio.PlaybackStateChanged += (_, s) => session.SetStatus(s);
                        if (_audio.CurrentTrack is { } current)
                            session.SetTrack(current.Title, current.Artist);
                        session.SetStatus(_audio.State);
                    }
                }

                hotkeys ??= new Interop.GlobalHotkeys(window, id =>
                {
                    var osd = Overlay();
                    switch (id)
                    {
                        case Interop.GlobalHotkeys.OverlayId:
                            osd.ToggleVisible();
                            break;
                        case Interop.GlobalHotkeys.PreviousId:
                            _audio.Previous();
                            osd.ShowTransient();
                            break;
                        case Interop.GlobalHotkeys.PlayPauseId:
                            _audio.PlayPause();
                            osd.ShowTransient();
                            break;
                        case Interop.GlobalHotkeys.NextId:
                            _audio.Next();
                            osd.ShowTransient();
                            break;
                        case Interop.GlobalHotkeys.ActivateId:
                            OverlayWindow.ActivateMainWindow();
                            break;
                    }
                }, transportKeys: mediaSession == null);

                if (_settings.Current.WindowMaximized)
                    window.WindowState = _settings.Current.MaximizeOverTaskbar
                        ? WindowState.FullScreen
                        : WindowState.Maximized;
            };

            bool lastOverTaskbar = _settings.Current.MaximizeOverTaskbar;
            _settings.Saved += (_, _) =>
            {
                if (_settings.Current.MaximizeOverTaskbar == lastOverTaskbar) return;
                lastOverTaskbar = _settings.Current.MaximizeOverTaskbar;
                if (window.WindowState is not (WindowState.Maximized or WindowState.FullScreen)) return;
                window.WindowState = lastOverTaskbar ? WindowState.FullScreen : WindowState.Maximized;
            };

            Interop.SingleInstance.StartServer((files, startedUtc) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    OpenFiles(files, startedUtc);
                    OverlayWindow.ActivateMainWindow();
                }));

            desktop.Exit += (_, _) =>
            {
                overlay?.Close();
                hotkeys?.Dispose();
                mediaSession?.Dispose();
                frame.Dispose();
                var lastState = window.PersistentState;
                _settings.Current.WindowMaximized = lastState is WindowState.Maximized or WindowState.FullScreen;
                if (lastState == WindowState.Normal && window.WindowState == WindowState.Normal)
                {
                    _settings.Current.WindowWidth = window.Width;
                    _settings.Current.WindowHeight = window.Height;
                }
                if (_audio.CurrentTrack != null)
                    _settings.Current.LastPositionSeconds = _audio.Position.TotalSeconds;
                _settings.Save();
                _audio.Dispose();
                _db.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
