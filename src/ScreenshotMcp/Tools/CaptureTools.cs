using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ScreenshotMcp.Capture;
using ScreenshotMcp.Imaging;

namespace ScreenshotMcp.Tools;

[McpServerToolType]
internal static class CaptureTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    [McpServerTool(Name = "capture_screen", Title = "Capture screen"), Description(
        """
        Takes a screenshot of the user's monitors and saves it to the temp folder, returning the file
        path plus the image itself so it can be looked at immediately. Captures every monitor as a
        separate image by default. Use this whenever the user asks a question about what is currently
        on their screen.
        """)]
    internal static CallToolResult CaptureScreen(
        [Description("""Which monitors to capture: "all" (default), "primary", or a 0-based monitor index such as "1". Call list_capture_targets to see the indexes.""")]
        string monitor = "all",
        [Description("""When capturing "all", stitch every monitor into one wide image instead of returning one image per monitor. Separate images are easier to read and are the default.""")]
        bool combined = false,
        [Description("""Optional crop as "x,y,width,height" in pixels, relative to the top-left of the captured area. Only valid for a single monitor or a combined capture.""")]
        string? region = null,
        [Description("Longest edge of the output image in pixels. Default 1568, which is the size Claude downscales to anyway. Use 0 to keep the native resolution.")]
        int maxLongEdge = ImageStore.DefaultMaxLongEdge,
        [Description("""Output format: "png" (default, sharper text) or "jpeg" (smaller file).""")]
        string format = "png",
        [Description("Draw the mouse cursor into the capture.")]
        bool includeCursor = false,
        [Description("Include the image in the response so it can be seen without a separate file read. Default true; set false to get only the path.")]
        bool returnImage = true)
    {
        return Execute(() =>
        {
            long started = Stopwatch.GetTimestamp();
            IReadOnlyList<MonitorTarget> monitors = CaptureTargets.GetMonitors();
            List<Capture> captures = [];

            if (monitor.Equals("all", StringComparison.OrdinalIgnoreCase) && !combined && monitors.Count > 1)
            {
                if (region is not null)
                {
                    throw new ArgumentException(
                        "A region needs a single capture area. Pass a monitor index, or set combined=true.",
                        nameof(region));
                }

                foreach (MonitorTarget target in monitors)
                {
                    captures.Add(CaptureArea(target.Bounds, null, $"monitor-{target.Index}", includeCursor, target));
                }
            }
            else
            {
                (Rectangle bounds, MonitorTarget? target, string label) = ResolveArea(monitor, monitors);
                captures.Add(CaptureArea(bounds, region, label, includeCursor, target));
            }

            return BuildResult(captures, maxLongEdge, format, returnImage, started);
        });
    }

    [McpServerTool(Name = "capture_window", Title = "Capture window"), Description(
        """
        Takes a screenshot of a single window and saves it to the temp folder, returning the file path
        plus the image itself. The window is captured even when another window is covering it. Use
        this when the user asks about one specific app rather than their whole screen.
        """)]
    internal static CallToolResult CaptureWindow(
        [Description("Part of the window title (case-insensitive), the process name, or a window handle from list_capture_targets.")]
        string window,
        [Description("Longest edge of the output image in pixels. Default 1568, which is the size Claude downscales to anyway. Use 0 to keep the native resolution.")]
        int maxLongEdge = ImageStore.DefaultMaxLongEdge,
        [Description("""Output format: "png" (default, sharper text) or "jpeg" (smaller file).""")]
        string format = "png",
        [Description("Draw the mouse cursor into the capture.")]
        bool includeCursor = false,
        [Description("Include the image in the response so it can be seen without a separate file read. Default true; set false to get only the path.")]
        bool returnImage = true)
    {
        return Execute(() =>
        {
            long started = Stopwatch.GetTimestamp();
            (WindowTarget target, IReadOnlyList<WindowTarget> alternatives) = CaptureTargets.ResolveWindow(window);

            if (target.IsMinimized)
            {
                throw new InvalidOperationException(
                    $"\"{target.Title}\" is minimized, so there is nothing to capture. Ask the user to restore it.");
            }

            Bitmap bitmap = ScreenCapturer.CaptureWindow(target.Handle, includeCursor);

            Dictionary<string, object?> details = new()
            {
                ["window"] = target.Title,
                ["process"] = target.ProcessName,
                ["handle"] = target.Handle.ToString(),
                ["bounds"] = Describe(target.Bounds),
            };

            if (alternatives.Count > 0)
            {
                details["otherMatches"] = alternatives.Select(w => $"{w.Title} ({w.ProcessName}, handle {w.Handle})");
            }

            Capture capture = new(bitmap, Sanitize(target.Title), details);

            return BuildResult([capture], maxLongEdge, format, returnImage, started);
        });
    }

    [McpServerTool(Name = "list_capture_targets", Title = "List capture targets"), Description(
        """
        Lists the monitors and the visible top-level windows that can be captured, with their indexes,
        handles and pixel bounds. Use this to pick a target before calling capture_screen or
        capture_window.
        """)]
    internal static string ListCaptureTargets()
    {
        var payload = new
        {
            virtualScreen = Describe(CaptureTargets.VirtualScreen),
            monitors = CaptureTargets.GetMonitors().Select(m => new
            {
                index = m.Index,
                device = m.DeviceName,
                bounds = Describe(m.Bounds),
                primary = m.IsPrimary,
                scale = m.Scale,
            }),
            windows = CaptureTargets.GetWindows()
                .OrderByDescending(w => w.IsForeground)
                .ThenBy(w => w.ProcessName)
                .Select(w => new
                {
                    title = w.Title,
                    process = w.ProcessName,
                    className = w.ClassName,
                    handle = w.Handle.ToString(),
                    bounds = Describe(w.Bounds),
                    minimized = w.IsMinimized,
                    foreground = w.IsForeground,
                }),
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private sealed record Capture(Bitmap Bitmap, string Name, Dictionary<string, object?> Details);

    /// <summary>
    /// Turns a failure into an error result carrying the real message. Letting the exception escape
    /// would give the caller only a generic "an error occurred", with nothing to correct against.
    /// </summary>
    private static CallToolResult Execute(Func<CallToolResult> capture)
    {
        try
        {
            return capture();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = exception.Message }],
            };
        }
    }

    private static (Rectangle Bounds, MonitorTarget? Target, string Label) ResolveArea(
        string monitor,
        IReadOnlyList<MonitorTarget> monitors)
    {
        if (monitor.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return (CaptureTargets.VirtualScreen, null, "all-monitors");
        }

        if (monitor.Equals("primary", StringComparison.OrdinalIgnoreCase))
        {
            MonitorTarget primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            return (primary.Bounds, primary, $"monitor-{primary.Index}");
        }

        if (!int.TryParse(monitor.Trim(), out int index))
        {
            throw new ArgumentException(
                $"""Unknown monitor "{monitor}". Use "all", "primary", or a 0-based index.""",
                nameof(monitor));
        }

        MonitorTarget? match = monitors.FirstOrDefault(m => m.Index == index)
            ?? throw new ArgumentException(
                $"There is no monitor {index}. Available indexes: 0-{monitors.Count - 1}.",
                nameof(monitor));

        return (match.Bounds, match, $"monitor-{match.Index}");
    }

    private static Capture CaptureArea(
        Rectangle bounds,
        string? region,
        string name,
        bool includeCursor,
        MonitorTarget? target)
    {
        Rectangle area = region is null ? bounds : ApplyRegion(bounds, region);
        Bitmap bitmap = ScreenCapturer.CaptureRegion(area, includeCursor);

        Dictionary<string, object?> details = new()
        {
            ["area"] = Describe(area),
        };

        if (target is not null)
        {
            details["monitor"] = target.Index;
            details["device"] = target.DeviceName;
            details["primary"] = target.IsPrimary;
            details["scale"] = target.Scale;
        }

        return new Capture(bitmap, name, details);
    }

    private static Rectangle ApplyRegion(Rectangle bounds, string region)
    {
        int[] parts = [.. region.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p, out int value)
                ? value
                : throw new ArgumentException($"""Region "{region}" must be four numbers: "x,y,width,height".""", nameof(region)))];

        if (parts.Length != 4)
        {
            throw new ArgumentException(
                $"""Region "{region}" must be four numbers: "x,y,width,height".""",
                nameof(region));
        }

        Rectangle crop = new(bounds.X + parts[0], bounds.Y + parts[1], parts[2], parts[3]);
        crop.Intersect(bounds);

        if (crop.Width <= 0 || crop.Height <= 0)
        {
            throw new ArgumentException(
                $"""Region "{region}" falls outside the captured area {Describe(bounds)}.""",
                nameof(region));
        }

        return crop;
    }

    private static CallToolResult BuildResult(
        List<Capture> captures,
        int maxLongEdge,
        string format,
        bool returnImage,
        long started)
    {
        List<SavedImage> saved = [];
        List<object> summaries = [];
        List<ContentBlock> content = [];

        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");

            foreach (Capture capture in captures)
            {
                SavedImage image = ImageStore.Save(capture.Bitmap, $"{stamp}-{capture.Name}", maxLongEdge, format);
                saved.Add(image);

                Dictionary<string, object?> summary = new(capture.Details)
                {
                    ["path"] = image.Path,
                    ["size"] = $"{image.Width}x{image.Height}",
                    ["captured"] = $"{image.SourceWidth}x{image.SourceHeight}",
                    ["bytes"] = image.Bytes,
                };

                summaries.Add(summary);
            }
        }
        finally
        {
            foreach (Capture capture in captures)
            {
                capture.Bitmap.Dispose();
            }
        }

        var payload = new
        {
            images = summaries,
            elapsedMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        };

        content.Add(new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) });

        if (returnImage)
        {
            foreach (SavedImage image in saved)
            {
                content.Add(new ImageContentBlock
                {
                    // Data carries the base64 payload as UTF-8 bytes — the serializer decodes it
                    // straight into the JSON string. Passing raw file bytes here emits mojibake
                    // that clients reject as an invalid base64 string.
                    Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(File.ReadAllBytes(image.Path))),
                    MimeType = image.MimeType,
                });
            }
        }

        return new CallToolResult { Content = content };
    }

    private static string Describe(Rectangle rect) =>
        $"{rect.Width}x{rect.Height} at {rect.X},{rect.Y}";

    private static string Sanitize(string name)
    {
        string trimmed = new([.. name.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != ' ')]);
        return trimmed.Length switch
        {
            0 => "window",
            > 40 => trimmed[..40],
            _ => trimmed,
        };
    }
}
