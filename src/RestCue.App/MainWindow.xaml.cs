using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using RestCue.App.Lifecycle;
using RestCue.Core.Activity;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using RestCue.Core.Time;
using RestCue.Infrastructure.Time;

namespace RestCue.App;

public partial class MainWindow : System.Windows.Window, IStatusWindow
{
    private readonly DispatcherTimer activityTimer;
    private IUserActivityMonitor? activityMonitor;
    private UserActivityStatusTracker? activityTracker;
    private WorkCycleTracker? workCycleTracker;
    private ReminderWindow? reminderWindow;
    private TimeSpan snoozeDuration;

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
        this.activityMonitor = activityMonitor;
        activityTracker = new UserActivityStatusTracker(
            activityMonitor,
            new UserActivityStatusEvaluator(settings.IdleThreshold));

        snoozeDuration = settings.SnoozeDuration;

        workCycleTracker = new WorkCycleTracker(
            new SystemClock(),
            settings.WorkInterval,
            settings.IdleThreshold,
            settings.NaturalPauseThreshold,
            settings.MaximumReminderWait,
            settings.BreakDuration,
            settings.PassiveBreakThreshold,
            settings.SnoozeDuration,
            settings.ReminderDisplayDuration);

        workCycleTracker.ReminderShown += OnReminderShown;
        workCycleTracker.BreakCompleted += OnBreakCompleted;
        workCycleTracker.PassiveBreakCompleted += OnPassiveBreakCompleted;
        workCycleTracker.ReminderDismissed += OnReminderDismissed;

        RefreshActivityStatus(activityTracker.Refresh());
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

    private void OnActivityTimerTick(object? sender, EventArgs e)
    {
        if (activityTracker == null || activityMonitor == null) return;

        var sample = activityMonitor.GetCurrentActivity();
        var status = activityTracker.Refresh(sample);
        RefreshActivityStatus(status);

        if (workCycleTracker != null)
        {
            if (sample.IsAvailable)
            {
                workCycleTracker.Tick(sample.IdleDuration);
            }
            else
            {
                workCycleTracker.TickActivityUnavailable();
            }

            UpdateCycleStatus();
        }
    }

    private void OnReminderShown(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (reminderWindow == null)
            {
                reminderWindow = new ReminderWindow();
                reminderWindow.BreakRequested += OnBreakRequested;
                reminderWindow.BreakCompleted += OnReminderBreakCompleted;
                reminderWindow.SnoozeRequested += OnSnoozeRequested;
                reminderWindow.IgnoreRequested += OnIgnoreRequested;
                reminderWindow.Closed += (_, _) => reminderWindow = null;
            }

            reminderWindow.SnoozeDuration = snoozeDuration;
            reminderWindow.ShowReminder();
        });
    }

    private void OnBreakRequested(object? sender, EventArgs e)
    {
        workCycleTracker?.StartBreak();
        reminderWindow?.StartBreakCountdown(
            (int)(workCycleTracker?.BreakDuration.TotalSeconds ?? 20));
    }

    private void OnReminderBreakCompleted(object? sender, EventArgs e)
    {
        reminderWindow?.Close();
        reminderWindow = null;
    }

    private void OnBreakCompleted(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (reminderWindow != null)
            {
                reminderWindow.CompleteBreak();
            }
        });
    }

    private void OnPassiveBreakCompleted(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (reminderWindow != null)
            {
                reminderWindow.Close();
                reminderWindow = null;
            }
            UpdateCycleStatus();
        });
    }

    private void OnReminderDismissed(object? sender, ReminderDismissedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (reminderWindow != null)
            {
                reminderWindow.Close();
                reminderWindow = null;
            }
            UpdateCycleStatus();
        });
    }

    private void OnSnoozeRequested(object? sender, EventArgs e)
    {
        workCycleTracker?.Snooze();
        reminderWindow?.Close();
        reminderWindow = null;
        UpdateCycleStatus();
    }

    private void OnIgnoreRequested(object? sender, EventArgs e)
    {
        workCycleTracker?.Ignore();
        reminderWindow?.Close();
        reminderWindow = null;
        UpdateCycleStatus();
    }

    private void RefreshActivityStatus(UserActivityStatus status)
    {
        ActivityStatusText.Text = status switch
        {
            UserActivityStatus.Working => "有效工作中",
            UserActivityStatus.Idle => "Idle",
            _ => throw new InvalidOperationException("Unknown activity status.")
        };
    }

    private void UpdateCycleStatus()
    {
        if (workCycleTracker == null) return;

        CyclePhaseText.Text = workCycleTracker.CurrentPhase switch
        {
            WorkCyclePhase.Working => "累積工作中",
            WorkCyclePhase.PendingReminder => "等待停頓中",
            WorkCyclePhase.ReminderVisible => "提醒顯示中",
            WorkCyclePhase.BreakInProgress => "休息中",
            WorkCyclePhase.Snoozed => "延後中",
            _ => "未知"
        };
    }
}
