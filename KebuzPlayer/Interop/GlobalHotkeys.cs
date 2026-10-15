using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Win32;

namespace KebuzPlayer.Interop;

public sealed class GlobalHotkeys : IDisposable
{
    public const int OverlayId = 1;
    public const int PreviousId = 2;
    public const int PlayPauseId = 3;
    public const int NextId = 4;
    public const int ActivateId = 5;

    private const uint WmHotkey = 0x0312;

    private const uint VkMediaStop = 0xB2;
    private const uint VkMediaPrevTrack = 0xB1;
    private const uint VkMediaPlayPause = 0xB3;
    private const uint VkMediaNextTrack = 0xB0;
    private const uint VkLaunchMediaSelect = 0xB5;

    private static readonly Dictionary<int, uint> IdToVk = new()
    {
        [OverlayId] = VkMediaStop,
        [PreviousId] = VkMediaPrevTrack,
        [PlayPauseId] = VkMediaPlayPause,
        [NextId] = VkMediaNextTrack,
        [ActivateId] = VkLaunchMediaSelect,
    };

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private readonly IntPtr _handle;
    private readonly Action<int> _handler;
    private bool _registered;

    public GlobalHotkeys(Window window, Action<int> handler, bool transportKeys)
    {
        _window = window;
        _handler = handler;
        _handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_handle == IntPtr.Zero) return;

        Win32Properties.AddWndProcHookCallback(window, WndProcHook);
        foreach (var (id, vk) in IdToVk)
            if (transportKeys || id is OverlayId or ActivateId)
                RegisterHotKey(_handle, id, 0, vk);
        _registered = true;
    }

    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            int id = wParam.ToInt32();
            if (id is >= OverlayId and <= ActivateId)
            {
                _handler(id);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_registered) return;
        _registered = false;
        foreach (var id in IdToVk.Keys)
            UnregisterHotKey(_handle, id);
        Win32Properties.RemoveWndProcHookCallback(_window, WndProcHook);
    }
}
