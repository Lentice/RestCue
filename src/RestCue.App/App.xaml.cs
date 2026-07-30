using System.Diagnostics;
using System.Windows;
using RestCue.App.Lifecycle;
using RestCue.App.UsageEvents;
using RestCue.Core.Domain;
using RestCue.Core.Events;
using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using RestCue.Core.Time;
using RestCue.Core.Transparency;
using RestCue.Core.UsageEvents;
using RestCue.Infrastructure.Activity;
using RestCue.Infrastructure.Audio;
using RestCue.Infrastructure.Settings;
using RestCue.Infrastructure.Time;
using RestCue.Infrastructure.UsageEvents;

namespace RestCue.App;

public partial class App : System.Windows.Application
{
    private ApplicationLifecycle? _lifecycle;
    private ApplicationStartup? _startup;
    private WindowsTrayIcon? _trayIcon;
    private MainWindow? _statusWindow;
    private BackgroundUsageEventWriter? _eventWriter;
    private IUsageEventRepository? _usageEventRepository;
    private ISettingsRepository? _settingsRepository;
    private IApplicationRuleRepository? _ruleRepository;
    private WorkCycleTracker? _tracker;
    private WindowsBreakGuideAudioPlayer? _audioPlayer;
    private WorkCyclePhase _lastPhase;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _statusWindow = new MainWindow();
        _trayIcon = new WindowsTrayIcon();
        _lifecycle = new ApplicationLifecycle(
            _trayIcon,
            _statusWindow,
            Shutdown);
        _settingsRepository = new SqliteSettingsRepository(
            LocalSettingsPaths.DatabaseFile,
            new AppSettingsValidator());
        _startup = new ApplicationStartup(_settingsRepository, _lifecycle);
        try
        {
            await _startup.InitializeAsync();

            // A stored value that passes validation but that the engine's constructor
            // still refuses would fail on every launch. Degrade to defaults instead.
            AppSettings engineSettings = _startup.ResolveEngineSettings(
                settings => WorkCycleTrackerFactory.Create(settings, new SystemClock()),
                message => Trace.TraceError(message));

            _audioPlayer = new WindowsBreakGuideAudioPlayer();
            _statusWindow.AudioPlayer = _audioPlayer;
            _ruleRepository = new SqliteApplicationRuleRepository(LocalSettingsPaths.DatabaseFile);
            var loadedRules = await _ruleRepository.LoadAllAsync();

            var defaultSuggestionNames = DefaultApplicationRules.All
                .Select(r => r.ProcessName)
                .Except(loadedRules.Select(r => r.ProcessName), StringComparer.OrdinalIgnoreCase);

            _statusWindow.StartActivityTracking(
                new WindowsUserActivityMonitor(),
                engineSettings,
                foregroundContextProvider: new WindowsForegroundContextProvider(
                    engineSettings.CollectForegroundProcessNames),
                applicationRules: loadedRules,
                defaultSuggestionProcessNames: defaultSuggestionNames);

            WireSuggestionPrompting();

            _statusWindow.WireLifecycleEvents();
            WireUsageEventPersistence();
            WireUsageEventEmitters();
            WireTrayCommands();
        }
        catch (Exception exception)
        {
            _eventWriter?.Write(UsageEventType.ErrorOccurred, DateTimeOffset.UtcNow,
                new ErrorOccurredPayload("StartupFailure"));
            ApplicationStartupFailureHandler.Handle(
                exception,
                message => Trace.TraceError(message),
                Shutdown);
        }
    }

    private void OnDispatcherUnhandledException(
        object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        HandleUnhandledException(
            e.Exception,
            message => Trace.TraceError(message),
            () => _eventWriter?.Write(
                UsageEventType.ErrorOccurred,
                DateTimeOffset.UtcNow,
                new ErrorOccurredPayload("UnhandledDispatcherException")));

        e.Handled = true;
    }

    /// <summary>
    /// The application's floor: an unexpected failure is recorded and survived rather than
    /// fatal, so a click that lands on the wrong side of a state change cannot cost the
    /// user their accumulated work time.
    /// </summary>
    /// <remarks>
    /// It records rather than swallowing, and it is a backstop rather than a substitute
    /// for the per-command guards.
    /// </remarks>
    internal static void HandleUnhandledException(
        Exception exception,
        Action<string> logError,
        Action recordDiagnostic)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(logError);
        ArgumentNullException.ThrowIfNull(recordDiagnostic);

        logError($"RestCue: unhandled exception survived — {exception}");

        try
        {
            recordDiagnostic();
        }
        catch (Exception diagnosticFailure)
        {
            // Recording must never be the thing that takes the application down.
            logError($"RestCue: failed to record the unhandled exception — {diagnosticFailure.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;

        _eventWriter?.Write(UsageEventType.AppStopped, DateTimeOffset.UtcNow);
        UnwireUsageEventEmitters();

        if (_statusWindow != null)
        {
            _statusWindow.UnwireLifecycleEvents();
            _statusWindow.StopActivityTracking();
        }
        UnwireUsageEventPersistence();
        _audioPlayer?.Dispose();
        _lifecycle?.Dispose();
        base.OnExit(e);
    }

    internal void WireSettingsCommand(ITrayIcon trayIcon)
    {
        trayIcon.SettingsRequested += (_, _) => OpenSettingsWindow();
    }

    internal void WireDataTransparencyCommand(ITrayIcon trayIcon)
    {
        trayIcon.DataTransparencyRequested += (_, _) => OpenDataTransparencyWindow();
    }

    internal void WireDataManagementCommand(ITrayIcon trayIcon)
    {
        trayIcon.DataManagementRequested += (_, _) => OpenDataManagementWindow();
    }

    internal void WireAboutCommand(ITrayIcon trayIcon)
    {
        trayIcon.AboutRequested += (_, _) => OpenAboutWindow();
    }

    internal void WireStatisticsCommand(ITrayIcon trayIcon)
    {
        trayIcon.StatisticsRequested += (_, _) => OpenStatisticsWindow();
    }

    internal static void WireBreakNowCommand(ITrayIcon trayIcon, IStatusWindow statusWindow)
    {
        trayIcon.BreakNowRequested += (_, _) => statusWindow.StartBreakNow();
    }

    internal static void WireModeCommands(ITrayIcon trayIcon, IStatusWindow statusWindow)
    {
        trayIcon.PauseRequested += (_, _) => statusWindow.Pause();
        trayIcon.PauseForRequested += (_, duration) => statusWindow.PauseFor(duration);
        trayIcon.ResumeRequested += (_, _) => statusWindow.Resume();
        trayIcon.FocusModeRequested += (_, _) => statusWindow.StartFocusMode();
        trayIcon.EndFocusModeRequested += (_, _) => statusWindow.EndFocusMode();
        trayIcon.DisableRequested += (_, _) => statusWindow.Disable();
        trayIcon.EnableRequested += (_, _) => statusWindow.Enable();
    }

    private void WireMainWindowCommands()
    {
        if (_statusWindow == null) return;

        _statusWindow.OpenStatistics = OpenStatisticsWindow;
        _statusWindow.OpenDataTransparency = OpenDataTransparencyWindow;
        _statusWindow.OpenDataManagement = OpenDataManagementWindow;
        _statusWindow.OpenSettings = OpenSettingsWindow;
        _statusWindow.OpenAbout = OpenAboutWindow;
        _statusWindow.ExitApplication = () => _lifecycle?.Exit();
    }

    private void OpenStatisticsWindow()
    {
        if (_usageEventRepository == null)
        {
            Trace.TraceError("RestCue: statistics unavailable, no repository.");
            return;
        }

        new StatisticsWindow(new DailyStatisticsService(_usageEventRepository)).Show();
    }

    private void OpenDataTransparencyWindow()
    {
        if (_usageEventRepository == null || _settingsRepository == null)
        {
            Trace.TraceError("RestCue: data transparency unavailable.");
            return;
        }

        var reader = new SqliteUsageEventMetadataReader(LocalSettingsPaths.DatabaseFile);
        new TransparencyWindow(new DataTransparencyService(
            _settingsRepository, reader, LocalSettingsPaths.DatabaseFile)).Show();
    }

    private void OpenDataManagementWindow()
    {
        if (_usageEventRepository == null || _settingsRepository == null || _startup == null)
        {
            Trace.TraceError("RestCue: data management unavailable.");
            return;
        }

        var window = new DataManagementWindow(_usageEventRepository, _settingsRepository);
        window.DataCleared += (_, _) =>
        {
            foreach (var statisticsWindow in Current.Windows.OfType<StatisticsWindow>())
            {
                statisticsWindow.Close();
            }
            foreach (var transparencyWindow in Current.Windows.OfType<TransparencyWindow>())
            {
                transparencyWindow.Close();
            }
        };
        window.SettingsReset += async (_, _) =>
        {
            var loadResult = await _settingsRepository.LoadAsync();
            _startup.CurrentSettings = loadResult.Settings;
            _statusWindow?.UpdateForegroundContextProvider(
                loadResult.Settings.CollectForegroundProcessNames);
        };
        window.Show();
    }

    private void OpenSettingsWindow()
    {
        if (_startup == null || _settingsRepository == null || _ruleRepository == null)
        {
            Trace.TraceError("RestCue: settings unavailable.");
            return;
        }

        var window = new SettingsWindow(_settingsRepository, _ruleRepository, _startup.CurrentSettings);
        window.ApplicationRulesChanged += OnApplicationRulesChanged;
        window.SettingsSaved += OnSettingsSaved;
        window.Closed += (_, _) =>
        {
            window.ApplicationRulesChanged -= OnApplicationRulesChanged;
            window.SettingsSaved -= OnSettingsSaved;
        };
        window.Show();
    }

    private void OnSettingsSaved(object? sender, AppSettings saved)
    {
        if (_startup == null)
            return;

        _startup.CurrentSettings = saved;
        _statusWindow?.ApplyLiveSettings(saved);
    }

    private async void OnApplicationRulesChanged(object? sender, EventArgs e)
    {
        if (_ruleRepository == null || _statusWindow == null)
            return;

        try
        {
            var loadedRules = await _ruleRepository.LoadAllAsync();
            _statusWindow.UpdateApplicationRules(loadedRules);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"RestCue: failed to reload rules: {ex.Message}");
        }
    }

    private void OpenAboutWindow()
    {
        foreach (var window in Current.Windows.OfType<AboutWindow>())
        {
            window.Activate();
            return;
        }

        var aboutWindow = new AboutWindow
        {
            OpenDataTransparencyRequested = OpenDataTransparencyWindow
        };
        aboutWindow.Show();
    }

    /// <summary>
    /// Enters Pause, guarding before acting: a rejected request must cost the user
    /// nothing, so the reminder surface is only closed once the transition is known to be
    /// legal. Returns false when the command was not available.
    /// </summary>
    internal static bool ExecutePause(WorkCycleTracker tracker, Action closeReminder)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(closeReminder);

        if (!CommandAvailabilityPolicy.ForPhase(tracker.CurrentPhase).CanPause)
            return false;

        closeReminder();
        tracker.Pause();
        return true;
    }

    /// <inheritdoc cref="ExecutePause"/>
    internal static bool ExecutePauseFor(
        WorkCycleTracker tracker, TimeSpan duration, Action closeReminder)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(closeReminder);

        if (!CommandAvailabilityPolicy.ForPhase(tracker.CurrentPhase).CanPause)
            return false;

        closeReminder();
        tracker.Pause(duration);
        return true;
    }

    /// <inheritdoc cref="ExecutePause"/>
    internal static bool ExecuteStartFocusMode(WorkCycleTracker tracker, Action closeReminder)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(closeReminder);

        if (!CommandAvailabilityPolicy.ForPhase(tracker.CurrentPhase).CanStartFocusMode)
            return false;

        closeReminder();
        tracker.StartFocusMode();
        return true;
    }

    /// <inheritdoc cref="ExecutePause"/>
    internal static bool ExecuteEndFocusMode(WorkCycleTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (!CommandAvailabilityPolicy.ForPhase(tracker.CurrentPhase).CanEndFocusMode)
            return false;

        tracker.EndFocusMode();
        return true;
    }

    /// <inheritdoc cref="ExecutePause"/>
    internal static bool ExecuteResume(WorkCycleTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (!CommandAvailabilityPolicy.ForPhase(tracker.CurrentPhase).CanResume)
            return false;

        tracker.Resume();
        return true;
    }

    /// <summary>
    /// Disables reminders. Legal in every phase but one, including during a running break —
    /// so the break is cancelled as an explicit, recorded step rather than silently
    /// dropped when the phase changes.
    /// </summary>
    internal static bool ExecuteDisable(WorkCycleTracker tracker, Action closeReminder)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(closeReminder);

        if (!CommandAvailabilityPolicy.ForPhase(tracker.CurrentPhase).CanDisable)
            return false;

        closeReminder();
        tracker.CancelBreak();
        tracker.Disable();
        return true;
    }

    /// <inheritdoc cref="ExecutePause"/>
    internal static bool ExecuteEnable(WorkCycleTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (!CommandAvailabilityPolicy.ForPhase(tracker.CurrentPhase).CanEnable)
            return false;

        tracker.Enable();
        return true;
    }

    /// <summary>
    /// Starts a break by hand. Guarded like the mode commands, so a click that lands on the
    /// wrong side of a state change is a no-op rather than a crash.
    /// </summary>
    internal static bool ExecuteManualStartBreak(WorkCycleTracker tracker, Action closeReminder)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(closeReminder);

        if (!CommandAvailabilityPolicy.ForPhase(tracker.CurrentPhase).CanBreakNow)
            return false;

        closeReminder();
        tracker.ManualStartBreak();
        return true;
    }

    /// <summary>
    /// Starts a break from a visible reminder. Snooze and Ignore share the same shape: all
    /// three are only legal from ReminderVisible, and none of them may throw out of an
    /// event handler.
    /// </summary>
    internal static bool ExecuteStartBreak(WorkCycleTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (tracker.CurrentPhase != WorkCyclePhase.ReminderVisible)
            return false;

        tracker.StartBreak();
        return true;
    }

    /// <inheritdoc cref="ExecuteStartBreak"/>
    internal static bool ExecuteSnooze(WorkCycleTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (tracker.CurrentPhase != WorkCyclePhase.ReminderVisible)
            return false;

        tracker.Snooze();
        return true;
    }

    /// <inheritdoc cref="ExecuteStartBreak"/>
    internal static bool ExecuteIgnore(WorkCycleTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (tracker.CurrentPhase != WorkCyclePhase.ReminderVisible)
            return false;

        tracker.Ignore();
        return true;
    }

    private void WireTrayCommands()
    {
        if (_trayIcon == null || _statusWindow == null) return;

        WireModeCommands(_trayIcon, _statusWindow);
        WireBreakNowCommand(_trayIcon, _statusWindow);
        WireStatisticsCommand(_trayIcon);
        WireDataTransparencyCommand(_trayIcon);
        WireDataManagementCommand(_trayIcon);
        WireSettingsCommand(_trayIcon);
        WireAboutCommand(_trayIcon);

        WireMainWindowCommands();

        _statusWindow.PhaseChanged += OnPhaseChanged;
        _statusWindow.DebtLevelChanged += OnDebtLevelChanged;
        _statusWindow.LowInterruptionReminderRequested += (_, e) =>
        {
            if (_trayIcon != null)
                ApplySuppressedReminderToTray(_trayIcon, e.ShowTrayCue);
        };

        _statusWindow.LightTouchReminderRequested += (_, _) =>
        {
            if (_trayIcon != null)
                ApplyLightTouchReminderToTray(
                    _trayIcon,
                    _startup?.CurrentSettings.LightTouchSoundEnabled == true,
                    System.Media.SystemSounds.Asterisk.Play);
        };
    }

    private void WireUsageEventEmitters()
    {
        _eventWriter?.Write(UsageEventType.AppStarted, DateTimeOffset.UtcNow);

        if (_tracker != null)
        {
            _tracker.ReminderSuppressed += OnReminderSuppressedEvent;
            _statusWindow!.PhaseChanged += OnPhaseChangedForWorkSession;
        }
    }

    private void UnwireUsageEventEmitters()
    {
        if (_tracker != null)
        {
            _tracker.ReminderSuppressed -= OnReminderSuppressedEvent;
        }
        if (_statusWindow != null)
        {
            _statusWindow.PhaseChanged -= OnPhaseChangedForWorkSession;
        }
    }

    private ISuggestionStore? _suggestionStore;

    private void WireSuggestionPrompting()
    {
        if (_ruleRepository == null || _statusWindow == null) return;

        _suggestionStore = new SqliteSuggestionStore(LocalSettingsPaths.DatabaseFile);

        _statusWindow.SuggestionRequested += OnSuggestionRequested;
    }

    private async void OnSuggestionRequested(object? sender, SuggestionEventArgs e)
    {
        if (_ruleRepository == null || _suggestionStore == null) return;

        var result = System.Windows.MessageBox.Show(
            $"已檢測到「{e.ProcessName}」正在執行。\n\nRestCue 建議為此應用程式套用「僅系統列」規則，避免休息提醒干擾。\n\n要套用此建議嗎？",
            "RestCue – 應用程式規則建議",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            var rule = new ApplicationRule
            {
                ProcessName = e.ProcessName,
                RuleType = ApplicationRuleType.TrayOnly,
            };
            await _ruleRepository.SaveAsync(rule);
            var loadedRules = await _ruleRepository.LoadAllAsync();
            _statusWindow?.UpdateApplicationRules(loadedRules);
        }
        else
        {
            await _suggestionStore.DismissAsync(e.ProcessName);
        }
    }

    private void OnReminderSuppressedEvent(object? sender, ReminderSuppressedEventArgs e)
    {
        _eventWriter?.Write(UsageEventType.ContextSuppressed, DateTimeOffset.UtcNow);
    }

    private bool _wasWorking;

    private void OnPhaseChangedForWorkSession(object? sender, WorkCyclePhase newPhase)
    {
        bool isWorking = ContinuousWorkPolicy.IsContinuousWork(newPhase);
        if (isWorking && !_wasWorking)
        {
            _eventWriter?.Write(UsageEventType.WorkSessionStarted, DateTimeOffset.UtcNow);
        }
        else if (!isWorking && _wasWorking)
        {
            _eventWriter?.Write(UsageEventType.WorkSessionEnded, DateTimeOffset.UtcNow);
        }
        _wasWorking = isWorking;
    }

    private void WireUsageEventPersistence()
    {
        _tracker = _statusWindow?.WorkCycleTracker;
        if (_tracker == null) return;

        IUsageEventRepository? repo = null;
        try
        {
            repo = new SqliteUsageEventRepository(
                LocalSettingsPaths.DatabaseFile);
        }
        catch
        {
            Trace.TraceError("RestCue: failed to create usage event repository.");
            return;
        }

        _usageEventRepository = repo;
        _eventWriter = new BackgroundUsageEventWriter(
            repo,
            msg => Trace.TraceError(msg));

        _tracker.ReminderShown += OnReminderShown;
        _tracker.BreakStarted += OnBreakStartedEvent;
        _tracker.BreakCompleted += OnBreakCompletedEvent;
        _tracker.BreakCancelled += OnBreakCancelledEvent;
        _tracker.PassivePauseDetected += OnPassivePauseDetectedEvent;
        _tracker.ReminderDismissed += OnReminderDismissedEvent;
        _tracker.IdleStarted += OnIdleStarted;
        _tracker.IdleEnded += OnIdleEnded;
        _tracker.CooldownStarted += OnCooldownStarted;
        _tracker.CooldownEnded += OnCooldownEnded;
        _tracker.Paused += OnPausedEvent;
        _tracker.Resumed += OnResumedEvent;
        _tracker.FocusModeStarted += OnFocusModeStartedEvent;
        _tracker.FocusModeEnded += OnFocusModeEndedEvent;
        _tracker.Disabled += OnDisabledEvent;
        _tracker.Enabled += OnEnabledEvent;
        _tracker.RestDebtLevelChanged += OnRestDebtLevelChangedEvent;
        _tracker.ProcessNameChanged += OnProcessNameChangedEvent;
    }

    private void UnwireUsageEventPersistence()
    {
        if (_tracker == null) return;

        _tracker.ReminderShown -= OnReminderShown;
        _tracker.BreakStarted -= OnBreakStartedEvent;
        _tracker.BreakCompleted -= OnBreakCompletedEvent;
        _tracker.BreakCancelled -= OnBreakCancelledEvent;
        _tracker.PassivePauseDetected -= OnPassivePauseDetectedEvent;
        _tracker.ReminderDismissed -= OnReminderDismissedEvent;
        _tracker.IdleStarted -= OnIdleStarted;
        _tracker.IdleEnded -= OnIdleEnded;
        _tracker.CooldownStarted -= OnCooldownStarted;
        _tracker.CooldownEnded -= OnCooldownEnded;
        _tracker.Paused -= OnPausedEvent;
        _tracker.Resumed -= OnResumedEvent;
        _tracker.FocusModeStarted -= OnFocusModeStartedEvent;
        _tracker.FocusModeEnded -= OnFocusModeEndedEvent;
        _tracker.Disabled -= OnDisabledEvent;
        _tracker.Enabled -= OnEnabledEvent;
        _tracker.RestDebtLevelChanged -= OnRestDebtLevelChangedEvent;
        _tracker.ProcessNameChanged -= OnProcessNameChangedEvent;

        _eventWriter?.Dispose();
        _eventWriter = null;
        _tracker = null;
    }

    private void OnReminderShown(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.ReminderShown);
    private void OnBreakStartedEvent(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.BreakStarted);
    private void OnBreakCompletedEvent(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.BreakCompleted);
    private void OnBreakCancelledEvent(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.BreakCancelled);
    private void OnPassivePauseDetectedEvent(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.PassivePauseDetected);
    private void OnIdleStarted(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.IdleStarted);
    private void OnIdleEnded(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.IdleEnded);
    private void OnCooldownStarted(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.CooldownStarted);
    private void OnCooldownEnded(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.CooldownEnded);
    private void OnPausedEvent(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.Paused);
    private void OnResumedEvent(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.Resumed);
    private void OnFocusModeStartedEvent(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.FocusModeStarted);
    private void OnFocusModeEndedEvent(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.FocusModeEnded);
    private void OnDisabledEvent(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.Disabled);
    private void OnEnabledEvent(object? sender, EventArgs e) => WriteUsageEvent(UsageEventType.Enabled);

    private void OnReminderDismissedEvent(object? sender, ReminderDismissedEventArgs e)
    {
        UsageEventType individualType = e.Result switch
        {
            ReminderResult.Snoozed => UsageEventType.ReminderSnoozed,
            ReminderResult.Ignored => UsageEventType.ReminderIgnored,
            ReminderResult.AutoDismissed => UsageEventType.ReminderAutoDismissed,
            _ => UsageEventType.ReminderDismissed,
        };
        _eventWriter?.Write(individualType, DateTimeOffset.UtcNow);
    }

    private void OnRestDebtLevelChangedEvent(object? sender, RestDebtLevelChangedEventArgs e) =>
        _eventWriter?.Write(UsageEventType.RestDebtLevelChanged, DateTimeOffset.UtcNow,
            new RestDebtLevelChangedPayload(e.Previous, e.Current));

    private void OnProcessNameChangedEvent(object? sender, string? processName) =>
        _eventWriter?.Write(UsageEventType.ForegroundProcessChanged, DateTimeOffset.UtcNow,
            new ForegroundProcessChangedPayload(processName ?? string.Empty));

    private void WriteUsageEvent(UsageEventType type) =>
        _eventWriter?.Write(type, DateTimeOffset.UtcNow);

    internal static string GetStatusTextForPhase(WorkCyclePhase phase)
    {
        return phase switch
        {
            WorkCyclePhase.Paused => "RestCue – 已暫停",
            WorkCyclePhase.FocusMode => "RestCue – 專注模式",
            WorkCyclePhase.Disabled => "RestCue – 已停用",
            WorkCyclePhase.Idle => "RestCue – 離開中",
            WorkCyclePhase.BreakInProgress => "RestCue – 休息中",
            _ => "RestCue – Eye Break Reminder"
        };
    }

    internal static string GetStatusTextForDebtLevel(RestDebtLevel level)
    {
        return level switch
        {
            RestDebtLevel.Level1 => "RestCue – 輕微疲勞 (Level 1)",
            RestDebtLevel.Level2 => "RestCue – 明顯疲勞 (Level 2)",
            RestDebtLevel.Level3 => "RestCue – 需要休息 (Level 3)",
            RestDebtLevel.Level4 => "RestCue – 急需休息 (Level 4)",
            _ => "RestCue – 監視中 (Level 0)"
        };
    }

    /// <summary>
    /// Applies the availability policy to the tray menu. There is no phase switch here:
    /// the policy is the only place that reasons about phases, so this surface cannot
    /// drift away from the main window's.
    /// </summary>
    internal static void ApplyPhaseToTray(ITrayIcon tray, WorkCyclePhase phase, RestDebtLevel debtLevel)
    {
        ArgumentNullException.ThrowIfNull(tray);

        CommandAvailability availability = CommandAvailabilityPolicy.ForPhase(phase);

        tray.SetSuppressedState(false);
        tray.SetPauseText(availability.ShowResume);
        tray.SetPauseEnabled(availability.PauseToggleEnabled);
        tray.SetFocusModeText(availability.ShowEndFocusMode);
        tray.SetFocusModeEnabled(availability.FocusToggleEnabled);
        tray.SetDisableText(availability.ShowEnable);
        tray.SetDisableEnabled(availability.DisableToggleEnabled);
        tray.SetBreakNowEnabled(availability.CanBreakNow);
        tray.SetDebtLevel(debtLevel);

        // During an active cycle the debt level is the more useful status; the mode
        // phases name themselves.
        tray.SetStatusText(IsActiveCyclePhase(phase)
            ? GetStatusTextForDebtLevel(debtLevel)
            : GetStatusTextForPhase(phase));
    }

    private static bool IsActiveCyclePhase(WorkCyclePhase phase) =>
        phase is WorkCyclePhase.Working
            or WorkCyclePhase.PendingReminder
            or WorkCyclePhase.ReminderVisible
            or WorkCyclePhase.Snoozed;

    /// <summary>
    /// Handles a reminder that was held back to a low-interruption presentation.
    /// </summary>
    /// <remarks>
    /// A named function rather than an inline closure, so the tray-cue behaviour can be
    /// tested by calling it instead of by a test reimplementing it.
    /// </remarks>
    internal static void ApplySuppressedReminderToTray(ITrayIcon tray, bool showTrayCue)
    {
        ArgumentNullException.ThrowIfNull(tray);

        tray.SetSuppressedState(showTrayCue);
        tray.SetStatusText(showTrayCue
            ? PendingReminderStatusText
            : "RestCue – Eye Break Reminder");
    }

    /// <summary>
    /// Handles a reminder presented at the light-touch tier: tray cue plus a toast, and a
    /// sound only when the user has left it enabled.
    /// </summary>
    internal static void ApplyLightTouchReminderToTray(
        ITrayIcon tray, bool soundEnabled, Action playSound)
    {
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(playSound);

        tray.SetSuppressedState(true);
        tray.SetStatusText(PendingReminderStatusText);
        tray.ShowLightTouchNotification(
            "RestCue – 休息提醒",
            "該休息了！點擊系統列圖示查看詳情。");

        if (soundEnabled)
        {
            playSound();
        }
    }

    internal const string PendingReminderStatusText = "RestCue – 休息提醒待處理";

    private void OnPhaseChanged(object? sender, WorkCyclePhase phase)
    {
        if (_trayIcon == null || _statusWindow == null) return;

        _lastPhase = phase;
        ApplyPhaseToTray(_trayIcon, phase, _statusWindow.CurrentDebtLevel);
    }

    private void OnDebtLevelChanged(object? sender, RestDebtLevelChangedEventArgs e)
    {
        if (_trayIcon == null || _statusWindow == null) return;

        _trayIcon.SetDebtLevel(e.Current);

        if (_lastPhase is WorkCyclePhase.Working or WorkCyclePhase.PendingReminder
            or WorkCyclePhase.ReminderVisible or WorkCyclePhase.Snoozed)
        {
            _trayIcon.SetStatusText(GetStatusTextForDebtLevel(e.Current));
        }
    }
}
