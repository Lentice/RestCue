using System.Diagnostics;
using System.Runtime.InteropServices;
using RestCue.Core.Activity;

namespace RestCue.Infrastructure.Activity;

public sealed class WindowsForegroundContextProvider : IForegroundContextProvider
{
    private readonly bool canCollectProcessNames;
    private readonly IFullscreenWin32Api win32;
    private readonly IFullscreenDetector fullscreenDetector;

    public WindowsForegroundContextProvider(
        bool canCollectProcessNames,
        IFullscreenWin32Api? win32Api = null,
        IFullscreenDetector? fullscreenDetector = null)
    {
        this.canCollectProcessNames = canCollectProcessNames;
        win32 = win32Api ?? new FullscreenWin32Api();
        this.fullscreenDetector = fullscreenDetector ?? new WindowsFullscreenDetector(win32);
    }

    public ForegroundContext GetCurrentContext()
    {
        IntPtr hwnd = win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return ForegroundContext.Create(null, FullscreenState.Uncertain);

        string? processName = null;
        if (canCollectProcessNames)
        {
            _ = GetWindowThreadProcessId(hwnd, out uint processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch
            {
            }
        }

        FullscreenState state = fullscreenDetector.IsForegroundFullscreen()
            ? FullscreenState.Confirmed
            : FullscreenState.NotFullscreen;
        return ForegroundContext.Create(processName, state);
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
