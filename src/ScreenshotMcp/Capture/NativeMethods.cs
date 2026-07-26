using System.Runtime.InteropServices;

namespace ScreenshotMcp.Capture;

internal static partial class NativeMethods
{
    internal const int SRCCOPY = 0x00CC0020;

    /// <summary>Includes layered (transparent) windows in a BitBlt of the screen.</summary>
    internal const int CAPTUREBLT = 0x40000000;

    /// <summary>PW_RENDERFULLCONTENT — renders DirectX/composited window content (Windows 8.1+).</summary>
    internal const int PW_RENDERFULLCONTENT = 0x00000002;

    internal const int SM_XVIRTUALSCREEN = 76;
    internal const int SM_YVIRTUALSCREEN = 77;
    internal const int SM_CXVIRTUALSCREEN = 78;
    internal const int SM_CYVIRTUALSCREEN = 79;

    internal const int MONITORINFOF_PRIMARY = 0x00000001;

    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const int DWMWA_CLOAKED = 14;

    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;

    internal const int CURSOR_SHOWING = 0x00000001;
    internal const int DI_NORMAL = 0x0003;

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2.</summary>
    internal static readonly nint DpiAwarenessPerMonitorV2 = -4;

    /// <summary>MDT_EFFECTIVE_DPI.</summary>
    internal const int MonitorDpiTypeEffective = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public nint hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ICONINFO
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    internal delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint data);

    internal delegate bool EnumWindowsProc(nint hWnd, nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessDpiAwarenessContext(nint value);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    internal static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint hWnd, nint hdc);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrintWindow(nint hWnd, nint hdc, int flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetWindowText(nint hWnd, [Out] char[] text, int maxCount);

    [LibraryImport("user32.dll")]
    internal static partial int GetWindowTextLengthW(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetClassName(nint hWnd, [Out] char[] name, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint hWnd, int index);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(EnumWindowsProc callback, nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFOEX info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorInfo(ref CURSORINFO info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetIconInfo(nint hIcon, out ICONINFO info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DrawIconEx(
        nint hdc,
        int xLeft,
        int yTop,
        nint hIcon,
        int width,
        int height,
        int step,
        nint brushFlicker,
        int flags);

    [LibraryImport("user32.dll")]
    internal static partial nint CopyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BitBlt(
        nint dest,
        int destX,
        int destY,
        int width,
        int height,
        nint src,
        int srcX,
        int srcY,
        int rop);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hWnd, int attribute, out RECT value, int size);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hWnd, int attribute, out int value, int size);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);
}
