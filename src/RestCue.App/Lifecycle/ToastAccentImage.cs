using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace RestCue.App.Lifecycle;

/// <summary>
/// Renders the small circular logo shown at the left edge of a toast. Windows gives no
/// control over a toast's background colour, so this image is the only place a rest-debt
/// level can tint the notification — deliberately a small dot rather than a hero image,
/// so it reads as a hint instead of an alarm.
/// </summary>
internal static class ToastAccentImage
{
    private static readonly Dictionary<int, string?> Cache = new();
    private static readonly object Gate = new();

    /// <summary>
    /// Returns a file path to a circular badge in <paramref name="color"/>, or
    /// <see langword="null"/> if it could not be written (the caller then shows a toast
    /// with its default logo).
    /// </summary>
    public static string? TryGetPath(Color color)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(color.ToArgb(), out string? cached))
                return cached;

            string? path = TryRender(color);
            Cache[color.ToArgb()] = path;
            return path;
        }
    }

    private static string? TryRender(Color color)
    {
        try
        {
            string directory = Path.Combine(Path.GetTempPath(), "RestCue", "Toast");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"accent-{color.ToArgb():x8}.png");
            if (File.Exists(path))
                return path;

            using var bitmap = new Bitmap(64, 64);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                // The toast crops this to a circle, so the whole surface is the tint and
                // the glyph is punched out in white to stay legible on every level colour.
                graphics.Clear(color);
                graphics.ScaleTransform(2f, 2f);
                TrayIconFactory.DrawEye(graphics, Color.White);
            }

            bitmap.Save(path, ImageFormat.Png);
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
