using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace RestCue.App.Lifecycle;

internal enum TrayIconKind
{
    Normal,
    Level1,
    Level2,
    Level3,
    Level4,
    Suppressed,
    RemindersOff
}

internal static class TrayIconFactory
{
    public static Icon Create(TrayIconKind kind, Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        DrawEye(graphics, color);

        switch (kind)
        {
            case TrayIconKind.Level1:
                DrawLevelBadge(graphics, color, ring: true, size: 8f);
                break;
            case TrayIconKind.Level2:
                DrawLevelBadge(graphics, color, ring: false, size: 8f);
                break;
            case TrayIconKind.Level3:
                DrawLevelBadge(graphics, color, ring: false, size: 11f);
                break;
            case TrayIconKind.Level4:
                DrawLevelBadge(graphics, color, ring: false, size: 14f);
                break;
            case TrayIconKind.Suppressed:
                DrawPendingDot(graphics, color);
                break;
            case TrayIconKind.RemindersOff:
                DrawRemindersOffMark(graphics, color);
                break;
        }

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

    /// <summary>
    /// A badge in the bottom-right corner that grows with severity, so the level reads in
    /// greyscale. Level 1 is a hollow ring; the higher levels are filled discs of growing
    /// size. The colour is reinforcement, not the message.
    /// </summary>
    private static void DrawLevelBadge(Graphics graphics, Color color, bool ring, float size)
    {
        float left = 32f - size - 2.5f;
        float top = 32f - size - 2.5f;

        if (ring)
        {
            using var pen = new Pen(color, 3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawEllipse(pen, left, top, size, size);
        }
        else
        {
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, left, top, size, size);
        }
    }

    /// <summary>
    /// A dot under the eye marks a reminder held back to a tray cue, so "muted" is not
    /// just a grey eye.
    /// </summary>
    private static void DrawPendingDot(Graphics graphics, Color color)
    {
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 12.5f, 24.5f, 7f, 7f);
    }

    /// <summary>
    /// A strike-through across the eye reads as "reminders off" in greyscale and colour.
    /// </summary>
    private static void DrawRemindersOffMark(Graphics graphics, Color color)
    {
        using var pen = new Pen(color, 3.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(pen, 6f, 6f, 26f, 26f);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
