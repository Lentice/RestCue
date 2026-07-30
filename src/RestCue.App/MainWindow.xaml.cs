using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using RestCue.App.Lifecycle;
using RestCue.Core.Activity;
using RestCue.Core.Audio;
using RestCue.Core.Domain;
using RestCue.Core.Events;
using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using RestCue.Core.Time;
using RestCue.Infrastructure.Activity;
using RestCue.Infrastructure.Time;

namespace RestCue.App;

public partial class MainWindow : System.Windows.Window, IStatusWindow
{
    private readonly DispatcherTimer activityTimer;
    private IUserActivityMonitor? activityMonitor;
    private UserActivityStatusTracker? activityTracker;
    private WorkCycleTracker? workCycleTracker;
    private IForegroundContextProvider? foregroundContextProvider;
    private ApplicationRuleSet? applicationRules;
    private ReminderWindow? reminderWindow;
    private BreakGuideSession? breakGuideSession;
    private BreakGuideAudioCoordinator? audioCoordinator;
    private IBreakGuideAudioPlayer? audioPlayer;
    private IClock? clock;
    private TimeSpan snoozeDuration;
    private Core.Settings.BreakGuideMode userBreakGuideMode;

    public MainWindow()
    {
        InitializeComponent();
        PauseFor15MenuItem.Header = PausePresets.FifteenMinutes.Label;
        PauseFor30MenuItem.Header = PausePresets.ThirtyMinutes.Label;
        PauseFor60MenuItem.Header = PausePresets.OneHour.Label;
        activityTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        activityTimer.Tick += OnActivityTimerTick;
    }

    public event EventHandler<WorkCyclePhase>? PhaseChanged;
    public event EventHandler<ReminderSuppressedEventArgs>? LowInterruptionReminderRequested;
    public event EventHandler<RestDebtLevelChangedEventArgs>? DebtLevelChanged;

    public RestDebtLevel CurrentDebtLevel { get; private set; }

    public WorkCycleTracker? WorkCycleTracker => workCycleTracker;

    public IBreakGuideAudioPlayer? AudioPlayer
    {
        get => audioPlayer;
        set => audioPlayer = value;
    }

    public Action? OpenStatistics { get; set; }
    public Action? OpenDataTransparency { get; set; }
    public Action? OpenDataManagement { get; set; }
    public Action? OpenSettings { get; set; }
    public Action? OpenAbout { get; set; }
    public Action? ExitApplication { get; set; }

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
        IClock? clock = null,
        IForegroundContextProvider? foregroundContextProvider = null,
        IEnumerable<ApplicationRule>? applicationRules = null)
    {
        this.activityMonitor = activityMonitor;
        this.clock = clock ?? new SystemClock();
        activityTracker = new UserActivityStatusTracker(
            activityMonitor,
            new UserActivityStatusEvaluator(settings.IdleThreshold));

        snoozeDuration = settings.SnoozeDuration;
        userBreakGuideMode = settings.BreakGuideMode;
        this.foregroundContextProvider = foregroundContextProvider;
        this.applicationRules = new ApplicationRuleSet(applicationRules);

        workCycleTracker = new WorkCycleTracker(
            clock ?? new SystemClock(),
            settings.WorkInterval,
            settings.IdleThreshold,
            settings.NaturalPauseThreshold,
            settings.MaximumReminderWait,
            settings.BreakDuration,
            settings.PassiveBreakThreshold,
            settings.SnoozeDuration,
            settings.ReminderDisplayDuration,
            settings.RetryCooldown,
            settings.DebtLevel2Threshold,
            settings.DebtLevel3Threshold,
            settings.DebtLevel4Threshold,
            settings.FocusModeDuration);

        workCycleTracker.ReminderShown += OnReminderShown;
        workCycleTracker.ReminderSuppressed += OnReminderSuppressed;
        workCycleTracker.BreakCompleted += OnBreakCompleted;
        workCycleTracker.PassivePauseDetected += OnPassivePauseDetected;
        workCycleTracker.ReminderDismissed += OnReminderDismissed;
        workCycleTracker.Paused += OnPaused;
        workCycleTracker.Resumed += OnResumed;
        workCycleTracker.FocusModeStarted += OnFocusModeStarted;
        workCycleTracker.FocusModeEnded += OnFocusModeEnded;
        workCycleTracker.Disabled += OnDisabled;
        workCycleTracker.Enabled += OnEnabled;
        workCycleTracker.RestDebtLevelChanged += OnRestDebtLevelChanged;

        CurrentDebtLevel = workCycleTracker.RestDebtLevel;

        RefreshActivityStatus(activityTracker.Refresh());
        activityTimer.Start();
        UpdateCycleStatus();
    }

    public void StopActivityTracking()
    {
        EndAudioGuide();
        audioCoordinator = null;

        activityTimer.Stop();

        if (workCycleTracker != null)
        {
            workCycleTracker.ReminderShown -= OnReminderShown;
            workCycleTracker.ReminderSuppressed -= OnReminderSuppressed;
            workCycleTracker.BreakCompleted -= OnBreakCompleted;
            workCycleTracker.PassivePauseDetected -= OnPassivePauseDetected;
            workCycleTracker.ReminderDismissed -= OnReminderDismissed;
            workCycleTracker.Paused -= OnPaused;
            workCycleTracker.Resumed -= OnResumed;
            workCycleTracker.FocusModeStarted -= OnFocusModeStarted;
            workCycleTracker.FocusModeEnded -= OnFocusModeEnded;
            workCycleTracker.Disabled -= OnDisabled;
            workCycleTracker.Enabled -= OnEnabled;
            workCycleTracker.RestDebtLevelChanged -= OnRestDebtLevelChanged;
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
        try
        {
            App.ExecutePause(workCycleTracker, CloseReminderIfOpen);
            UpdateCycleStatus();
        }
        catch (InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "RestCue: Pause or PauseFor rejected — invalid state transition.");
        }
    }

    public void PauseFor(TimeSpan duration)
    {
        if (workCycleTracker == null) return;
        try
        {
            CloseReminderIfOpen();
            workCycleTracker.Pause(duration);
            UpdateCycleStatus();
        }
        catch (InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "RestCue: PauseFor rejected — invalid state transition.");
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
        try
        {
            App.ExecuteStartFocusMode(workCycleTracker, CloseReminderIfOpen);
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

    public void UpdateForegroundContextProvider(bool collectProcessNames)
    {
        foregroundContextProvider = new WindowsForegroundContextProvider(
            collectProcessNames);
    }

    public void StartBreakNow()
    {
        if (workCycleTracker == null) return;

        CloseReminderIfOpen();

        try
        {
            workCycleTracker.ManualStartBreak();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        UpdateCycleStatus();

        if (reminderWindow == null)
        {
            reminderWindow = new ReminderWindow();
            reminderWindow.BreakCompleted += OnReminderBreakCompleted;
            reminderWindow.CancelRequested += OnCancelRequested;
            reminderWindow.Closed += OnReminderWindowClosed;
        }

        reminderWindow.BreakDuration = workCycleTracker.BreakDuration;
        SetupBreakGuideSession(clock ?? new SystemClock(), workCycleTracker.BreakDuration);
        reminderWindow.Show();
    }

    private void OnStatisticsClick(object? sender, RoutedEventArgs e) => OpenStatistics?.Invoke();
    private void OnDataTransparencyClick(object? sender, RoutedEventArgs e) => OpenDataTransparency?.Invoke();
    private void OnDataManagementClick(object? sender, RoutedEventArgs e) => OpenDataManagement?.Invoke();
    private void OnSettingsClick(object? sender, RoutedEventArgs e) => OpenSettings?.Invoke();
    private void OnAboutClick(object? sender, RoutedEventArgs e) => OpenAbout?.Invoke();
    private void OnExitClick(object? sender, RoutedEventArgs e) => ExitApplication?.Invoke();

    private void OnBreakNowClick(object? sender, RoutedEventArgs e) => StartBreakNow();

    private void OnTogglePauseClick(object? sender, RoutedEventArgs e)
    {
        if (workCycleTracker?.CurrentPhase == WorkCyclePhase.Paused)
            Resume();
        else
            Pause();
    }

    private void OnPauseFor15Click(object? sender, RoutedEventArgs e) =>
        PauseFor(PausePresets.FifteenMinutes.Duration);

    private void OnPauseFor30Click(object? sender, RoutedEventArgs e) =>
        PauseFor(PausePresets.ThirtyMinutes.Duration);

    private void OnPauseFor60Click(object? sender, RoutedEventArgs e) =>
        PauseFor(PausePresets.OneHour.Duration);
    private void OnPauseManualClick(object? sender, RoutedEventArgs e) => Pause();
    private void OnResumeClick(object? sender, RoutedEventArgs e) => Resume();

    private void OnToggleFocusClick(object? sender, RoutedEventArgs e)
    {
        if (workCycleTracker?.CurrentPhase == WorkCyclePhase.FocusMode)
            EndFocusMode();
        else
            StartFocusMode();
    }

    private void OnToggleDisableClick(object? sender, RoutedEventArgs e)
    {
        if (workCycleTracker?.CurrentPhase == WorkCyclePhase.Disabled)
            Enable();
        else
            Disable();
    }

    private void UpdateMenuAndButtonStates()
    {
        if (workCycleTracker == null) return;

        var phase = workCycleTracker.CurrentPhase;

        PauseSubmenu.Visibility = Visibility.Collapsed;
        ResumeMenuItem.Visibility = Visibility.Collapsed;
        FocusMenuItem.Header = "專注模式";
        FocusMenuItem.IsEnabled = true;
        DisableMenuItem.Header = "停用提醒";
        DisableMenuItem.IsEnabled = true;
        BreakNowMenuItem.IsEnabled = true;

        PauseResumeButton.Content = "暫停";
        PauseResumeButton.IsEnabled = true;
        FocusButton.Content = "專注模式";
        FocusButton.IsEnabled = true;
        DisableButton.Content = "停用提醒";
        DisableButton.IsEnabled = true;
        BreakNowButton.IsEnabled = true;

        switch (phase)
        {
            case WorkCyclePhase.Paused:
                ResumeMenuItem.Visibility = Visibility.Visible;
                FocusMenuItem.IsEnabled = false;
                BreakNowMenuItem.IsEnabled = false;
                DisableMenuItem.IsEnabled = false;
                PauseResumeButton.Content = "繼續";
                FocusButton.IsEnabled = false;
                BreakNowButton.IsEnabled = false;
                DisableButton.IsEnabled = false;
                break;

            case WorkCyclePhase.FocusMode:
                FocusMenuItem.Header = "結束專注模式";
                PauseResumeButton.IsEnabled = false;
                FocusButton.Content = "結束專注模式";
                DisableMenuItem.IsEnabled = false;
                DisableButton.IsEnabled = false;
                break;

            case WorkCyclePhase.Disabled:
                DisableMenuItem.Header = "啟用提醒";
                PauseResumeButton.IsEnabled = false;
                FocusMenuItem.IsEnabled = false;
                BreakNowMenuItem.IsEnabled = false;
                DisableButton.Content = "啟用提醒";
                FocusButton.IsEnabled = false;
                BreakNowButton.IsEnabled = false;
                break;

            case WorkCyclePhase.BreakInProgress:
                PauseResumeButton.IsEnabled = false;
                FocusMenuItem.IsEnabled = false;
                FocusButton.IsEnabled = false;
                DisableMenuItem.IsEnabled = false;
                DisableButton.IsEnabled = false;
                BreakNowMenuItem.IsEnabled = false;
                BreakNowButton.IsEnabled = false;
                break;

            case WorkCyclePhase.Idle:
                PauseResumeButton.IsEnabled = false;
                FocusMenuItem.IsEnabled = false;
                FocusButton.IsEnabled = false;
                DisableMenuItem.IsEnabled = false;
                DisableButton.IsEnabled = false;
                BreakNowMenuItem.IsEnabled = false;
                BreakNowButton.IsEnabled = false;
                break;

            default:
                PauseSubmenu.Visibility = Visibility.Visible;
                break;
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
            if (workCycleTracker?.CurrentPhase == WorkCyclePhase.BreakInProgress)
            {
                workCycleTracker.CancelBreak();
            }
            EndAudioGuide();
            audioCoordinator = null;
            reminderWindow.StopBreakGuide();
            reminderWindow.Close();
            reminderWindow = null;
        }
    }

    private void OnReminderWindowClosed(object? sender, EventArgs e)
    {
        EndAudioGuide();
        audioCoordinator = null;
        reminderWindow = null;
        breakGuideSession = null;
    }

    private void OnCancelRequested(object? sender, EventArgs e)
    {
        workCycleTracker?.CancelBreak();
        CloseReminderIfOpen();
        UpdateCycleStatus();
    }

    private void OnActivityTimerTick(object? sender, EventArgs e)
    {
        if (activityTracker == null || activityMonitor == null) return;

        var sample = activityMonitor.GetCurrentActivity();
        var status = activityTracker.Refresh(sample);
        RefreshActivityStatus(status);

        if (workCycleTracker != null)
        {
            ApplyForegroundContext();
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

    private void ApplyForegroundContext()
    {
        if (workCycleTracker == null || foregroundContextProvider == null) return;

        var context = foregroundContextProvider.GetCurrentContext();
        workCycleTracker.TrackForegroundProcess(context.ProcessName);
        var rule = applicationRules?.Find(context.ProcessName);
        bool windowSuppression = context.FullscreenState != FullscreenState.NotFullscreen;

        var fsCap = PresentationIntensityPolicy.FromFullscreenState(context.FullscreenState);
        var ruleCap = PresentationIntensityPolicy.FromApplicationRuleType(rule?.RuleType ?? ApplicationRuleType.Normal);
        var combinedCap = (PresentationIntensity)Math.Min((int)fsCap, (int)ruleCap);
        workCycleTracker.SetIntensityCaps(combinedCap, PresentationIntensityPolicy.DefaultUserCap);

        workCycleTracker.UpdateForegroundContext(
            windowSuppression,
            rule?.IsSuppressingReminder ?? false,
            !windowSuppression && rule?.RuleType == ApplicationRuleType.TrayOnly,
            rule?.RuleType == ApplicationRuleType.CustomInterval ? rule.CustomInterval : null);
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
                reminderWindow.CancelRequested += OnCancelRequested;
                reminderWindow.Closed += OnReminderWindowClosed;
            }

            reminderWindow.SnoozeDuration = snoozeDuration;
            reminderWindow.BreakDuration = workCycleTracker!.BreakDuration;
            reminderWindow.ShowReminder();
        });
    }

    private void OnReminderSuppressed(object? sender, ReminderSuppressedEventArgs e)
    {
        Dispatcher.Invoke(() => LowInterruptionReminderRequested?.Invoke(this, e));
    }

    private void OnBreakRequested(object? sender, EventArgs e)
    {
        workCycleTracker?.StartBreak();
        if (reminderWindow == null || workCycleTracker == null) return;

        SetupBreakGuideSession(clock ?? new SystemClock(), workCycleTracker.BreakDuration);
    }

    private void SetupBreakGuideSession(IClock clock, TimeSpan duration)
    {
        breakGuideSession = new BreakGuideSession(clock, duration);
        breakGuideSession.CueChanged += OnBreakGuideCueChanged;
        breakGuideSession.Completed += OnBreakGuideCompleted;
        breakGuideSession.Cancelled += OnBreakGuideCancelled;

        if (reminderWindow != null)
        {
            reminderWindow.BreakGuideTick = () => breakGuideSession.Tick();
            reminderWindow.StartBreakGuide();
        }

        breakGuideSession.Start();

        if (audioPlayer != null && workCycleTracker != null)
        {
            EndAudioGuide();
            audioCoordinator = new BreakGuideAudioCoordinator(audioPlayer, MapBreakGuideMode(userBreakGuideMode));
            bool audioAllowed = workCycleTracker.EffectiveIntensity >= PresentationIntensity.PopupAndSound;
            audioCoordinator.BeginGuide(audioAllowed);
            breakGuideSession.CueChanged += OnAudioCueChanged;
            audioCoordinator.DegradedToVisual += (_, _) =>
                System.Diagnostics.Trace.TraceError("RestCue: Break guide degraded to visual-only.");
        }
    }

    private void OnAudioCueChanged(object? sender, BreakGuideCue cue)
    {
        audioCoordinator?.HandleCue(cue);
    }

    private void EndAudioGuide()
    {
        if (audioCoordinator != null)
        {
            if (breakGuideSession != null)
                breakGuideSession.CueChanged -= OnAudioCueChanged;
            audioCoordinator.EndGuide();
        }
    }

    private void OnBreakGuideCueChanged(object? sender, BreakGuideCue cue)
    {
        Dispatcher.Invoke(() =>
        {
            if (reminderWindow != null)
            {
                reminderWindow.PhaseText.Text = RestCue.Core.Reminders.BreakGuideText.ForCue(cue);
            }
        });
    }

    private void OnBreakGuideCompleted(object? sender, EventArgs e)
    {
        EndAudioGuide();
        audioCoordinator = null;
        breakGuideSession = null;
    }

    private void OnBreakGuideCancelled(object? sender, EventArgs e)
    {
        EndAudioGuide();
        audioCoordinator = null;
        breakGuideSession = null;
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

    private void OnPassivePauseDetected(object? sender, EventArgs e)
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

    private void OnRestDebtLevelChanged(object? sender, RestDebtLevelChangedEventArgs e)
    {
        CurrentDebtLevel = e.Current;
        Dispatcher.Invoke(() =>
        {
            DebtLevelChanged?.Invoke(this, e);
            UpdateCycleStatus();
        });
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
            Dispatcher.Invoke(() =>
            {
                UpdateCyclePhaseText(phase);
                UpdateMenuAndButtonStates();
            });
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

    internal static Core.Reminders.BreakGuideMode MapBreakGuideMode(Core.Settings.BreakGuideMode mode)
    {
        return mode switch
        {
            Core.Settings.BreakGuideMode.Cue => Core.Reminders.BreakGuideMode.Chime,
            Core.Settings.BreakGuideMode.Voice => Core.Reminders.BreakGuideMode.Speech,
            Core.Settings.BreakGuideMode.NumberlessVisual => Core.Reminders.BreakGuideMode.VisualOnly,
            _ => Core.Reminders.BreakGuideMode.Chime
        };
    }
}
