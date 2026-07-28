using System.Runtime.InteropServices;

namespace RestCue.Infrastructure.Activity;

public interface IFullscreenWin32Api
{
    IntPtr GetForegroundWindow();
    IntPtr GetDesktopWindow();
    IntPtr GetShellWindow();
    int GetWindowLong(IntPtr hWnd, int nIndex);
    bool GetWindowRect(IntPtr hWnd, out RECT rect);
    nint MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
public struct MONITORINFO
{
    public int Size;
    public RECT MonitorRect;
    public RECT WorkRect;
    public uint Flags;
}
