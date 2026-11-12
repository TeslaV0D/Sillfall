using System.Runtime.InteropServices;
using Weatherly.Core.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Weatherly.Platform.Shell;

/// <summary>
/// The icon in the notification area with its context menu. Create it on a thread with a message loop
/// (the app's UI thread): the icon talks to a hidden window of that thread. When Explorer restarts, the
/// icon adds itself again.
/// </summary>
public sealed partial class TrayIcon : IDisposable
{
    private const string WindowClass = "Weatherly.TrayIcon";
    private const uint IconId = 1;
    private const uint CallbackMessage = PInvoke.WM_APP + 1;

    // shellapi.h. Declared here instead of through CsWin32: its NOTIFYICONDATAW is architecture-specific
    // (packed differently on x86), and this library is AnyCPU. The app ships for x64 and ARM64 only.
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;
    private const uint NifShowTip = 0x80;
    private const uint NifInfo = 0x10;
    private const uint NiifUser = 0x4;
    private const uint NiifNoSound = 0x10;
    private const uint NiifRespectQuietTime = 0x80;
    private const uint NotifyIconVersion4 = 4;
    private const uint NinSelect = PInvoke.WM_USER + 0;
    private const uint NinKeySelect = NinSelect | 0x1;

    private readonly WNDPROC _windowProcedure;
    private readonly HINSTANCE _module;
    private readonly uint _taskbarCreated;
    private readonly HWND _window;
    private readonly HICON _icon;
    private readonly bool _ownsIcon;
    private string _tooltip;
    private bool _disposed;

    /// <param name="iconPath">An .ico file. Without one the default application icon is used.</param>
    /// <param name="tooltip">Text shown when hovering the icon.</param>
    /// <param name="menu">Builds the menu each time it opens.</param>
    /// <param name="activate">Runs on a left click (the default menu entry).</param>
    public TrayIcon(string? iconPath, string tooltip, Func<IReadOnlyList<TrayMenuItem>> menu, Action activate)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(activate);
        Menu = menu;
        Activate = activate;
        _tooltip = tooltip ?? string.Empty;
        _windowProcedure = WindowProcedure;
        _module = GetModule();
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        (_icon, _ownsIcon) = LoadIcon(iconPath);
        RegisterWindowClass();
        _window = CreateHiddenWindow();
    }

    /// <summary>Whether the shell accepted the icon.</summary>
    public bool IsShown { get; private set; }

    private Func<IReadOnlyList<TrayMenuItem>> Menu { get; }

    private Action Activate { get; }

    /// <summary>Adds the icon to the notification area. Returns whether the shell accepted it.</summary>
    public bool Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NotifyIconData data = CreateData();
        IsShown = Notify(NimAdd, ref data);
        if (IsShown)
        {
            data.Version = NotifyIconVersion4;
            Notify(NimSetVersion, ref data);
        }

        return IsShown;
    }

    /// <summary>Changes the hover text.</summary>
    public void SetTooltip(string tooltip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _tooltip = tooltip ?? string.Empty;
        if (IsShown)
        {
            NotifyIconData data = CreateData();
            Notify(NimModify, ref data);
        }
    }

    /// <summary>
    /// Shows a short notification at the icon (a toast on Windows 10 and 11), silent and held back during Focus or a
    /// full-screen app. Returns whether the shell took it.
    /// </summary>
    public unsafe bool ShowNotification(string title, string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsShown)
        {
            return false;
        }

        NotifyIconData data = CreateData();
        data.Flags |= NifInfo;
        data.InfoFlags = NiifUser | NiifNoSound | NiifRespectQuietTime;
        string info = (text ?? string.Empty).Length > 255 ? text![..255] : text ?? string.Empty;
        string caption = (title ?? string.Empty).Length > 63 ? title![..63] : title ?? string.Empty;
        info.AsSpan().CopyTo(new Span<char>(data.Info, 256));
        caption.AsSpan().CopyTo(new Span<char>(data.InfoTitle, 64));
        return Notify(NimModify, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (IsShown)
        {
            NotifyIconData data = CreateData();
            Notify(NimDelete, ref data);
            IsShown = false;
        }

        PInvoke.DestroyWindow(_window);
        UnregisterWindowClass();
        if (_ownsIcon)
        {
            PInvoke.DestroyIcon(_icon);
        }
    }

    private static unsafe HINSTANCE GetModule()
    {
        HMODULE module = PInvoke.GetModuleHandle((PCWSTR)null);
        return module.IsNull ? throw new Win32ErrorException("GetModuleHandle") : (HINSTANCE)module.Value;
    }

    private static unsafe (HICON Icon, bool Owned) LoadIcon(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            int size = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
            fixed (char* file = path)
            {
                HANDLE handle = PInvoke.LoadImage(HINSTANCE.Null, file, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_LOADFROMFILE);
                if (!handle.IsNull)
                {
                    return ((HICON)handle.Value, true);
                }
            }
        }

        // Shared system icon: never destroyed.
        return (PInvoke.LoadIcon(HINSTANCE.Null, PInvoke.IDI_APPLICATION), false);
    }

    private static unsafe bool Notify(uint message, ref NotifyIconData data)
    {
        fixed (NotifyIconData* pointer = &data)
        {
            return ShellNotifyIcon(message, pointer) != 0;
        }
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    private static unsafe partial int ShellNotifyIcon(uint message, NotifyIconData* data);

    private unsafe NotifyIconData CreateData()
    {
        var data = new NotifyIconData
        {
            Size = (uint)sizeof(NotifyIconData),
            Window = (nint)_window.Value,
            Id = IconId,
            Flags = NifMessage | NifIcon | NifTip | NifShowTip,
            CallbackMessage = CallbackMessage,
            Icon = (nint)_icon.Value,
        };

        // The tooltip buffer holds 127 characters and the terminator.
        string tip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip;
        tip.AsSpan().CopyTo(new Span<char>(data.Tip, 128));
        return data;
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
                throw new Win32ErrorException("RegisterClassEx (tray)");
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

    /// <summary>
    /// Hidden top-level window (not message-only: those miss the TaskbarCreated broadcast). Invisible, so
    /// the window tracking ignores it.
    /// </summary>
    private unsafe HWND CreateHiddenWindow()
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

            return window.IsNull ? throw new Win32ErrorException("CreateWindowEx (tray)") : window;
        }
    }

    private LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == CallbackMessage)
        {
            // Version 4: the event in the low word of lParam, the anchor point in wParam.
            uint notification = (uint)(lParam.Value & 0xFFFF);
            switch (notification)
            {
                case PInvoke.WM_CONTEXTMENU:
                    ShowMenu(window);
                    break;
                case NinSelect:
                case NinKeySelect:
                    Activate();
                    break;
            }

            return new LRESULT(0);
        }

        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            // Explorer restarted: the notification area is new and empty.
            Show();
            return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private unsafe void ShowMenu(HWND window)
    {
        var actions = new List<Action?>();
        HMENU menu = BuildMenu(Menu(), actions);
        try
        {
            PInvoke.GetCursorPos(out System.Drawing.Point cursor);

            // Without the foreground the menu would not close when clicking elsewhere.
            PInvoke.SetForegroundWindow(window);
            int command = PInvoke.TrackPopupMenuEx(
                menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY),
                cursor.X,
                cursor.Y,
                window,
                (TPMPARAMS*)null);
            PInvoke.PostMessage(window, PInvoke.WM_NULL, default, default);

            // Command ids start at 1: 0 means the menu was dismissed.
            if (command > 0 && command <= actions.Count)
            {
                actions[command - 1]?.Invoke();
            }
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }

    private static unsafe void Append(HMENU menu, MENU_ITEM_FLAGS flags, nuint id, string? text)
    {
        fixed (char* pointer = text)
        {
            if (!PInvoke.AppendMenu(menu, flags, id, pointer))
            {
                throw new Win32ErrorException("AppendMenu");
            }
        }
    }

    private static unsafe HMENU BuildMenu(IReadOnlyList<TrayMenuItem> items, List<Action?> actions)
    {
        HMENU menu = PInvoke.CreatePopupMenu();
        if (menu.IsNull)
        {
            throw new Win32ErrorException("CreatePopupMenu");
        }

        foreach (TrayMenuItem item in items)
        {
            if (item.IsSeparator)
            {
                Append(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
                continue;
            }

            MENU_ITEM_FLAGS flags = MENU_ITEM_FLAGS.MF_STRING;
            if (item.IsChecked)
            {
                flags |= MENU_ITEM_FLAGS.MF_CHECKED;
            }

            if (!item.IsEnabled)
            {
                flags |= MENU_ITEM_FLAGS.MF_GRAYED;
            }

            if (item.Children is { Count: > 0 } children)
            {
                HMENU submenu = BuildMenu(children, actions);
                Append(menu, flags | MENU_ITEM_FLAGS.MF_POPUP, (nuint)submenu.Value, item.Text);
                continue;
            }

            actions.Add(item.Invoke);
            uint id = (uint)actions.Count;
            Append(menu, flags, id, item.Text);
            if (item.IsRadio && item.IsChecked)
            {
                PInvoke.CheckMenuRadioItem(menu, id, id, id, (uint)MENU_ITEM_FLAGS.MF_BYCOMMAND);
            }

            if (item.IsDefault)
            {
                PInvoke.SetMenuDefaultItem(menu, id, 0);
            }
        }

        return menu;
    }

    /// <summary>NOTIFYICONDATAW with the natural 64-bit layout (976 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint Version;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }
}
