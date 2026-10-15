using System.Runtime.InteropServices;

namespace KebuzPlayer.Interop;

public static class TimerResolution
{
    private const uint PeriodMs = 1;

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);

    private static bool _raised;

    public static void Raise()
    {
        if (_raised) return;
        _raised = timeBeginPeriod(PeriodMs) == 0;
    }

    public static void Release()
    {
        if (!_raised) return;
        timeEndPeriod(PeriodMs);
        _raised = false;
    }
}
