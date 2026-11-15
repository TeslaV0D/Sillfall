using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sillfall.Platform.Overlay;

/// <summary>
/// Waits between frames with a high-resolution waitable timer (finding F-06). <c>Thread.Sleep(1)</c> sleeps a whole
/// system timer tick (15.6 ms by default), which turned the 60 fps cap into ~49 fps and "the display's rate" into 64 fps
/// on a 180 Hz monitor, unless another app happened to raise the system timer resolution. This timer is precise on its
/// own and does not change the resolution for the whole system (no <c>timeBeginPeriod</c>).
/// <para>
/// The wait also ends when a message arrives for the thread, so window events and commands are never held up by it.
/// </para>
/// </summary>
internal sealed class FrameWaiter : IDisposable
{
    // Access rights and the infinite timeout (winnt.h, winbase.h), as numbers: CsWin32 offers them only as loose constants.
    private const uint TimerModifyState = 0x0002;
    private const uint Synchronize = 0x0010_0000;
    private const uint Infinite = 0xFFFF_FFFF;

    private readonly SafeFileHandle _timer;

    public FrameWaiter()
    {
        // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION exists since Windows 10 1803; the minimum OS is 2004.
        _timer = PInvoke.CreateWaitableTimerEx(
            null,
            (string?)null,
            PInvoke.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
            TimerModifyState | Synchronize);
        if (_timer.IsInvalid)
        {
            throw new Win32ErrorException("CreateWaitableTimerEx");
        }
    }

    /// <summary>Waits up to <paramref name="seconds"/>, or less if a message arrives.</summary>
    public unsafe void Wait(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        // Relative due time: negative, in 100 ns units.
        long dueTime = -Math.Max(1L, (long)(seconds * 10_000_000));
        if (!PInvoke.SetWaitableTimer(_timer, in dueTime, 0, null, null, false))
        {
            Thread.Sleep(1);
            return;
        }

        var timer = (HANDLE)_timer.DangerousGetHandle();
        PInvoke.MsgWaitForMultipleObjectsEx(
            1,
            &timer,
            Infinite,
            QUEUE_STATUS_FLAGS.QS_ALLINPUT,
            MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);
    }

    public void Dispose() => _timer.Dispose();
}
