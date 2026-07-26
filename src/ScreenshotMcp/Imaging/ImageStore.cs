using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ScreenshotMcp.Imaging;

internal sealed record SavedImage(string Path, int Width, int Height, int SourceWidth, int SourceHeight, long Bytes)
{
    internal string MimeType => System.IO.Path.GetExtension(Path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        ? "image/jpeg"
        : "image/png";
}

internal static class ImageStore
{
    /// <summary>
    /// Claude downscales images to 1568px on the long edge before the model sees them, so capturing
    /// or uploading anything larger costs time and tokens without adding readable detail.
    /// </summary>
    internal const int DefaultMaxLongEdge = 1568;

    /// <summary>GDI+ requires the quality value as a long; passing an int throws "Parameter is not valid".</summary>
    private const long JpegQuality = 85L;

    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(6);

    private static readonly Lazy<ImageCodecInfo> JpegCodec = new(() =>
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid));

    internal static string Root { get; } =
        Path.Combine(Path.GetTempPath(), "screenshot-mcp");

    internal static SavedImage Save(Bitmap source, string name, int maxLongEdge, string format)
    {
        bool jpeg = format.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
            || format.Equals("jpg", StringComparison.OrdinalIgnoreCase);

        Directory.CreateDirectory(Root);
        string path = Path.Combine(Root, $"{name}.{(jpeg ? "jpg" : "png")}");

        using Bitmap output = Resize(source, maxLongEdge);

        if (jpeg)
        {
            using EncoderParameters parameters = new(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
            output.Save(path, JpegCodec.Value, parameters);
        }
        else
        {
            output.Save(path, ImageFormat.Png);
        }

        return new SavedImage(
            path,
            output.Width,
            output.Height,
            source.Width,
            source.Height,
            new FileInfo(path).Length);
    }

    /// <summary>Deletes captures older than the retention period so the temp folder cannot grow without bound.</summary>
    internal static void Prune()
    {
        try
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            DateTime cutoff = DateTime.UtcNow - RetentionPeriod;

            foreach (string file in Directory.EnumerateFiles(Root))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                    // Another process is holding the file; it will be pruned on a later run.
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Nothing to do — pruning is best-effort housekeeping.
        }
    }

    private static Bitmap Resize(Bitmap source, int maxLongEdge)
    {
        int longEdge = Math.Max(source.Width, source.Height);
        if (maxLongEdge <= 0 || longEdge <= maxLongEdge)
        {
            return (Bitmap)source.Clone();
        }

        double ratio = (double)maxLongEdge / longEdge;
        int width = Math.Max(1, (int)Math.Round(source.Width * ratio));
        int height = Math.Max(1, (int)Math.Round(source.Height * ratio));

        Bitmap target = new(width, height, PixelFormat.Format24bppRgb);
        target.SetResolution(source.HorizontalResolution, source.VerticalResolution);

        using Graphics graphics = Graphics.FromImage(target);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));

        return target;
    }
}
