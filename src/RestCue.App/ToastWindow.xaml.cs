using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RestCue.Core.Settings;
using RestCue.Infrastructure.Activity;

namespace RestCue.App;

public partial class ToastWindow : Window, IDisposable
{
    private readonly DispatcherTimer dismissalTimer;
    private readonly IFullscreenWin32Api win32;
    private bool isDisposed;

    public ToastWindow()
        : this(new FullscreenWin32Api())
    {
    }

    internal ToastWindow(IFullscreenWin32Api win32)
    {
        this.win32 = win32 ?? throw new ArgumentNullException(nameof(win32));
        InitializeComponent();

        dismissalTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(7)
        };
        dismissalTimer.Tick += OnDismissalTimerTick;
    }

    public void ShowToast(string title, string text, NotificationDuration duration)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        TitleText.Text = title;
        MessageText.Text = text;

        if (IsVisible)
        {
            UpdateLayout();
            PositionOnForegroundMonitorRightEdge();
        }
        else
        {
            Show();
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(PositionOnForegroundMonitorRightEdge));

        dismissalTimer.Stop();
        if (duration != NotificationDuration.UntilDismissed)
        {
            dismissalTimer.Interval = duration == NotificationDuration.Long
                ? TimeSpan.FromSeconds(25)
                : TimeSpan.FromSeconds(7);
            dismissalTimer.Start();
        }
    }

    public void Dispose()
    {
        if (isDisposed) return;

        isDisposed = true;
        dismissalTimer.Stop();
        if (IsVisible)
        {
            Close();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(HandleWindowMessage);

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(
            hwnd,
            GWL_EXSTYLE,
            exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
    }

    protected override void OnClosed(EventArgs e)
    {
        dismissalTimer.Stop();
        base.OnClosed(e);
    }

    private void OnDismissalTimerTick(object? sender, EventArgs e)
    {
        dismissalTimer.Stop();
        Hide();
    }

    private void PositionOnForegroundMonitorRightEdge()
    {
        if (!IsVisible) return;

        var foreground = win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            FallbackToPrimaryScreen();
            return;
        }

        nint monitor = win32.MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            FallbackToPrimaryScreen();
            return;
        }

        var monitorInfo = new MONITORINFO
        {
            Size = Marshal.SizeOf<MONITORINFO>()
        };
        if (!win32.GetMonitorInfo(monitor, ref monitorInfo))
        {
            FallbackToPrimaryScreen();
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !win32.GetWindowRect(hwnd, out var windowRect))
        {
            FallbackToPrimaryScreen();
            return;
        }

        var workArea = monitorInfo.WorkRect;
        var position = ReminderWindowPlacement.RightEdge(
            workArea.Left,
            workArea.Top,
            workArea.Right,
            workArea.Bottom,
            windowRect.Right - windowRect.Left,
            windowRect.Bottom - windowRect.Top,
            12);

        if (!SetWindowPos(
            hwnd,
            IntPtr.Zero,
            position.X,
            position.Y,
            0,
            0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE))
        {
            FallbackToPrimaryScreen();
        }
    }

    private void FallbackToPrimaryScreen()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - ActualHeight - 12;
    }

    private static IntPtr HandleWindowMessage(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WM_NCHITTEST)
        {
            handled = true;
            return new IntPtr(HTTRANSPARENT);
        }

        return IntPtr.Zero;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;

    [DllImport("user32.dll", SetLastError = false)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}
