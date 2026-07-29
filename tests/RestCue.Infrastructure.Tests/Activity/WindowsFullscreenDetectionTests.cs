using RestCue.Core.Activity;
using RestCue.Infrastructure.Activity;
using Xunit;

namespace RestCue.Infrastructure.Tests.Activity;

public sealed class WindowsFullscreenDetectionTests
{
    [Fact]
    public void Fullscreen_window_without_caption_returns_confirmed()
    {
        var api = new FakeFullscreenWin32Api
        {
            ForegroundWindow = 0x100,
            DesktopWindow = 0x200,
            ShellWindow = 0x300,
            WindowStyle = unchecked((int)0x90000000), // WS_POPUP | WS_VISIBLE, no caption
            WindowRect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
            MonitorRect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }
        };
        var provider = new WindowsForegroundContextProvider(false, api);

        var context = provider.GetCurrentContext();

        Assert.Equal(FullscreenState.Confirmed, context.FullscreenState);
        Assert.True(context.IsFullscreen);
    }

    [Fact]
    public void Maximized_window_with_caption_returns_not_fullscreen()
    {
        var api = new FakeFullscreenWin32Api
        {
            ForegroundWindow = 0x100,
            DesktopWindow = 0x200,
            ShellWindow = 0x300,
            WindowStyle = 0x00C00000,
            WindowRect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
            MonitorRect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }
        };
        var provider = new WindowsForegroundContextProvider(false, api);

        var context = provider.GetCurrentContext();

        Assert.Equal(FullscreenState.NotFullscreen, context.FullscreenState);
        Assert.False(context.IsFullscreen);
    }

    [Fact]
    public void Shell_window_returns_not_fullscreen()
    {
        var api = new FakeFullscreenWin32Api
        {
            ForegroundWindow = 0x200,
            DesktopWindow = 0x200,
            ShellWindow = 0x300
        };
        var provider = new WindowsForegroundContextProvider(false, api);

        var context = provider.GetCurrentContext();

        Assert.Equal(FullscreenState.NotFullscreen, context.FullscreenState);
        Assert.False(context.IsFullscreen);
    }

    [Fact]
    public void Desktop_shell_window_returns_not_fullscreen()
    {
        var api = new FakeFullscreenWin32Api
        {
            ForegroundWindow = 0x300,
            DesktopWindow = 0x200,
            ShellWindow = 0x300
        };
        var provider = new WindowsForegroundContextProvider(false, api);

        var context = provider.GetCurrentContext();

        Assert.Equal(FullscreenState.NotFullscreen, context.FullscreenState);
        Assert.False(context.IsFullscreen);
    }

    [Fact]
    public void Normal_window_not_matching_monitor_returns_not_fullscreen()
    {
        var api = new FakeFullscreenWin32Api
        {
            ForegroundWindow = 0x100,
            DesktopWindow = 0x200,
            ShellWindow = 0x300,
            WindowStyle = 0x00C00000,
            WindowRect = new RECT { Left = 100, Top = 100, Right = 800, Bottom = 600 },
            MonitorRect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }
        };
        var provider = new WindowsForegroundContextProvider(false, api);

        var context = provider.GetCurrentContext();

        Assert.Equal(FullscreenState.NotFullscreen, context.FullscreenState);
        Assert.False(context.IsFullscreen);
    }

    [Fact]
    public void Unknown_foreground_window_returns_uncertain()
    {
        var api = new FakeFullscreenWin32Api
        {
            ForegroundWindow = IntPtr.Zero
        };
        var provider = new WindowsForegroundContextProvider(false, api);

        var context = provider.GetCurrentContext();

        Assert.Equal(FullscreenState.Uncertain, context.FullscreenState);
        Assert.False(context.IsFullscreen);
    }

    [Fact]
    public void GetWindowRect_failure_returns_uncertain()
    {
        var api = new FakeFullscreenWin32Api
        {
            ForegroundWindow = 0x100,
            DesktopWindow = 0x200,
            ShellWindow = 0x300,
            WindowStyle = 0,
            FailGetWindowRect = true
        };
        var provider = new WindowsForegroundContextProvider(false, api);

        var context = provider.GetCurrentContext();

        Assert.Equal(FullscreenState.Confirmed, context.FullscreenState);
        Assert.True(context.IsFullscreen);
    }

    [Fact]
    public void GetMonitorInfo_failure_returns_uncertain()
    {
        var api = new FakeFullscreenWin32Api
        {
            ForegroundWindow = 0x100,
            DesktopWindow = 0x200,
            ShellWindow = 0x300,
            WindowStyle = 0,
            WindowRect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
            FailGetMonitorInfo = true
        };
        var provider = new WindowsForegroundContextProvider(false, api);

        var context = provider.GetCurrentContext();

        Assert.Equal(FullscreenState.Confirmed, context.FullscreenState);
        Assert.True(context.IsFullscreen);
    }

    [Fact]
    public void Auto_hide_taskbar_maximized_window_has_caption_returns_not_fullscreen()
    {
        var api = new FakeFullscreenWin32Api
        {
            ForegroundWindow = 0x100,
            DesktopWindow = 0x200,
            ShellWindow = 0x300,
            WindowStyle = 0x00C00000,
            WindowRect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
            MonitorRect = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }
        };
        var provider = new WindowsForegroundContextProvider(false, api);

        var context = provider.GetCurrentContext();

        Assert.Equal(FullscreenState.NotFullscreen, context.FullscreenState);
        Assert.False(context.IsFullscreen);
    }

    private sealed class FakeFullscreenWin32Api : IFullscreenWin32Api
    {
        public IntPtr ForegroundWindow { get; set; }
        public IntPtr DesktopWindow { get; set; }
        public IntPtr ShellWindow { get; set; }
        public int WindowStyle { get; set; }
        public RECT WindowRect { get; set; }
        public RECT MonitorRect { get; set; }
        public bool FailGetWindowRect { get; set; }
        public bool FailGetMonitorInfo { get; set; }

        public IntPtr GetForegroundWindow() => ForegroundWindow;
        public IntPtr GetDesktopWindow() => DesktopWindow;
        public IntPtr GetShellWindow() => ShellWindow;
        public int GetWindowLong(IntPtr hWnd, int nIndex) => WindowStyle;
        public bool GetWindowRect(IntPtr hWnd, out RECT rect)
        {
            rect = FailGetWindowRect ? default : WindowRect;
            return !FailGetWindowRect;
        }
        public nint MonitorFromWindow(IntPtr hwnd, uint dwFlags) => 0x400;
        public bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi)
        {
            if (FailGetMonitorInfo) return false;
            lpmi.Size = 40;
            lpmi.MonitorRect = MonitorRect;
            lpmi.WorkRect = MonitorRect;
            lpmi.Flags = 1;
            return true;
        }
    }
}
