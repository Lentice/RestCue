using RestCue.Core.Activity;

namespace RestCue.Infrastructure.Activity;

public sealed class WindowsFullscreenDetector : IFullscreenDetector
{
    private readonly IFullscreenWin32Api win32;

    public WindowsFullscreenDetector(IFullscreenWin32Api? win32Api = null)
    {
        this.win32 = win32Api ?? new FullscreenWin32Api();
    }

    public bool IsForegroundFullscreen()
    {
        IntPtr hwnd = win32.GetForegroundWindow();
        return IsWindowFullscreen(hwnd) != FullscreenState.NotFullscreen;
    }

    private FullscreenState IsWindowFullscreen(IntPtr hwnd)
    {
        try
        {
            if (hwnd == win32.GetDesktopWindow() || hwnd == win32.GetShellWindow())
                return FullscreenState.NotFullscreen;

            const int GWL_STYLE = -16;
            const int WS_CAPTION = 0x00C00000;

            int style = win32.GetWindowLong(hwnd, GWL_STYLE);
            if (style == 0)
                return FullscreenState.Uncertain;

            if ((style & WS_CAPTION) != 0)
                return FullscreenState.NotFullscreen;

            if (!win32.GetWindowRect(hwnd, out RECT windowRect))
                return FullscreenState.Uncertain;

            nint monitor = win32.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return FullscreenState.Uncertain;

            var monitorInfo = new MONITORINFO
            {
                Size = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>()
            };
            if (!win32.GetMonitorInfo(monitor, ref monitorInfo))
                return FullscreenState.Uncertain;

            bool matchesMonitor = windowRect.Left == monitorInfo.MonitorRect.Left &&
                                  windowRect.Top == monitorInfo.MonitorRect.Top &&
                                  windowRect.Right == monitorInfo.MonitorRect.Right &&
                                  windowRect.Bottom == monitorInfo.MonitorRect.Bottom;

            return matchesMonitor ? FullscreenState.Confirmed : FullscreenState.NotFullscreen;
        }
        catch
        {
            return FullscreenState.Uncertain;
        }
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
}
