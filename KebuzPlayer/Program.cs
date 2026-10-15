using Avalonia;
using System;
using System.Linq;

namespace KebuzPlayer;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (Environment.ProcessPath is { } exePath)
        {
            if (args.Contains("--register-default-app"))
            {
                Infrastructure.DefaultAppRegistration.Register(exePath);
                return;
            }
            if (args.Contains("--unregister-default-app"))
            {
                if (Infrastructure.DefaultAppRegistration.IsRegisteredTo(exePath))
                    Infrastructure.DefaultAppRegistration.Unregister();
                return;
            }
        }
        App.LaunchFilePaths = args.Where(a => !a.StartsWith("--")).ToArray();
        if (!Interop.SingleInstance.TryAcquire())
        {
            Interop.SingleInstance.SignalExisting(App.LaunchFilePaths,
                System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime());
            return;
        }
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Interop.SingleInstance.Release();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
