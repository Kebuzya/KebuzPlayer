using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Win32;

namespace KebuzPlayer.Interop;

public sealed class CaptionlessFrame : IDisposable
{
    private const uint WmNcCalcSize = 0x0083;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NcCalcSizeParams
    {
        public Rect Target, Source, Client;
        public IntPtr Position;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter,
        int x, int y, int cx, int cy, uint flags);

    private readonly Window _window;
    private bool _registered;

    public CaptionlessFrame(Window window)
    {
        _window = window;
        Win32Properties.AddWndProcHookCallback(window, WndProcHook);
        _registered = true;
        Recalculate();
        window.Opened += (_, _) => Recalculate();
    }

    private void Recalculate()
    {
        if (_window.TryGetPlatformHandle()?.Handle is not { } hWnd || hWnd == IntPtr.Zero) return;
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcCalcSize || wParam == IntPtr.Zero) return IntPtr.Zero;

        var proposed = Marshal.PtrToStructure<NcCalcSizeParams>(lParam).Target;
        DefWindowProc(hWnd, msg, wParam, lParam);

        var computed = Marshal.PtrToStructure<NcCalcSizeParams>(lParam);
        int border = computed.Target.Left - proposed.Left;
        computed.Target.Top = IsZoomed(hWnd) ? proposed.Top + border : proposed.Top;
        Marshal.StructureToPtr(computed, lParam, false);

        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_registered) return;
        _registered = false;
        Win32Properties.RemoveWndProcHookCallback(_window, WndProcHook);
    }
}
