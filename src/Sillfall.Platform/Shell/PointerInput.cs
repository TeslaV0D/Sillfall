using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Sillfall.Platform.Shell;

/// <summary>
/// Moves the real mouse, for the smoke test only: it drags a visitor into the box the way a user would, through the
/// hit windows and the system's input queue, so nothing of the interactive path is skipped.
/// </summary>
public static class PointerInput
{
    /// <summary>Puts the pointer at <paramref name="x"/>, <paramref name="y"/> (desktop pixels).</summary>
    public static bool MoveTo(int x, int y) => PInvoke.SetCursorPos(x, y);

    /// <summary>Presses (<see langword="true"/>) or releases the left button where the pointer is.</summary>
    public static bool LeftButton(bool down)
    {
        Span<INPUT> input = stackalloc INPUT[1];
        input[0] = new INPUT
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                mi = new MOUSEINPUT { dwFlags = down ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP },
            },
        };
        return PInvoke.SendInput(input, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>()) == 1;
    }
}
