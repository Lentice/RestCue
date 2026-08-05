using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace RestCue.App.Lifecycle;

internal static class TrayIconFactory
{
    public static Icon Create(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        DrawEye(graphics, color);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>
    /// Draws the RestCue eye glyph in <paramref name="color"/> onto a 32x32 surface.
    /// </summary>
    public static void DrawEye(Graphics graphics, Color color)
    {
        using var stroke = new Pen(color, 3.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var eye = new GraphicsPath();
        eye.StartFigure();
        eye.AddBezier(3.5f, 16f, 8f, 8f, 14f, 6.5f, 16f, 6.5f);
        eye.AddBezier(16f, 6.5f, 18f, 6.5f, 24f, 8f, 28.5f, 16f);
        eye.AddBezier(28.5f, 16f, 24f, 24f, 18f, 25.5f, 16f, 25.5f);
        eye.AddBezier(16f, 25.5f, 14f, 25.5f, 8f, 24f, 3.5f, 16f);
        eye.CloseFigure();

        graphics.DrawPath(stroke, eye);
        using var pupil = new SolidBrush(color);
        graphics.FillEllipse(pupil, 12f, 12f, 8f, 8f);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
