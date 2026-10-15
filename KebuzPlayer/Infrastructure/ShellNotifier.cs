using System;

namespace KebuzPlayer.Infrastructure;

public static class ShellNotifier
{
    public static event Action? VisualizerFullscreenRequested;

    public static void RaiseVisualizerFullscreen() => VisualizerFullscreenRequested?.Invoke();
}
