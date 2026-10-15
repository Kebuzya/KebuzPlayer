using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;

namespace KebuzPlayer.Interop;

public enum MediaButton { Play = 0, Pause = 1, Stop = 2, Next = 6, Previous = 7 }

public sealed unsafe class MediaSession : IDisposable
{
    private const string ClassName = "Windows.Media.SystemMediaTransportControls";

    private static readonly Guid IidInterop  = new("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a");
    private static readonly Guid IidControls = new("99fa3ff4-1742-42a6-902e-087d41f965ec");
    private static readonly Guid IidHandler  = new("0557e996-7b23-5bae-aa81-ea0d671143a4");
    private static readonly Guid IidUnknown  = new("00000000-0000-0000-c000-000000000046");
    private static readonly Guid IidAgile    = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");

    private const int SlotGetForWindow       = 6;
    private const int SlotGetPlaybackStatus  = 6;
    private const int SlotPutPlaybackStatus  = 7;
    private const int SlotGetDisplayUpdater  = 8;
    private const int SlotPutIsEnabled       = 11;
    private const int SlotGetIsPlayEnabled   = 12;
    private const int SlotPutIsPlayEnabled   = 13;
    private const int SlotPutIsPauseEnabled  = 17;
    private const int SlotPutIsPrevEnabled   = 25;
    private const int SlotPutIsNextEnabled   = 27;
    private const int SlotAddButtonPressed   = 32;
    private const int SlotRemoveButtonPressed = 33;
    private const int SlotUpdaterPutType     = 7;
    private const int SlotUpdaterMusic       = 12;
    private const int SlotUpdaterUpdate      = 17;
    private const int SlotMusicGetTitle      = 6;
    private const int SlotMusicPutTitle      = 7;
    private const int SlotMusicPutArtist     = 11;
    private const int SlotArgsGetButton      = 6;

    private const int MediaPlaybackTypeMusic = 1;

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr classId, in Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [StructLayout(LayoutKind.Sequential)]
    private struct HandlerObject
    {
        public void** Vtbl;
        public int    RefCount;
        public IntPtr Owner;
    }

    private static readonly void** HandlerVtbl = BuildHandlerVtbl();

    private readonly Action<MediaButton> _onButton;
    private IntPtr _controls;
    private IntPtr _updater;
    private IntPtr _handler;
    private GCHandle _self;
    private long _token;

    private MediaSession(Action<MediaButton> onButton) => _onButton = onButton;

    public static MediaSession? TryCreate(IntPtr hwnd, Action<MediaButton> onButton, Action<Exception>? onError = null)
    {
        var session = new MediaSession(onButton);
        try
        {
            session.Init(hwnd);
            return session;
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            session.Dispose();
            return null;
        }
    }

    private void Init(IntPtr hwnd)
    {
        IntPtr factory;
        var cls = HString(ClassName);
        try { Check(RoGetActivationFactory(cls, IidInterop, out factory)); }
        finally { WindowsDeleteString(cls); }

        try
        {
            IntPtr controls;
            var iid = IidControls;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)Slot(factory, SlotGetForWindow))(factory, hwnd, &iid, &controls));
            _controls = controls;
        }
        finally { Release(factory); }

        SetBool(SlotPutIsEnabled, true);
        SetBool(SlotPutIsPlayEnabled, true);
        SetBool(SlotPutIsPauseEnabled, true);
        SetBool(SlotPutIsPrevEnabled, true);
        SetBool(SlotPutIsNextEnabled, true);

        IntPtr updater;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(_controls, SlotGetDisplayUpdater))(_controls, &updater));
        _updater = updater;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(_updater, SlotUpdaterPutType))(_updater, MediaPlaybackTypeMusic));

        _self = GCHandle.Alloc(this);
        var obj = (HandlerObject*)NativeMemory.Alloc((nuint)sizeof(HandlerObject));
        obj->Vtbl = HandlerVtbl;
        obj->RefCount = 1;
        obj->Owner = GCHandle.ToIntPtr(_self);
        _handler = (IntPtr)obj;

        long token;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)Slot(_controls, SlotAddButtonPressed))(_controls, _handler, &token));
        _token = token;
    }

    public bool IsPlayEnabled
    {
        get
        {
            byte value;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)Slot(_controls, SlotGetIsPlayEnabled))(_controls, &value));
            return value != 0;
        }
    }

    public int PlaybackStatus
    {
        get
        {
            int value;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(_controls, SlotGetPlaybackStatus))(_controls, &value));
            return value;
        }
    }

    public string Title
    {
        get
        {
            var props = MusicProperties();
            try
            {
                IntPtr value;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(props, SlotMusicGetTitle))(props, &value));
                try
                {
                    var raw = WindowsGetStringRawBuffer(value, out uint length);
                    return raw == IntPtr.Zero ? "" : new string((char*)raw, 0, (int)length);
                }
                finally { WindowsDeleteString(value); }
            }
            finally { Release(props); }
        }
    }

    public void SetStatus(PlaybackState state)
    {
        if (_controls == IntPtr.Zero) return;
        int status = state switch
        {
            PlaybackState.Playing => 3,
            PlaybackState.Paused  => 4,
            _                     => 2,
        };
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(_controls, SlotPutPlaybackStatus))(_controls, status));
    }

    public void SetTrack(string title, string artist)
    {
        if (_updater == IntPtr.Zero) return;
        var props = MusicProperties();
        try
        {
            PutString(props, SlotMusicPutTitle, title);
            PutString(props, SlotMusicPutArtist, artist);
        }
        finally { Release(props); }
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(_updater, SlotUpdaterUpdate))(_updater));
    }

    private IntPtr MusicProperties()
    {
        IntPtr props;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(_updater, SlotUpdaterMusic))(_updater, &props));
        return props;
    }

    private static void PutString(IntPtr target, int slot, string value)
    {
        var h = HString(value);
        try { Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(target, slot))(target, h)); }
        finally { WindowsDeleteString(h); }
    }

    private void SetBool(int slot, bool value) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)Slot(_controls, slot))(_controls, value ? (byte)1 : (byte)0));

    private void Raise(MediaButton button)
    {
        try { _onButton(button); }
        catch { }
    }

    public void Dispose()
    {
        if (_controls != IntPtr.Zero)
        {
            if (_token != 0)
                ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Slot(_controls, SlotRemoveButtonPressed))(_controls, _token);
            ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)Slot(_controls, SlotPutIsEnabled))(_controls, 0);
        }
        Release(_updater);
        Release(_controls);
        Release(_handler);
        _updater = _controls = _handler = IntPtr.Zero;
        _token = 0;
    }

    private static IntPtr HString(string value)
    {
        Check(WindowsCreateString(value, value.Length, out var h));
        return h;
    }

    private static void* Slot(IntPtr obj, int index) => (*(void***)obj)[index];

    private static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero)
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(obj, 2))(obj);
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    private static void** BuildHandlerVtbl()
    {
        var vtbl = (void**)NativeMemory.Alloc((nuint)(4 * sizeof(void*)));
        vtbl[0] = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&HandlerQueryInterface;
        vtbl[1] = (delegate* unmanaged[Stdcall]<IntPtr, uint>)&HandlerAddRef;
        vtbl[2] = (delegate* unmanaged[Stdcall]<IntPtr, uint>)&HandlerRelease;
        vtbl[3] = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)&HandlerInvoke;
        return vtbl;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int HandlerQueryInterface(IntPtr self, Guid* iid, IntPtr* ppv)
    {
        if (*iid == IidUnknown || *iid == IidHandler || *iid == IidAgile)
        {
            *ppv = self;
            Interlocked.Increment(ref ((HandlerObject*)self)->RefCount);
            return 0;
        }
        *ppv = IntPtr.Zero;
        return unchecked((int)0x80004002);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint HandlerAddRef(IntPtr self) =>
        (uint)Interlocked.Increment(ref ((HandlerObject*)self)->RefCount);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint HandlerRelease(IntPtr self)
    {
        var obj = (HandlerObject*)self;
        int left = Interlocked.Decrement(ref obj->RefCount);
        if (left == 0)
        {
            var handle = GCHandle.FromIntPtr(obj->Owner);
            if (handle.IsAllocated) handle.Free();
            NativeMemory.Free(obj);
        }
        return (uint)left;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int HandlerInvoke(IntPtr self, IntPtr sender, IntPtr args)
    {
        try
        {
            int button;
            if (((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(args, SlotArgsGetButton))(args, &button) < 0)
                return 0;
            var owner = GCHandle.FromIntPtr(((HandlerObject*)self)->Owner).Target as MediaSession;
            owner?.Raise((MediaButton)button);
        }
        catch { }
        return 0;
    }
}
