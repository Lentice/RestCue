using System.Runtime.InteropServices;

namespace RestCue.Infrastructure.Activity;

public sealed class FullscreenWin32Api : IFullscreenWin32Api
{
    public IntPtr GetForegroundWindow() => Native.GetForegroundWindow();
    public IntPtr GetDesktopWindow() => Native.GetDesktopWindow();
    public IntPtr GetShellWindow() => Native.GetShellWindow();
    public int GetWindowLong(IntPtr hWnd, int nIndex) => Native.GetWindowLong(hWnd, nIndex);
    public bool GetWindowRect(IntPtr hWnd, out RECT rect) => Native.GetWindowRect(hWnd, out rect);
    public nint MonitorFromWindow(IntPtr hwnd, uint dwFlags) => Native.MonitorFromWindow(hwnd, dwFlags);
    public bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi) => Native.GetMonitorInfo(hMonitor, ref lpmi);

    private static class Native
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern nint MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);
    }
}
