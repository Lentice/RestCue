using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RestCue.Core.Settings;
using RestCue.Infrastructure.Activity;

namespace RestCue.App;

public partial class ReminderWindow : Window
{
    private readonly DispatcherTimer breakGuideTimer;
    private readonly IFullscreenWin32Api win32 = new FullscreenWin32Api();
    private bool isBreakStarting;

    public event EventHandler? BreakRequested;
    public event EventHandler? BreakCompleted;
    public event EventHandler? SnoozeRequested;
    public event EventHandler? IgnoreRequested;
    public event EventHandler? CancelRequested;

    public TimeSpan SnoozeDuration { get; set; }
    public TimeSpan BreakDuration { get; set; }

    public Action? BreakGuideTick { get; set; }

    public bool ReduceMotion { get; set; }

    private static readonly System.Windows.Media.Animation.DoubleAnimation DiscretePulse = new()
    {
        From = 1.0,
        To = 1.0,
        Duration = TimeSpan.FromSeconds(0.1),
    };

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

    /// <summary>
    /// Applies the user's reminder-opacity setting to the whole surface, clamped to the
    /// settings contract's range.
    /// </summary>
    /// <remarks>
    /// WPF opacity does not affect hit-testing, so the reminder stays clickable even at the
    /// minimum value. The break guide animates <c>GuideVisual.Opacity</c>, an inner
    /// element, so the two compose instead of fighting.
    /// </remarks>
    public void ApplySurfaceOpacity(double opacity)
    {
        Opacity = Math.Clamp(
            opacity,
            SettingsRanges.MinimumReminderOpacity,
            SettingsRanges.MaximumReminderOpacity);
    }

    public void ShowReminder()
    {
        ClearBreakStartGuard();
        PhaseText.Text = "看向約六公尺外";
        ActionButton.Content = "開始休息";
        SnoozeButton.Content = $"延後 {(int)Math.Round(SnoozeDuration.TotalMinutes)} 分鐘";
        SnoozeButton.Visibility = Visibility.Visible;
        ActionButton.Visibility = Visibility.Visible;
        IgnoreButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        GuideVisual.Visibility = Visibility.Collapsed;
        PositionOnForegroundMonitorRightEdge();
        Show();
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(PositionOnForegroundMonitorRightEdge));
    }

    public void StartBreakGuide()
    {
        PhaseText.Text = RestCue.Core.Reminders.BreakGuideText.ForCue(RestCue.Core.Reminders.BreakGuideCue.Start);
        ActionButton.Visibility = Visibility.Collapsed;
        SnoozeButton.Visibility = Visibility.Collapsed;
        IgnoreButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        GuideVisual.Visibility = Visibility.Visible;

        if (ReduceMotion)
        {
            GuideVisual.BeginAnimation(
                System.Windows.UIElement.OpacityProperty, DiscretePulse);
            PhaseText.Text = RestCue.Core.Reminders.BreakGuideText.ForCue(
                RestCue.Core.Reminders.BreakGuideCue.Middle);
        }
        else
        {
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
        }
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
        ClearBreakStartGuard();
    }

    /// <summary>
    /// Clears the double-activation guard on the primary action.
    /// </summary>
    /// <remarks>
    /// Every exit from break-guide presentation clears it — completion, cancellation, or
    /// close. Clearing it only on completion left a cancelled break with the guard stuck
    /// set, which permanently disabled "start a break" on this surface.
    /// </remarks>
    private void ClearBreakStartGuard()
    {
        isBreakStarting = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        ClearBreakStartGuard();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = win32.GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionOnForegroundMonitorRightEdge();
    }

    private void PositionOnForegroundMonitorRightEdge()
    {
        var hwnd = win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            FallbackToPrimaryScreen();
            return;
        }

        nint monitor = win32.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            FallbackToPrimaryScreen();
            return;
        }

        var monitorInfo = new MONITORINFO
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>()
        };
        if (!win32.GetMonitorInfo(monitor, ref monitorInfo))
        {
            FallbackToPrimaryScreen();
            return;
        }

        var workArea = monitorInfo.WorkRect;
        var reminderHwnd = new WindowInteropHelper(this).Handle;
        if (reminderHwnd == IntPtr.Zero)
        {
            FallbackToPrimaryScreen();
            return;
        }

        if (!win32.GetWindowRect(reminderHwnd, out var windowRect))
        {
            FallbackToPrimaryScreen();
            return;
        }

        var position = ReminderWindowPlacement.RightEdge(
            workArea.Left,
            workArea.Top,
            workArea.Right,
            workArea.Bottom,
            windowRect.Right - windowRect.Left,
            windowRect.Bottom - windowRect.Top,
            4);

        if (!SetWindowPos(
            reminderHwnd,
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
        ClearBreakStartGuard();
        Hide();
        BreakCompleted?.Invoke(this, EventArgs.Empty);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;

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
