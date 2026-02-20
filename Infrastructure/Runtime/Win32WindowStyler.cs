using System.Runtime.InteropServices;

namespace NeZha_Desktop.Infrastructure.Runtime;

public static class Win32WindowStyler
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;

    private const nint WsCaption = 0x00C00000;
    private const nint WsMinimizeBox = 0x00020000;
    private const nint WsMaximizeBox = 0x00010000;
    private const nint WsSysMenu = 0x00080000;

    private const nint WsExToolWindow = 0x00000080;
    private const nint WsExAppWindow = 0x00040000;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoActivate = 0x0010;
    private static readonly nint HwndBottom = new(1);
    private const uint WmNclButtonDown = 0x00A1;
    private const nint HtCaption = 2;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    public static void ApplyWidgetWindowStyle(nint hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlStyle);
        style &= ~WsCaption;
        style &= ~WsMinimizeBox;
        style &= ~WsMaximizeBox;
        style &= ~WsSysMenu;
        SetWindowLongPtr(hwnd, GwlStyle, style);

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        exStyle |= WsExToolWindow;
        exStyle &= ~WsExAppWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, exStyle);

        _ = SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged | SwpNoActivate);
        TrySetRoundedCorners(hwnd);
    }

    public static void BeginDragMove(nint hwnd)
    {
        _ = ReleaseCapture();
        _ = SendMessage(hwnd, WmNclButtonDown, HtCaption, nint.Zero);
    }

    public static void SendToBottom(nint hwnd)
    {
        _ = SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    private static void TrySetRoundedCorners(nint hwnd)
    {
        try
        {
            var preference = DwmwcpRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch
        {
            // ignore
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
