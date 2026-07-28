using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
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

    public event EventHandler<WorkCyclePhase>? PhaseChanged;

    public void WireLifecycleEvents()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void UnwireLifecycleEvents()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    public void StartActivityTracking(
        IUserActivityMonitor activityMonitor,
        AppSettings settings,
        IClock? clock = null)
    {
        this.activityMonitor = activityMonitor;
        activityTracker = new UserActivityStatusTracker(
            activityMonitor,
            new UserActivityStatusEvaluator(settings.IdleThreshold));

        snoozeDuration = settings.SnoozeDuration;

        workCycleTracker = new WorkCycleTracker(
            clock ?? new SystemClock(),
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
        workCycleTracker.Paused += OnPaused;
        workCycleTracker.Resumed += OnResumed;
        workCycleTracker.FocusModeStarted += OnFocusModeStarted;
        workCycleTracker.FocusModeEnded += OnFocusModeEnded;
        workCycleTracker.Disabled += OnDisabled;
        workCycleTracker.Enabled += OnEnabled;

        RefreshActivityStatus(activityTracker.Refresh());
        activityTimer.Start();
    }

    public void StopActivityTracking()
    {
        activityTimer.Stop();

        if (workCycleTracker != null)
        {
            workCycleTracker.ReminderShown -= OnReminderShown;
            workCycleTracker.BreakCompleted -= OnBreakCompleted;
            workCycleTracker.PassiveBreakCompleted -= OnPassiveBreakCompleted;
            workCycleTracker.ReminderDismissed -= OnReminderDismissed;
            workCycleTracker.Paused -= OnPaused;
            workCycleTracker.Resumed -= OnResumed;
            workCycleTracker.FocusModeStarted -= OnFocusModeStarted;
            workCycleTracker.FocusModeEnded -= OnFocusModeEnded;
            workCycleTracker.Disabled -= OnDisabled;
            workCycleTracker.Enabled -= OnEnabled;
        }
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

    public void Pause()
    {
        if (workCycleTracker == null) return;
        CloseReminderIfOpen();
        try
        {
            workCycleTracker.Pause();
            UpdateCycleStatus();
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Resume()
    {
        if (workCycleTracker == null) return;
        try
        {
            workCycleTracker.Resume();
            UpdateCycleStatus();
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void StartFocusMode()
    {
        if (workCycleTracker == null) return;
        CloseReminderIfOpen();
        try
        {
            workCycleTracker.StartFocusMode();
            UpdateCycleStatus();
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void EndFocusMode()
    {
        if (workCycleTracker == null) return;
        try
        {
            workCycleTracker.EndFocusMode();
            UpdateCycleStatus();
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Disable()
    {
        if (workCycleTracker == null) return;
        CloseReminderIfOpen();
        try
        {
            workCycleTracker.Disable();
            UpdateCycleStatus();
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Enable()
    {
        if (workCycleTracker == null) return;
        try
        {
            workCycleTracker.Enable();
            UpdateCycleStatus();
        }
        catch (InvalidOperationException)
        {
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (workCycleTracker == null) return;

            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    CloseReminderIfOpen();
                    workCycleTracker.HandleLock();
                    UpdateCycleStatus();
                    break;

                case SessionSwitchReason.SessionUnlock:
                    workCycleTracker.HandleUnlock();
                    UpdateCycleStatus();
                    break;
            }
        });
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (workCycleTracker == null) return;

            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    CloseReminderIfOpen();
                    workCycleTracker.HandleSleep();
                    UpdateCycleStatus();
                    break;

                case PowerModes.Resume:
                    workCycleTracker.HandleResume();
                    UpdateCycleStatus();
                    break;
            }
        });
    }

    private void CloseReminderIfOpen()
    {
        if (reminderWindow != null)
        {
            reminderWindow.Close();
            reminderWindow = null;
        }
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
            reminderWindow.BreakDuration = workCycleTracker!.BreakDuration;
            reminderWindow.ShowReminder();
        });
    }

    private void OnBreakRequested(object? sender, EventArgs e)
    {
        workCycleTracker?.StartBreak();
        reminderWindow?.StartBreakCountdown();
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
            CloseReminderIfOpen();
            UpdateCycleStatus();
        });
    }

    private void OnReminderDismissed(object? sender, ReminderDismissedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            CloseReminderIfOpen();
            UpdateCycleStatus();
        });
    }

    private void OnSnoozeRequested(object? sender, EventArgs e)
    {
        workCycleTracker?.Snooze();
        CloseReminderIfOpen();
        UpdateCycleStatus();
    }

    private void OnIgnoreRequested(object? sender, EventArgs e)
    {
        workCycleTracker?.Ignore();
        CloseReminderIfOpen();
        UpdateCycleStatus();
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateCycleStatus);
    }

    private void OnResumed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateCycleStatus);
    }

    private void OnFocusModeStarted(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateCycleStatus);
    }

    private void OnFocusModeEnded(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateCycleStatus);
    }

    private void OnDisabled(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateCycleStatus);
    }

    private void OnEnabled(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateCycleStatus);
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

    private WorkCyclePhase? lastReportedPhase;

    private void UpdateCycleStatus()
    {
        if (workCycleTracker == null) return;

        var phase = workCycleTracker.CurrentPhase;

        if (phase != lastReportedPhase)
        {
            lastReportedPhase = phase;
            PhaseChanged?.Invoke(this, phase);
            Dispatcher.Invoke(() => UpdateCyclePhaseText(phase));
        }
    }

    private void UpdateCyclePhaseText(WorkCyclePhase phase)
    {
        CyclePhaseText.Text = phase switch
        {
            WorkCyclePhase.Working => "累積工作中",
            WorkCyclePhase.PendingReminder => "等待停頓中",
            WorkCyclePhase.ReminderVisible => "提醒顯示中",
            WorkCyclePhase.BreakInProgress => "休息中",
            WorkCyclePhase.Snoozed => "延後中",
            WorkCyclePhase.Idle => "離開中",
            WorkCyclePhase.Paused => "已暫停",
            WorkCyclePhase.FocusMode => "專注模式",
            WorkCyclePhase.Disabled => "已停用",
            _ => "未知"
        };
    }
}