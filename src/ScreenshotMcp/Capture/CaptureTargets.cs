using System.Diagnostics;
using System.Drawing;
using static ScreenshotMcp.Capture.NativeMethods;

namespace ScreenshotMcp.Capture;

/// <summary>A physical display, indexed in the order Windows enumerates it.</summary>
internal sealed record MonitorTarget(
    int Index,
    string DeviceName,
    Rectangle Bounds,
    bool IsPrimary,
    double Scale);

/// <summary>A top-level window that is visible and therefore capturable.</summary>
internal sealed record WindowTarget(
    nint Handle,
    string Title,
    string ClassName,
    string ProcessName,
    Rectangle Bounds,
    bool IsMinimized,
    bool IsForeground);

internal static class CaptureTargets
{
    internal static Rectangle VirtualScreen => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    internal static IReadOnlyList<MonitorTarget> GetMonitors()
    {
        List<MonitorTarget> monitors = [];

        EnumDisplayMonitors(0, 0, (nint hMonitor, nint _, ref RECT _, nint _) =>
        {
            MONITORINFOEX info = new() { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
            if (!GetMonitorInfoW(hMonitor, ref info))
            {
                return true;
            }

            double scale = GetDpiForMonitor(hMonitor, MonitorDpiTypeEffective, out uint dpiX, out _) == 0
                ? Math.Round(dpiX / 96.0, 2)
                : 1.0;

            monitors.Add(new MonitorTarget(
                monitors.Count,
                info.szDevice,
                ToRectangle(info.rcMonitor),
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                scale));

            return true;
        }, 0);

        // Primary first, then left-to-right, so indexes stay stable between calls.
        return [.. monitors
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.X)
            .ThenBy(m => m.Bounds.Y)
            .Select((m, i) => m with { Index = i })];
    }

    internal static IReadOnlyList<WindowTarget> GetWindows()
    {
        List<WindowTarget> windows = [];
        nint foreground = GetForegroundWindow();

        EnumWindows((nint hWnd, nint _) =>
        {
            if (!IsCapturableWindow(hWnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hWnd, out uint processId);

            windows.Add(new WindowTarget(
                hWnd,
                GetText(hWnd),
                GetClass(hWnd),
                GetProcessName(processId),
                GetWindowBounds(hWnd),
                IsIconic(hWnd),
                hWnd == foreground));

            return true;
        }, 0);

        return windows;
    }

    /// <summary>
    /// Resolves a window by decimal/hex handle, or by case-insensitive match against the process
    /// name or title. Ranked most-specific first, so asking for "Steam" picks the Steam client
    /// rather than a browser that merely has "Steam" in its page title.
    /// </summary>
    internal static (WindowTarget Match, IReadOnlyList<WindowTarget> Alternatives) ResolveWindow(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("A window title or handle is required.", nameof(query));
        }

        IReadOnlyList<WindowTarget> windows = GetWindows();

        if (TryParseHandle(query, out nint handle))
        {
            WindowTarget? byHandle = windows.FirstOrDefault(w => w.Handle == handle);
            return byHandle is null
                ? throw new ArgumentException($"No visible window with handle {handle}.", nameof(query))
                : (byHandle, []);
        }

        WindowTarget[] ranked = [.. windows
            .Select(w => (Window: w, Score: Score(w, query.Trim())))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Window.IsForeground)
            .ThenByDescending(x => x.Window.Bounds.Width * (long)x.Window.Bounds.Height)
            .Select(x => x.Window)];

        if (ranked.Length == 0)
        {
            throw new ArgumentException(
                $"No visible window matches \"{query}\". Call list_capture_targets to see what is open.",
                nameof(query));
        }

        return (ranked[0], ranked[1..]);
    }

    private static int Score(WindowTarget window, string query)
    {
        const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;

        if (window.Title.Equals(query, ignoreCase))
        {
            return 6;
        }

        if (window.ProcessName.Equals(query, ignoreCase))
        {
            return 5;
        }

        if (window.ProcessName.StartsWith(query, ignoreCase))
        {
            return 4;
        }

        if (window.Title.StartsWith(query, ignoreCase))
        {
            return 3;
        }

        if (window.ProcessName.Contains(query, ignoreCase))
        {
            return 2;
        }

        return window.Title.Contains(query, ignoreCase) ? 1 : 0;
    }

    internal static Rectangle GetWindowBounds(nint hWnd)
    {
        // The DWM frame bounds exclude the invisible resize border that GetWindowRect includes.
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0
            && frame.Width > 0
            && frame.Height > 0)
        {
            return ToRectangle(frame);
        }

        return GetWindowRect(hWnd, out RECT rect) ? ToRectangle(rect) : Rectangle.Empty;
    }

    private static bool IsCapturableWindow(nint hWnd)
    {
        if (!IsWindowVisible(hWnd) || GetWindowTextLengthW(hWnd) == 0)
        {
            return false;
        }

        if ((GetWindowLongPtr(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        // Cloaked windows are invisible UWP shells and virtual-desktop leftovers.
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        Rectangle bounds = GetWindowBounds(hWnd);
        return bounds.Width > 1 && bounds.Height > 1;
    }

    private static bool TryParseHandle(string query, out nint handle)
    {
        string text = query.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out long hex))
        {
            handle = (nint)hex;
            return true;
        }

        if (long.TryParse(text, out long value) && value > 0)
        {
            handle = (nint)value;
            return true;
        }

        handle = 0;
        return false;
    }

    private static string GetText(nint hWnd)
    {
        int length = GetWindowTextLengthW(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        char[] buffer = new char[length + 1];
        int written = GetWindowText(hWnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(written, 0));
    }

    private static string GetClass(nint hWnd)
    {
        char[] buffer = new char[256];
        int written = GetClassName(hWnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(written, 0));
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    internal static Rectangle ToRectangle(RECT rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);
}
