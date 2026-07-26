using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using static ScreenshotMcp.Capture.NativeMethods;

namespace ScreenshotMcp.Capture;

/// <summary>
/// GDI-based capture. BitBlt from the screen DC is the lowest-latency path for one-shot grabs —
/// DXGI Desktop Duplication only wins when capturing continuously, where its setup cost amortizes.
/// </summary>
internal static class ScreenCapturer
{
    internal static Bitmap CaptureRegion(Rectangle region, bool includeCursor)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentException($"Capture region {region} has no area.", nameof(region));
        }

        nint screenDc = GetDC(0);
        if (screenDc == 0)
        {
            throw new InvalidOperationException("Could not obtain the screen device context.");
        }

        try
        {
            return CopyFromDc(screenDc, region, includeCursor);
        }
        finally
        {
            ReleaseDC(0, screenDc);
        }
    }

    /// <summary>
    /// Captures a window even when it is occluded by another window, via PrintWindow with
    /// PW_RENDERFULLCONTENT. Falls back to a screen-region grab for windows that refuse to render
    /// themselves (some Electron and OpenGL apps return a blank surface).
    /// </summary>
    internal static Bitmap CaptureWindow(nint hWnd, bool includeCursor)
    {
        if (!IsWindow(hWnd))
        {
            throw new ArgumentException($"Window handle {hWnd} no longer exists.", nameof(hWnd));
        }

        Rectangle visible = CaptureTargets.GetWindowBounds(hWnd);
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            throw new InvalidOperationException("The window has no visible area to capture.");
        }

        if (!GetWindowRect(hWnd, out RECT raw))
        {
            return CaptureRegion(visible, includeCursor);
        }

        Rectangle full = CaptureTargets.ToRectangle(raw);
        Bitmap? rendered = TryPrintWindow(hWnd, full);

        if (rendered is null)
        {
            return CaptureRegion(visible, includeCursor);
        }

        using (rendered)
        {
            // PrintWindow renders the full window rect; crop away the invisible shadow border.
            Rectangle crop = new(
                visible.X - full.X,
                visible.Y - full.Y,
                visible.Width,
                visible.Height);
            crop.Intersect(new Rectangle(0, 0, rendered.Width, rendered.Height));

            if (crop.Width <= 0 || crop.Height <= 0 || crop.Size == rendered.Size)
            {
                return (Bitmap)rendered.Clone();
            }

            return rendered.Clone(crop, rendered.PixelFormat);
        }
    }

    private static Bitmap? TryPrintWindow(nint hWnd, Rectangle bounds)
    {
        nint screenDc = GetDC(0);
        if (screenDc == 0)
        {
            return null;
        }

        nint memoryDc = 0;
        nint bitmap = 0;
        nint previous = 0;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
            if (memoryDc == 0 || bitmap == 0)
            {
                return null;
            }

            previous = SelectObject(memoryDc, bitmap);

            if (!PrintWindow(hWnd, memoryDc, PW_RENDERFULLCONTENT))
            {
                return null;
            }

            Bitmap result = Image.FromHbitmap(bitmap);
            return IsBlank(result) ? Discard(result) : result;
        }
        finally
        {
            if (previous != 0)
            {
                SelectObject(memoryDc, previous);
            }

            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != 0)
            {
                DeleteDC(memoryDc);
            }

            ReleaseDC(0, screenDc);
        }
    }

    private static Bitmap CopyFromDc(nint sourceDc, Rectangle region, bool includeCursor)
    {
        nint memoryDc = 0;
        nint bitmap = 0;
        nint previous = 0;

        try
        {
            memoryDc = CreateCompatibleDC(sourceDc);
            bitmap = CreateCompatibleBitmap(sourceDc, region.Width, region.Height);
            if (memoryDc == 0 || bitmap == 0)
            {
                throw new InvalidOperationException("Could not allocate the capture bitmap.");
            }

            previous = SelectObject(memoryDc, bitmap);

            if (!BitBlt(memoryDc, 0, 0, region.Width, region.Height, sourceDc, region.X, region.Y, SRCCOPY | CAPTUREBLT))
            {
                throw new InvalidOperationException(
                    $"BitBlt failed (Win32 error {Marshal.GetLastWin32Error()}).");
            }

            if (includeCursor)
            {
                DrawCursor(memoryDc, region);
            }

            return Image.FromHbitmap(bitmap);
        }
        finally
        {
            if (previous != 0)
            {
                SelectObject(memoryDc, previous);
            }

            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != 0)
            {
                DeleteDC(memoryDc);
            }
        }
    }

    private static void DrawCursor(nint targetDc, Rectangle region)
    {
        CURSORINFO cursor = new() { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref cursor) || (cursor.flags & CURSOR_SHOWING) == 0 || cursor.hCursor == 0)
        {
            return;
        }

        nint icon = CopyIcon(cursor.hCursor);
        if (icon == 0)
        {
            return;
        }

        try
        {
            int hotspotX = 0;
            int hotspotY = 0;

            if (GetIconInfo(icon, out ICONINFO info))
            {
                hotspotX = info.xHotspot;
                hotspotY = info.yHotspot;

                if (info.hbmMask != 0)
                {
                    DeleteObject(info.hbmMask);
                }

                if (info.hbmColor != 0)
                {
                    DeleteObject(info.hbmColor);
                }
            }

            DrawIconEx(
                targetDc,
                cursor.ptScreenPos.X - region.X - hotspotX,
                cursor.ptScreenPos.Y - region.Y - hotspotY,
                icon,
                0,
                0,
                0,
                0,
                DI_NORMAL);
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    /// <summary>Detects the all-black surface PrintWindow leaves behind when a window won't render.</summary>
    private static bool IsBlank(Bitmap bitmap)
    {
        const int samplesPerAxis = 16;
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);

        try
        {
            int stepX = Math.Max(1, bitmap.Width / samplesPerAxis);
            int stepY = Math.Max(1, bitmap.Height / samplesPerAxis);

            unsafe
            {
                byte* scan = (byte*)data.Scan0;

                for (int y = 0; y < bitmap.Height; y += stepY)
                {
                    int* row = (int*)(scan + ((long)y * data.Stride));

                    for (int x = 0; x < bitmap.Width; x += stepX)
                    {
                        if ((row[x] & 0x00FFFFFF) != 0)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Bitmap? Discard(Bitmap bitmap)
    {
        bitmap.Dispose();
        return null;
    }
}
