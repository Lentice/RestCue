using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RestCue.App;

public partial class ReminderWindow : Window
{
    private readonly DispatcherTimer breakGuideTimer;
    private bool isBreakStarting;

    public event EventHandler? BreakRequested;
    public event EventHandler? BreakCompleted;
    public event EventHandler? SnoozeRequested;
    public event EventHandler? IgnoreRequested;
    public event EventHandler? CancelRequested;

    public TimeSpan SnoozeDuration { get; set; }
    public TimeSpan BreakDuration { get; set; }

    public Action? BreakGuideTick { get; set; }

    public ReminderWindow()
    {
        InitializeComponent();
        breakGuideTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        breakGuideTimer.Tick += OnBreakGuideTick;

        Loaded += OnLoaded;
    }

    public void ShowReminder()
    {
        PhaseText.Text = "Look at something\nsix meters away.";
        ActionButton.Content = "Start Break";
        SnoozeButton.Content = $"延後 {SnoozeDuration.Minutes} 分鐘";
        SnoozeButton.Visibility = Visibility.Visible;
        IgnoreButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        GuideVisual.Visibility = Visibility.Collapsed;
        PositionOnPrimaryScreenRightEdge();
        Show();
    }

    public void StartBreakGuide()
    {
        PhaseText.Text = RestCue.Core.Reminders.BreakGuideText.ForCue(RestCue.Core.Reminders.BreakGuideCue.Start);
        ActionButton.Visibility = Visibility.Collapsed;
        SnoozeButton.Visibility = Visibility.Collapsed;
        IgnoreButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        GuideVisual.Visibility = Visibility.Visible;
        GuideVisual.BeginAnimation(
            System.Windows.UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.4,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(1),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            });
        breakGuideTimer.Start();
    }

    public void CompleteBreak()
    {
        breakGuideTimer.Stop();
        GuideVisual.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
        CancelButton.Visibility = Visibility.Collapsed;
        GuideVisual.Visibility = Visibility.Collapsed;
        OnBreakCompleted();
    }

    public void StopBreakGuide()
    {
        breakGuideTimer.Stop();
        GuideVisual.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionOnPrimaryScreenRightEdge();
    }

    private void PositionOnPrimaryScreenRightEdge()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            FallbackToPrimaryScreen();
            return;
        }

        nint monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            FallbackToPrimaryScreen();
            return;
        }

        var monitorInfo = new MONITORINFO
        {
            Size = Marshal.SizeOf<MONITORINFO>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            FallbackToPrimaryScreen();
            return;
        }

        var workArea = monitorInfo.WorkRect;
        Left = workArea.Right - Width - 4;
        Top = workArea.Top + (workArea.Bottom - workArea.Top - Height) / 2;
    }

    private void FallbackToPrimaryScreen()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 4;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        if (isBreakStarting) return;

        isBreakStarting = true;
        BreakRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSnoozeButtonClick(object sender, RoutedEventArgs e)
    {
        SnoozeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnIgnoreButtonClick(object sender, RoutedEventArgs e)
    {
        IgnoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancelButtonClick(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnBreakGuideTick(object? sender, EventArgs e)
    {
        BreakGuideTick?.Invoke();
    }

    private void OnBreakCompleted()
    {
        isBreakStarting = false;
        Hide();
        BreakCompleted?.Invoke(this, EventArgs.Empty);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", SetLastError = false)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = false)]
    private static extern nint MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT MonitorRect;
        public RECT WorkRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
