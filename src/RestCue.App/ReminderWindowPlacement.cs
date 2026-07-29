namespace RestCue.App;

internal readonly record struct PixelPoint(int X, int Y);

internal static class ReminderWindowPlacement
{
    public static PixelPoint RightEdge(int workAreaLeft, int workAreaTop, int workAreaRight, int workAreaBottom, int windowWidth, int windowHeight, int gap)
    {
        return new PixelPoint(
            Math.Max(workAreaLeft, workAreaRight - windowWidth - gap),
            Math.Max(workAreaTop, workAreaTop + (workAreaBottom - workAreaTop - windowHeight) / 2));
    }
}
