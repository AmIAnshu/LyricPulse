using System.Runtime.InteropServices;

namespace LyricPulse.App.Platform;

internal enum QueryUserNotificationState
{
    NotPresent = 1,
    Busy = 2,
    RunningD3DFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7
}

/// <summary>
/// Source-generated [LibraryImport] Win32 declarations (zero runtime marshalling, 100% NativeAOT safe).
/// </summary>
internal static partial class Win32Native
{
    public const int GWL_EXSTYLE = -20;

    public const nint WS_EX_TRANSPARENT = 0x00000020;
    public const nint WS_EX_TOOLWINDOW = 0x00000080;
    public const nint WS_EX_LAYERED = 0x00080000;
    public const nint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const nint WS_EX_NOACTIVATE = 0x08000000;

    public static readonly nint HWND_TOPMOST = new(-1);
    public static readonly nint HWND_MESSAGE = new(-3);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    public const uint LWA_ALPHA = 0x00000002;

    public const uint DWMWA_NCRENDERING_POLICY = 2;
    public const uint DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    public const uint DWMWA_EXCLUDED_FROM_PEEK = 12;
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    public const int DWMNCRP_DISABLED = 1;
    public const int DWMWCP_DONOTROUND = 1;

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const int MDT_EFFECTIVE_DPI = 0;

    public const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_POWERBROADCAST = 0x0218;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_APP = 0x8000;

    public const nint PBT_APMRESUMEAUTOMATIC = 0x0012;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static partial int GetWindowLong32(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    public static nint GetWindowLongPtr(nint hWnd, int nIndex) =>
        nint.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    public static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong) =>
        nint.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, (int)dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(
        nint hWnd,
        uint crKey,
        byte bAlpha,
        uint dwFlags);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(
        nint hWnd,
        uint dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hWnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(PointStruct pt, uint dwFlags);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(
        nint hMonitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetMonitorInfo(nint hMonitor, MONITORINFOEXW* lpmi);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(out QueryUserNotificationState pquns);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    public static partial int GetMessage(out MsgStruct lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(ref MsgStruct lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(ref MsgStruct lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    public struct PointStruct
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RectStruct
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MsgStruct
    {
        public nint HWnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public PointStruct Pt;
        public uint LPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MONITORINFOEXW
    {
        public uint CbSize;
        public RectStruct RcMonitor;
        public RectStruct RcWork;
        public uint DwFlags;
        public fixed char SzDevice[32];
    }
}
