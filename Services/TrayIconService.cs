using System.Runtime.InteropServices;
using NeZha_Desktop.Contracts;
using WinRT.Interop;

namespace NeZha_Desktop.Services;

public sealed class TrayIconService : ITrayIconService
{
    private const int GwlWndProc = -4;
    private const uint WmApp = 0x8000;
    private const uint WmTrayIcon = WmApp + 101;
    private const uint WmCommand = 0x0111;
    private const uint WmContextMenu = 0x007B;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmLButtonDblClk = 0x0203;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;

    private const uint MfString = 0x00000000;
    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmRightButton = 0x0002;

    private const uint OpenMenuId = 1001;
    private const uint CloseMenuId = 1002;

    private MainWindow? _window;
    private nint _hwnd;
    private nint _hMenu;
    private nint _oldWndProc;
    private nint _trayIconHandle;
    private WndProcDelegate? _wndProcDelegate;
    private bool _initialized;

    public void Initialize(MainWindow window)
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            _window = window;
            _hwnd = WindowNative.GetWindowHandle(window);
            if (_hwnd == nint.Zero)
            {
                return;
            }

            _hMenu = CreatePopupMenu();
            _ = AppendMenu(_hMenu, MfString, OpenMenuId, "打开");
            _ = AppendMenu(_hMenu, MfString, CloseMenuId, "关闭");

            _wndProcDelegate = WndProc;
            _oldWndProc = SetWindowLongPtr(_hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            _trayIconHandle = LoadAppIconHandle();

            var data = CreateNotifyIconData();
            _ = Shell_NotifyIcon(NimAdd, ref data);
            data.uVersion = NotifyIconVersion4;
            _ = Shell_NotifyIcon(NimSetVersion, ref data);

            _initialized = true;
        }
        catch
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (!_initialized)
        {
            return;
        }

        var data = CreateNotifyIconData();
        _ = Shell_NotifyIcon(NimDelete, ref data);

        if (_oldWndProc != nint.Zero)
        {
            _ = SetWindowLongPtr(_hwnd, GwlWndProc, _oldWndProc);
            _oldWndProc = nint.Zero;
        }

        if (_hMenu != nint.Zero)
        {
            _ = DestroyMenu(_hMenu);
            _hMenu = nint.Zero;
        }

        if (_trayIconHandle != nint.Zero)
        {
            _ = DestroyIcon(_trayIconHandle);
            _trayIconHandle = nint.Zero;
        }

        _initialized = false;
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmTrayIcon)
        {
            // For NOTIFYICON_VERSION_4, event id is in LOWORD(lParam).
            var eventId = (uint)(lParam.ToInt64() & 0xFFFF);
            if (eventId == WmLButtonDblClk)
            {
                _window?.ShowFromTray();
                return nint.Zero;
            }

            if (eventId == WmRButtonUp)
            {
                ShowContextMenu(hWnd);
                return nint.Zero;
            }
        }
        else if (msg == WmContextMenu)
        {
            ShowContextMenu(hWnd);
            return nint.Zero;
        }
        else if (msg == WmCommand)
        {
            var command = (uint)(wParam.ToInt64() & 0xFFFF);
            if (command == OpenMenuId)
            {
                _window?.ShowFromTray();
                return nint.Zero;
            }

            if (command == CloseMenuId)
            {
                _window?.ExitApplication();
                return nint.Zero;
            }
        }

        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu(nint hWnd)
    {
        if (_hMenu == nint.Zero)
        {
            return;
        }

        _ = GetCursorPos(out var pt);
        _ = SetForegroundWindow(hWnd);
        _ = TrackPopupMenuEx(_hMenu, TpmLeftAlign | TpmBottomAlign | TpmRightButton, pt.X, pt.Y, hWnd, nint.Zero);
        _ = PostMessage(hWnd, 0, nint.Zero, nint.Zero);
    }

    private NOTIFYICONDATA CreateNotifyIconData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayIcon,
            hIcon = _trayIconHandle != nint.Zero ? _trayIconHandle : LoadIcon(nint.Zero, (nint)0x7F00),
            szTip = "NeZha Desktop"
        };
    }

    private static nint LoadAppIconHandle()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                var iconFromFile = LoadImage(nint.Zero, iconPath, ImageIcon, 16, 16, LrLoadFromFile);
                if (iconFromFile != nint.Zero)
                {
                    return iconFromFile;
                }
            }

            var appPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(appPath) || !File.Exists(appPath))
            {
                return nint.Zero;
            }

            var large = new nint[1];
            var small = new nint[1];
            var extracted = ExtractIconEx(appPath, 0, large, small, 1);
            if (extracted > 0)
            {
                if (small[0] != nint.Zero)
                {
                    if (large[0] != nint.Zero)
                    {
                        _ = DestroyIcon(large[0]);
                    }

                    return small[0];
                }

                if (large[0] != nint.Zero)
                {
                    return large[0];
                }
            }
        }
        catch
        {
            // ignore
        }

        return nint.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;

        public uint uVersion
        {
            get => uTimeoutOrVersion;
            set => uTimeoutOrVersion = value;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, nint[]? phiconLarge, nint[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TrackPopupMenuEx(nint hmenu, uint fuFlags, int x, int y, nint hwnd, nint lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);
}
