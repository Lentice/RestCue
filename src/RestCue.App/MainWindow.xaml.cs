using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using RestCue.App.Lifecycle;
using RestCue.App.UsageEvents;
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

public partial class MainWindow : System.Windows.Window, IStatusWindow, IWorkPhaseSource
{
    private const int CancelBreakHotkeyId = 1;
    private HwndSource? hwndSource;
    private bool hotkeyRegistered;
    private bool reduceMotion;
    private readonly DispatcherTimer activityTimer;
    private IUserActivityMonitor? activityMonitor;
    private UserActivityStatusTracker? activityTracker;
    private WorkCycleTracker? workCycleTracker;
    private IForegroundContextProvider? foregroundContextProvider;
    private ApplicationRuleSet? applicationRules;
    private HashSet<string>? defaultSuggestionNames;
    private HashSet<string>? seenSuggestionProcesses;
    private ReminderWindow? reminderWindow;
    private BreakGuideSession? breakGuideSession;
    private BreakGuideAudioCoordinator? audioCoordinator;
    private IBreakGuideAudioPlayer? audioPlayer;
    private IClock? clock;
    private TimeSpan snoozeDuration;
    private Core.Settings.BreakGuideMode userBreakGuideMode;
    private double reminderOpacity = 1.0;

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
    public event EventHandler? LightTouchReminderRequested;
    public event EventHandler<RestDebtLevelChangedEventArgs>? DebtLevelChanged;
    public event EventHandler<SuggestionEventArgs>? SuggestionRequested;

    public RestDebtLevel CurrentDebtLevel { get; private set; }

    public WorkCycleTracker? WorkCycleTracker => workCycleTracker;

    /// <summary>
    /// The phase right now, or <c>null</c> before activity tracking has started.
    /// Lets a late subscriber to <see cref="PhaseChanged"/> recover what it missed.
    /// </summary>
    WorkCyclePhase? IWorkPhaseSource.CurrentPhase => workCycleTracker?.CurrentPhase;

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
        IEnumerable<ApplicationRule>? applicationRules = null,
        IEnumerable<string>? defaultSuggestionProcessNames = null)
    {
        reduceMotion = settings.ReduceMotion;
        RegisterCancelBreakHotkey();

        this.activityMonitor = activityMonitor;
        this.clock = clock ?? new SystemClock();
        activityTracker = new UserActivityStatusTracker(
            activityMonitor,
            new UserActivityStatusEvaluator(settings.IdleThreshold));

        snoozeDuration = settings.SnoozeDuration;
        userBreakGuideMode = settings.BreakGuideMode;
        reminderOpacity = settings.ReminderOpacity;
        this.foregroundContextProvider = foregroundContextProvider;
        this.applicationRules = new ApplicationRuleSet(applicationRules);
        defaultSuggestionNames = defaultSuggestionProcessNames is not null
            ? new HashSet<string>(defaultSuggestionProcessNames, StringComparer.OrdinalIgnoreCase)
            : [];
        seenSuggestionProcesses = [];

        workCycleTracker = WorkCycleTrackerFactory.Create(settings, this.clock);

        workCycleTracker.ReminderShown += OnReminderShown;
        workCycleTracker.ReminderSuppressed += OnReminderSuppressed;
        workCycleTracker.ReminderLightTouch += OnReminderLightTouch;
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
        UnregisterCancelBreakHotkey();
        EndAudioGuide();
        audioCoordinator = null;

        activityTimer.Stop();

        if (workCycleTracker != null)
        {
            workCycleTracker.ReminderShown -= OnReminderShown;
            workCycleTracker.ReminderSuppressed -= OnReminderSuppressed;
            workCycleTracker.ReminderLightTouch -= OnReminderLightTouch;
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

    public void Pause() => RunCommand("Pause",
        tracker => App.ExecutePause(tracker, CloseReminderIfOpen));

    public void PauseFor(TimeSpan duration) => RunCommand("PauseFor",
        tracker => App.ExecutePauseFor(tracker, duration, CloseReminderIfOpen));

    public void Resume() => RunCommand("Resume", App.ExecuteResume);

    public void StartFocusMode() => RunCommand("StartFocusMode",
        tracker => App.ExecuteStartFocusMode(tracker, CloseReminderIfOpen));

    public void EndFocusMode() => RunCommand("EndFocusMode", App.ExecuteEndFocusMode);

    /// <summary>
    /// Runs a guarded command and refreshes the surfaces if it took effect.
    /// </summary>
    /// <remarks>
    /// The helpers check availability before acting, so a rejected command is a no-op
    /// rather than an exception. The catch remains as a floor for the genuine race — the
    /// phase changing between the check and the call — because no reminder command may
    /// throw out of an event handler.
    /// </remarks>
    /// <returns>True when the command took effect.</returns>
    private bool RunCommand(string name, Func<WorkCycleTracker, bool> command)
    {
        if (workCycleTracker == null) return false;

        try
        {
            if (command(workCycleTracker))
            {
                UpdateCycleStatus();
                return true;
            }

            System.Diagnostics.Trace.TraceWarning(
                $"RestCue: {name} not available in phase {workCycleTracker.CurrentPhase}.");
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"RestCue: {name} rejected — {ex.Message}");
        }

        return false;
    }

    public void Disable() => RunCommand("Disable",
        tracker => App.ExecuteDisable(tracker, CloseReminderIfOpen));

    public void Enable() => RunCommand("Enable", App.ExecuteEnable);

    public void UpdateForegroundContextProvider(bool collectProcessNames)
    {
        foregroundContextProvider = new WindowsForegroundContextProvider(
            collectProcessNames);
    }

    /// <summary>
    /// Applies the settings that do not require rebuilding the reminder engine. Process
    /// name collection comes first because it is a privacy control: a user who has just
    /// switched it off must not have to wait for a relaunch.
    /// </summary>
    /// <remarks>
    /// The engine parameters are deliberately left alone — see
    /// <see cref="RestartRequiredSettings"/> for why rebuilding is not the right answer.
    /// </remarks>
    public void ApplyLiveSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        UpdateForegroundContextProvider(settings.CollectForegroundProcessNames);

        reduceMotion = settings.ReduceMotion;
        userBreakGuideMode = settings.BreakGuideMode;
        reminderOpacity = settings.ReminderOpacity;
        snoozeDuration = settings.SnoozeDuration;

        // The engine holds the snooze deadline, so the label and the behaviour have to move
        // together or the button would promise a duration nothing honours.
        workCycleTracker?.UpdateSnoozeDuration(settings.SnoozeDuration);

        if (reminderWindow != null)
        {
            reminderWindow.ReduceMotion = reduceMotion;
            reminderWindow.ApplySurfaceOpacity(reminderOpacity);
            reminderWindow.SnoozeDuration = snoozeDuration;
        }
    }

    public void UpdateApplicationRules(IEnumerable<ApplicationRule> rules)
    {
        applicationRules = new ApplicationRuleSet(rules);
    }

    /// <summary>
    /// Creates the reminder surface if needed, wiring its full action set once.
    /// </summary>
    /// <remarks>
    /// Wiring used to depend on why the surface was opening — only cancel for a manually
    /// started break, everything for a reminder — so a surface reused across the two cases
    /// showed buttons that did nothing. Wiring once at construction makes that impossible
    /// rather than merely unreachable.
    /// </remarks>
    private void EnsureReminderWindow()
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

        reminderWindow.ReduceMotion = reduceMotion;
        reminderWindow.ApplySurfaceOpacity(reminderOpacity);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// Registers the global break-cancel shortcut.
    /// </summary>
    /// <remarks>
    /// The handle is forced into existence rather than waited for: registration used to be
    /// deferred until the main window was first shown, so a user who only ever used the
    /// tray never got the shortcut at all.
    /// </remarks>
    private void RegisterCancelBreakHotkey()
    {
        if (hotkeyRegistered)
            return;

        IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
        hwndSource = HwndSource.FromHwnd(handle);
        if (hwndSource == null)
        {
            System.Diagnostics.Trace.TraceError(
                "RestCue: no window handle available for the global break-cancel shortcut.");
            return;
        }

        hotkeyRegistered = CancelBreakShortcut.TryRegister(
            () => RegisterHotKey(
                handle,
                CancelBreakHotkeyId,
                CancelBreakShortcut.Modifiers,
                CancelBreakShortcut.VirtualKey),
            message => System.Diagnostics.Trace.TraceError(message));

        if (hotkeyRegistered)
            hwndSource.AddHook(HotKeyHandler);
    }

    private void UnregisterCancelBreakHotkey()
    {
        if (!hotkeyRegistered || hwndSource == null)
            return;

        hwndSource.RemoveHook(HotKeyHandler);
        UnregisterHotKey(hwndSource.Handle, CancelBreakHotkeyId);
        hotkeyRegistered = false;
    }

    private IntPtr HotKeyHandler(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == CancelBreakHotkeyId)
        {
            if (workCycleTracker?.CurrentPhase == WorkCyclePhase.BreakInProgress)
            {
                OnCancelRequested(this, EventArgs.Empty);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void StartBreakNow()
    {
        if (workCycleTracker == null) return;

        if (!RunCommand("BreakNow",
                tracker => App.ExecuteManualStartBreak(tracker, CloseReminderIfOpen)))
            return;

        EnsureReminderWindow();
        reminderWindow!.BreakDuration = workCycleTracker.BreakDuration;
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

    /// <summary>
    /// Applies the availability policy to the main window's menu and buttons. Like the
    /// tray applier, it keeps no phase switch of its own — the policy is the only place
    /// that reasons about phases.
    /// </summary>
    private void UpdateMenuAndButtonStates()
    {
        if (workCycleTracker == null) return;

        ApplyCommandAvailability(
            CommandAvailabilityPolicy.ForPhase(workCycleTracker.CurrentPhase));
    }

    internal void ApplyCommandAvailability(CommandAvailability availability)
    {
        // The timed-pause submenu is an elaboration of the pause command, so it follows
        // pause availability rather than a phase list of its own.
        PauseSubmenu.Visibility = availability.CanPause ? Visibility.Visible : Visibility.Collapsed;
        ResumeMenuItem.Visibility = availability.ShowResume ? Visibility.Visible : Visibility.Collapsed;

        string pauseLabel = availability.ShowResume ? "繼續" : "暫停";
        string focusLabel = availability.ShowEndFocusMode ? "結束專注模式" : "專注模式";
        string disableLabel = availability.ShowEnable ? "啟用提醒" : "停用提醒";

        FocusMenuItem.Header = focusLabel;
        FocusMenuItem.IsEnabled = availability.FocusToggleEnabled;
        DisableMenuItem.Header = disableLabel;
        DisableMenuItem.IsEnabled = availability.DisableToggleEnabled;
        BreakNowMenuItem.IsEnabled = availability.CanBreakNow;

        PauseResumeButton.Content = pauseLabel;
        PauseResumeButton.IsEnabled = availability.PauseToggleEnabled;
        FocusButton.Content = focusLabel;
        FocusButton.IsEnabled = availability.FocusToggleEnabled;
        DisableButton.Content = disableLabel;
        DisableButton.IsEnabled = availability.DisableToggleEnabled;
        BreakNowButton.IsEnabled = availability.CanBreakNow;
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

    /// <summary>
    /// Gives a break that is still running an explicit outcome as the application exits,
    /// through the same cancel seam every other early exit uses.
    /// </summary>
    /// <remarks>
    /// <see cref="StopActivityTracking"/> halts the timers and the audio guide but never
    /// leaves the break phase, so quitting mid-break left a break with no outcome. The
    /// cancel is unconditional because <see cref="WorkCycleTracker.CancelBreak"/> is
    /// already a no-op outside a break, and calling the tracker rather than the window
    /// records the outcome even if the guide window has already gone;
    /// <see cref="CloseReminderIfOpen"/> then finds the phase already changed and does not
    /// cancel a second time.
    /// <para>
    /// Deliberately does not republish the cycle status. The post-cancel phase counts as
    /// continuous work, so announcing it here would open a work session at the moment the
    /// application stops.
    /// </para>
    /// </remarks>
    internal void EndBreakForShutdown()
    {
        workCycleTracker?.CancelBreak();
        CloseReminderIfOpen();
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

        if (rule == null && context.ProcessName is not null &&
            defaultSuggestionNames?.Contains(context.ProcessName) == true &&
            seenSuggestionProcesses?.Add(context.ProcessName) == true)
        {
            SuggestionRequested?.Invoke(this, new SuggestionEventArgs(context.ProcessName));
        }

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
            EnsureReminderWindow();
            reminderWindow!.SnoozeDuration = snoozeDuration;
            reminderWindow.BreakDuration = workCycleTracker!.BreakDuration;
            reminderWindow.ShowReminder();
        });
    }

    private void OnReminderSuppressed(object? sender, ReminderSuppressedEventArgs e)
    {
        Dispatcher.Invoke(() => LowInterruptionReminderRequested?.Invoke(this, e));
    }

    private void OnReminderLightTouch(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => LightTouchReminderRequested?.Invoke(this, EventArgs.Empty));
    }

    private void OnBreakRequested(object? sender, EventArgs e)
    {
        // A reminder dismissed at the same moment as the click leaves nothing to start.
        if (!RunCommand("StartBreak", App.ExecuteStartBreak) || reminderWindow == null)
            return;

        SetupBreakGuideSession(clock ?? new SystemClock(), workCycleTracker!.BreakDuration);
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
            breakGuideSession?.CueChanged -= OnAudioCueChanged;
            audioCoordinator.EndGuide();
        }
    }

    private void OnBreakGuideCueChanged(object? sender, BreakGuideCue cue)
    {
        Dispatcher.Invoke(() =>
        {
            reminderWindow?.PhaseText.Text = RestCue.Core.Reminders.BreakGuideText.ForCue(cue);
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
            reminderWindow?.CompleteBreak();
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
        RunCommand("Snooze", App.ExecuteSnooze);
        CloseReminderIfOpen();
    }

    private void OnIgnoreRequested(object? sender, EventArgs e)
    {
        RunCommand("Ignore", App.ExecuteIgnore);
        CloseReminderIfOpen();
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

    /// <summary>
    /// Publishes the current phase the way the activity timer's tick does, so a test can
    /// drive a transition without waiting on the dispatcher timer.
    /// </summary>
    internal void PublishCycleStatus() => UpdateCycleStatus();

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

        StatusDot.Fill = phase switch
        {
            WorkCyclePhase.Working => (System.Windows.Media.Brush)FindResource("SuccessBrush"),
            WorkCyclePhase.ReminderVisible or WorkCyclePhase.BreakInProgress => (System.Windows.Media.Brush)FindResource("AccentBrush"),
            WorkCyclePhase.PendingReminder or WorkCyclePhase.Snoozed => (System.Windows.Media.Brush)FindResource("WarningBrush"),
            _ => (System.Windows.Media.Brush)FindResource("TextMutedBrush")
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
