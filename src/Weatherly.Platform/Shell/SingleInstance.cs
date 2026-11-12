using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.DataExchange;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Weatherly.Platform.Shell;

/// <summary>
/// One app per user session (PLAN.md, section 7: single instance). The first start claims a named mutex and
/// opens a hidden message window; later starts find that window, hand over their command line (for example a
/// <c>weatherly://</c> link) with WM_COPYDATA and exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Weatherly.SingleInstance";
    private const string WindowClass = "Weatherly.Instance";

    /// <summary>Marks our WM_COPYDATA messages ('WL').</summary>
    private const nuint MessageTag = 0x574C;

    private readonly Mutex _mutex;
    private readonly WNDPROC _windowProcedure;
    private readonly HINSTANCE _module;
    private readonly HWND _window;
    private bool _disposed;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _windowProcedure = WindowProcedure;
        _module = GetModule();
        RegisterWindowClass();
        _window = CreateMessageWindow();
    }

    /// <summary>Raised on the UI thread with the command line a later start handed over (may be empty).</summary>
    public event EventHandler<string>? MessageReceived;

    /// <summary>
    /// Claims the session for this process. Returns <see langword="null"/> when another instance already runs.
    /// Call on the UI thread: the message window lives there.
    /// </summary>
    public static SingleInstance? TryClaim()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex);
    }

    /// <summary>Hands <paramref name="message"/> to the running instance. Returns whether it got there.</summary>
    public static unsafe bool SendToRunning(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        HWND target;
        fixed (char* className = WindowClass)
        {
            target = PInvoke.FindWindow(className, null);
        }

        if (target.IsNull)
        {
            return false;
        }

        // Lets the running instance bring its window to the front: this process was started by the user.
        PInvoke.AllowSetForegroundWindow(Shell.SystemInfo.ProcessIdOf((nint)target.Value));
        fixed (char* text = message)
        {
            var data = new COPYDATASTRUCT
            {
                dwData = MessageTag,
                cbData = (uint)((message.Length + 1) * sizeof(char)),
                lpData = text,
            };
            nuint result;
            return PInvoke.SendMessageTimeout(target, PInvoke.WM_COPYDATA, default, (nint)(&data), SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, 5000, &result) != 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        PInvoke.DestroyWindow(_window);
        UnregisterWindowClass();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private static unsafe HINSTANCE GetModule()
    {
        HMODULE module = PInvoke.GetModuleHandle((PCWSTR)null);
        return module.IsNull ? throw new Win32ErrorException("GetModuleHandle") : (HINSTANCE)module.Value;
    }

    private unsafe void RegisterWindowClass()
    {
        fixed (char* className = WindowClass)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _windowProcedure,
                hInstance = _module,
                lpszClassName = className,
            };

            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new Win32ErrorException("RegisterClassEx (instance)");
            }
        }
    }

    private unsafe void UnregisterWindowClass()
    {
        fixed (char* className = WindowClass)
        {
            PInvoke.UnregisterClass(className, _module);
        }
    }

    /// <summary>Hidden top-level window: FindWindow does not see message-only windows.</summary>
    private unsafe HWND CreateMessageWindow()
    {
        fixed (char* className = WindowClass)
        {
            HWND window = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                className,
                null,
                WINDOW_STYLE.WS_POPUP,
                0,
                0,
                0,
                0,
                HWND.Null,
                HMENU.Null,
                _module,
                null);

            return window.IsNull ? throw new Win32ErrorException("CreateWindowEx (instance)") : window;
        }
    }

    private unsafe LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == PInvoke.WM_COPYDATA && lParam.Value != 0)
        {
            var data = (COPYDATASTRUCT*)lParam.Value;
            if (data->dwData == MessageTag && data->cbData <= 64 * 1024)
            {
                int length = (int)(data->cbData / sizeof(char));
                string text = new string((char*)data->lpData, 0, length).TrimEnd('\0');
                MessageReceived?.Invoke(this, text);
                return new LRESULT(1);
            }
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}
