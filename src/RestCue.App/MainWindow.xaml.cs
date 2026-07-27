using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using RestCue.App.Lifecycle;
using RestCue.Core.Activity;
using RestCue.Core.Settings;

namespace RestCue.App;

public partial class MainWindow : System.Windows.Window, IStatusWindow
{
    private readonly DispatcherTimer activityTimer;
    private UserActivityStatusTracker? activityTracker;

    public MainWindow()
    {
        InitializeComponent();
        activityTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        activityTimer.Tick += OnActivityTimerTick;
    }

    public void StartActivityTracking(
        IUserActivityMonitor activityMonitor,
        AppSettings settings)
    {
        activityTracker = new UserActivityStatusTracker(
            activityMonitor,
            new UserActivityStatusEvaluator(settings.IdleThreshold));
        RefreshActivityStatus();
        activityTimer.Start();
    }

    public void ShowOrActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private void OnActivityTimerTick(object? sender, EventArgs e) =>
        RefreshActivityStatus();

    private void RefreshActivityStatus()
    {
        UserActivityStatus status = activityTracker?.Refresh()
            ?? UserActivityStatus.Idle;
        ActivityStatusText.Text = status switch
        {
            UserActivityStatus.Working => "有效工作中",
            UserActivityStatus.Idle => "Idle",
            _ => throw new InvalidOperationException("Unknown activity status.")
        };
    }
}
