using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using RestCue.App.Lifecycle;
using RestCue.App.UsageEvents;
using RestCue.Core.DataManagement;
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
    private ApplicationLifecycle? lifecycle;
    private SessionInstanceGuard? instanceGuard;
    private ApplicationStartup? startup;
    private WindowsTrayIcon? trayIcon;
    private MainWindow? statusWindow;
    private BackgroundUsageEventWriter? eventWriter;
    private IUsageEventRepository? usageEventRepository;
    private ISettingsRepository? settingsRepository;
    private IApplicationRuleRepository? ruleRepository;
    private WorkCycleTracker? tracker;
    private WindowsBreakGuideAudioPlayer? audioPlayer;
    private WorkCyclePhase lastPhase;
    private bool traySuppressed;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // The single-instance claim comes first: a duplicate must never create the
        // tray, SQLite repository, tracker or any other normal service.
        if (!StartAsPrimaryInstance(
                () => SessionInstanceGuard.Acquire(),
                ShowDuplicateInstanceWarning,
                message => Trace.TraceError(message),
                Shutdown,
                out instanceGuard))
        {
            return;
        }

        statusWindow = new MainWindow();
        trayIcon = new WindowsTrayIcon();

        // Before anything can make the icon visible — InitializeAsync does, as soon as
        // settings have loaded, which is long before the tracker exists.
        ApplyUninitializedToTray(trayIcon);

        lifecycle = new ApplicationLifecycle(
            trayIcon,
            statusWindow,
            Shutdown);
        settingsRepository = new SqliteSettingsRepository(
            LocalSettingsPaths.DatabaseFile,
            new AppSettingsValidator());
        startup = new ApplicationStartup(settingsRepository, lifecycle);
        try
        {
            await startup.InitializeAsync();

            // A stored value that passes validation but that the engine's constructor
            // still refuses would fail on every launch. Degrade to defaults instead.
            AppSettings engineSettings = startup.ResolveEngineSettings(
                settings => WorkCycleTrackerFactory.Create(settings, new SystemClock()),
                message => Trace.TraceError(message));

            audioPlayer = new WindowsBreakGuideAudioPlayer();
            statusWindow.AudioPlayer = audioPlayer;
            ruleRepository = new SqliteApplicationRuleRepository(LocalSettingsPaths.DatabaseFile);
            var loadedRules = await ruleRepository.LoadAllAsync();
            suggestionStore = new SqliteSuggestionStore(LocalSettingsPaths.DatabaseFile);
            var dismissedProcessNames = await suggestionStore.GetDismissedProcessNamesAsync();

            var defaultSuggestionNames = GetDefaultSuggestionProcessNames(
                DefaultApplicationRules.All.Select(r => r.ProcessName),
                loadedRules.Select(r => r.ProcessName),
                dismissedProcessNames);

            statusWindow.StartActivityTracking(
                new WindowsUserActivityMonitor(),
                engineSettings,
                foregroundContextProvider: new WindowsForegroundContextProvider(
                    engineSettings.CollectForegroundProcessNames),
                applicationRules: loadedRules,
                defaultSuggestionProcessNames: defaultSuggestionNames);

            WireSuggestionPrompting();

            statusWindow.WireLifecycleEvents();
            WireUsageEventPersistence();
            WireUsageEventEmitters();
            WireTrayCommands();

            // Last, so that nothing above it can present a command that cannot act. A
            // failure anywhere in this block skips it and the surfaces stay disabled.
            CompleteInitialization();
        }
        catch (Exception exception)
        {
            eventWriter?.Write(UsageEventType.ErrorOccurred, DateTimeOffset.UtcNow,
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
            () => eventWriter?.Write(
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

        ExecuteShutdownSequence(
            endInProgressBreak: () => statusWindow?.EndBreakForShutdown(),
            writeAppStopped: () => eventWriter?.Write(UsageEventType.AppStopped, DateTimeOffset.UtcNow),
            releaseRecordingAndResources: ReleaseShutdownResources,
            logError: message => Trace.TraceError(message));

        base.OnExit(e);
    }

    /// <summary>
    /// Shutdown in the order the stored event log depends on: a break that is still
    /// running gets an explicit outcome, then the application-stopped event, and only
    /// then are the handlers that record them removed.
    /// </summary>
    /// <remarks>
    /// Quitting mid-break used to leave a break-started event with no matching outcome,
    /// because shutdown stopped the timers without ever leaving the break phase. Anything
    /// pairing break events — completion rate, outcome counts, total rest time — was
    /// skewed by every session that ended that way.
    /// <para>
    /// The order is the whole point, and it is why this is a separate testable method
    /// rather than a straight-line body: cancelling after persistence is unwired writes
    /// nothing, and cancelling after the application-stopped event puts the log out of
    /// sequence. Neither failure is visible by reading <see cref="OnExit"/>.
    /// </para>
    /// </remarks>
    internal static void ExecuteShutdownSequence(
        Action endInProgressBreak,
        Action writeAppStopped,
        Action releaseRecordingAndResources,
        Action<string> logError)
    {
        ArgumentNullException.ThrowIfNull(endInProgressBreak);
        ArgumentNullException.ThrowIfNull(writeAppStopped);
        ArgumentNullException.ThrowIfNull(releaseRecordingAndResources);
        ArgumentNullException.ThrowIfNull(logError);

        try
        {
            endInProgressBreak();
        }
        catch (Exception exception)
        {
            // Recording an outcome must never be the thing that stops the process exiting.
            logError($"RestCue: failed to end the in-progress break on shutdown — {exception.Message}");
        }

        writeAppStopped();
        releaseRecordingAndResources();
    }

    /// <summary>
    /// The single-instance gate at the head of startup. A primary claim proceeds;
    /// a duplicate shows the one-button warning and skips all normal services; an
    /// unexpected claim error takes the safe startup-failure path — logged and
    /// exited — never a duplicate verdict.
    /// </summary>
    internal static bool StartAsPrimaryInstance(
        Func<SessionInstanceGuard> acquireGuard,
        Action showDuplicateWarning,
        Action<string> logError,
        Action shutdown,
        out SessionInstanceGuard? guard)
    {
        ArgumentNullException.ThrowIfNull(acquireGuard);
        ArgumentNullException.ThrowIfNull(showDuplicateWarning);
        ArgumentNullException.ThrowIfNull(logError);
        ArgumentNullException.ThrowIfNull(shutdown);

        try
        {
            guard = acquireGuard();
        }
        catch (Exception exception)
        {
            guard = null;
            logError($"RestCue could not claim the single-instance right and will exit. {exception.Message}");
            shutdown();
            return false;
        }

        if (guard.IsPrimary)
        {
            return true;
        }

        guard.Dispose();
        guard = null;
        showDuplicateWarning();
        return false;
    }

    private void ShowDuplicateInstanceWarning()
    {
        var window = new DuplicateInstanceWindow();
        window.Confirmed += (_, _) => Shutdown();
        window.Show();
    }

    private void ReleaseShutdownResources()
    {
        UnwireUsageEventEmitters();

        if (statusWindow != null)
        {
            statusWindow.TrayStatusChanged -= OnTrayStatusChanged;
            statusWindow.UnwireLifecycleEvents();
            statusWindow.StopActivityTracking();
        }
        UnwireUsageEventPersistence();
        audioPlayer?.Dispose();
        instanceGuard?.Dispose();
        lifecycle?.Dispose();
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
        if (statusWindow == null) return;

        statusWindow.OpenStatistics = OpenStatisticsWindow;
        statusWindow.OpenDataTransparency = OpenDataTransparencyWindow;
        statusWindow.OpenDataManagement = OpenDataManagementWindow;
        statusWindow.OpenSettings = OpenSettingsWindow;
        statusWindow.OpenAbout = OpenAboutWindow;
        statusWindow.ExitApplication = () => lifecycle?.Exit();
    }

    private void OpenStatisticsWindow()
    {
        if (usageEventRepository == null)
        {
            Trace.TraceError("RestCue: statistics unavailable, no repository.");
            return;
        }

        new StatisticsWindow(new DailyStatisticsService(usageEventRepository)).Show();
    }

    private void OpenDataTransparencyWindow()
    {
        if (usageEventRepository == null || settingsRepository == null)
        {
            Trace.TraceError("RestCue: data transparency unavailable.");
            return;
        }

        var reader = new SqliteUsageEventMetadataReader(LocalSettingsPaths.DatabaseFile);
        new TransparencyWindow(new DataTransparencyService(
            settingsRepository, reader, LocalSettingsPaths.DatabaseFile)).Show();
    }

    private void OpenDataManagementWindow()
    {
        if (usageEventRepository == null || settingsRepository == null || startup == null)
        {
            Trace.TraceError("RestCue: data management unavailable.");
            return;
        }

        var maintenance = new SqliteUsageDataMaintenance(
            LocalSettingsPaths.DatabaseFile);
        Func<Task<ClearResult>> clearAndRecord = () => ClearAndRecordAsync(
            () => maintenance.ClearUsageHistoryAsync(),
            () => DataManagementWindow.WriteTimestampAsync("last_data_clear_utc"));
        var window = new DataManagementWindow(
            usageEventRepository,
            settingsRepository,
            () => eventWriter == null
                ? clearAndRecord()
                : eventWriter.RunExclusiveAsync(clearAndRecord),
            () => WriteSerializedTimestampAsync("last_data_export_utc"));
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
            var loadResult = await settingsRepository.LoadAsync();
            startup.CurrentSettings = loadResult.Settings;
            statusWindow?.UpdateForegroundContextProvider(
                loadResult.Settings.CollectForegroundProcessNames);
        };
        window.Show();
    }

    /// <summary>
    /// Clears the usage history and, only when the clear succeeded, records the
    /// timestamp. Composed inside the event writer's exclusive channel so the metadata
    /// write cannot race a background event write and fail after the clear succeeded.
    /// </summary>
    internal static async Task<ClearResult> ClearAndRecordAsync(
        Func<Task<ClearResult>> clear,
        Func<Task> recordTimestamp)
    {
        ArgumentNullException.ThrowIfNull(clear);
        ArgumentNullException.ThrowIfNull(recordTimestamp);

        ClearResult result = await clear();
        if (result.Succeeded)
            await recordTimestamp();
        return result;
    }

    private Task WriteSerializedTimestampAsync(string key)
    {
        Func<Task> record = () => DataManagementWindow.WriteTimestampAsync(key);
        return eventWriter == null
            ? record()
            : eventWriter.RunExclusiveAsync(async () =>
            {
                await record();
                return true;
            });
    }

    private void OpenSettingsWindow()
    {
        if (startup == null || settingsRepository == null || ruleRepository == null)
        {
            Trace.TraceError("RestCue: settings unavailable.");
            return;
        }

        var window = new SettingsWindow(settingsRepository, ruleRepository, startup.CurrentSettings);
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
        if (startup == null)
            return;

        startup.CurrentSettings = saved;
        statusWindow?.ApplyLiveSettings(saved);
    }

    private async void OnApplicationRulesChanged(object? sender, EventArgs e)
    {
        if (ruleRepository == null || statusWindow == null)
            return;

        int reload = ++applicationRulesReload;
        try
        {
            var loadedRules = await ruleRepository.LoadAllAsync();
            if (reload == applicationRulesReload)
                statusWindow.UpdateApplicationRules(loadedRules);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"RestCue: failed to reload rules: {ex.Message}");
        }
    }

    private int applicationRulesReload;

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
    /// Enters Pause. Returns false when the command was not available.
    /// </summary>
    /// <remarks>
    /// Guard before effect: a rejected request must cost the user nothing, so the reminder
    /// surface is only closed once the transition is known to be legal. Pausing during a
    /// running break is legal and cancels it — deliberately, and as a recorded step.
    /// </remarks>
    internal static bool ExecutePause(WorkCycleTracker tracker, Action closeReminder) =>
        ExecuteModeEntry(
            tracker,
            closeReminder,
            a => a.CanPause,
            t => t.Pause());

    /// <inheritdoc cref="ExecutePause"/>
    internal static bool ExecutePauseFor(
        WorkCycleTracker tracker, TimeSpan duration, Action closeReminder) =>
        ExecuteModeEntry(
            tracker,
            closeReminder,
            a => a.CanPause,
            t => t.Pause(duration));

    /// <inheritdoc cref="ExecutePause"/>
    internal static bool ExecuteStartFocusMode(WorkCycleTracker tracker, Action closeReminder) =>
        ExecuteModeEntry(
            tracker,
            closeReminder,
            a => a.CanStartFocusMode,
            t => t.StartFocusMode());

    /// <summary>
    /// The shared shape of every mode entry: establish legality, then close the reminder
    /// surface and cancel any running break, then transition. Nothing destructive happens
    /// before the command is known to be available.
    /// </summary>
    private static bool ExecuteModeEntry(
        WorkCycleTracker tracker,
        Action closeReminder,
        Func<CommandAvailability, bool> isAvailable,
        Action<WorkCycleTracker> transition)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(closeReminder);

        WorkCyclePhase phase = tracker.CurrentPhase;
        if (!isAvailable(CommandAvailabilityPolicy.ForPhase(phase)))
            return false;

        closeReminder();

        // CancelBreak is a no-op outside a break, but keeping it explicit is the point:
        // the cancellation is a recorded consequence, not a side effect of the phase
        // changing underneath the break.
        if (CommandAvailabilityPolicy.CancelsRunningBreak(phase))
            tracker.CancelBreak();

        transition(tracker);
        return true;
    }

    /// <inheritdoc cref="ExecutePause"/>
    internal static bool ExecuteEndFocusMode(WorkCycleTracker tracker) =>
        ExecuteGuarded(tracker, a => a.CanEndFocusMode, t => t.EndFocusMode());

    /// <inheritdoc cref="ExecutePause"/>
    internal static bool ExecuteResume(WorkCycleTracker tracker) =>
        ExecuteGuarded(tracker, a => a.CanResume, t => t.Resume());

    /// <summary>
    /// Disables reminders. Legal in every phase but one, including during a running break —
    /// so the break is cancelled as an explicit, recorded step rather than silently
    /// dropped when the phase changes.
    /// </summary>
    internal static bool ExecuteDisable(WorkCycleTracker tracker, Action closeReminder) =>
        ExecuteModeEntry(
            tracker,
            closeReminder,
            a => a.CanDisable,
            t => t.Disable());

    /// <inheritdoc cref="ExecutePause"/>
    internal static bool ExecuteEnable(WorkCycleTracker tracker) =>
        ExecuteGuarded(tracker, a => a.CanEnable, t => t.Enable());

    /// <summary>
    /// Starts a break by hand. Guarded like the mode commands, so a click that lands on the
    /// wrong side of a state change is a no-op rather than a crash.
    /// </summary>
    internal static bool ExecuteManualStartBreak(WorkCycleTracker tracker, Action closeReminder) =>
        ExecuteModeEntry(
            tracker,
            closeReminder,
            a => a.CanBreakNow,
            t => t.ManualStartBreak());

    /// <summary>
    /// Starts a break from a visible reminder. Snooze and Ignore share the same shape: all
    /// three are only legal from ReminderVisible, and none of them may throw out of an
    /// event handler.
    /// </summary>
    internal static bool ExecuteStartBreak(WorkCycleTracker tracker) =>
        ExecuteGuarded(tracker, a => a.CanStartBreakFromReminder, t => t.StartBreak());

    /// <inheritdoc cref="ExecuteStartBreak"/>
    internal static bool ExecuteSnooze(WorkCycleTracker tracker) =>
        ExecuteGuarded(tracker, a => a.CanSnooze, t => t.Snooze());

    /// <inheritdoc cref="ExecuteStartBreak"/>
    internal static bool ExecuteIgnore(WorkCycleTracker tracker) =>
        ExecuteGuarded(tracker, a => a.CanIgnore, t => t.Ignore());

    /// <summary>
    /// Runs a command that has no preparatory step, asking the availability policy — never
    /// its own phase comparison — whether it is legal.
    /// </summary>
    private static bool ExecuteGuarded(
        WorkCycleTracker tracker,
        Func<CommandAvailability, bool> isAvailable,
        Action<WorkCycleTracker> operation)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (!isAvailable(CommandAvailabilityPolicy.ForPhase(tracker.CurrentPhase)))
            return false;

        operation(tracker);
        return true;
    }

    /// <summary>
    /// The point at which the interface becomes live: the tracker exists, its commands are
    /// wired, and both surfaces are given the tracker's actual opening state.
    /// </summary>
    /// <remarks>
    /// State is applied rather than awaited. The tracker publishes its opening phase at the
    /// end of <see cref="MainWindow.StartActivityTracking"/>, before
    /// <see cref="WireTrayCommands"/> subscribes to phase changes, so the tray never saw it
    /// and kept its placeholder tooltip until some later phase or rest-debt change happened
    /// to occur. Reading the tracker directly does not depend on having won that race.
    /// </remarks>
    private void CompleteInitialization()
    {
        WorkCycleTracker? currentTracker = statusWindow?.WorkCycleTracker;
        if (currentTracker == null || trayIcon == null || statusWindow == null) return;

        statusWindow.CompleteCommandInitialization();

        lastPhase = currentTracker.CurrentPhase;
        ApplyPhaseToTray(
            trayIcon,
            lastPhase,
            statusWindow.CurrentDebtLevel,
            lastPhase == WorkCyclePhase.Working ? statusWindow.CurrentTrayStatusText : null);
    }

    private void WireTrayCommands()
    {
        if (trayIcon == null || statusWindow == null) return;

        WireModeCommands(trayIcon, statusWindow);
        WireBreakNowCommand(trayIcon, statusWindow);
        WireStatisticsCommand(trayIcon);
        WireDataTransparencyCommand(trayIcon);
        WireDataManagementCommand(trayIcon);
        WireSettingsCommand(trayIcon);
        WireAboutCommand(trayIcon);

        WireMainWindowCommands();

        statusWindow.PhaseChanged += OnPhaseChanged;
        statusWindow.DebtLevelChanged += OnDebtLevelChanged;
        statusWindow.TrayStatusChanged += OnTrayStatusChanged;
        statusWindow.LowInterruptionReminderRequested += (_, e) =>
        {
            if (trayIcon == null) return;
            traySuppressed = e.ShowTrayCue;
            ApplySuppressedReminderToTray(
                trayIcon,
                e.ShowTrayCue,
                statusWindow?.CurrentDebtLevel ?? RestDebtLevel.Level0,
                lastPhase);
        };

        statusWindow.LightTouchReminderRequested += (_, _) =>
        {
            if (trayIcon == null) return;
            traySuppressed = true;
            ApplyLightTouchReminderToTray(
                trayIcon,
                startup?.CurrentSettings.LightTouchSoundEnabled == true,
                System.Media.SystemSounds.Asterisk.Play,
                startup?.CurrentSettings.NotificationDuration ?? NotificationDuration.Default,
                statusWindow?.CurrentDebtLevel ?? RestDebtLevel.Level0,
                lastPhase);
        };
    }

    private void WireUsageEventEmitters()
    {
        eventWriter?.Write(UsageEventType.AppStarted, DateTimeOffset.UtcNow);

        if (tracker != null)
        {
            tracker.ReminderSuppressed += OnReminderSuppressedEvent;

            // Attached after the status window has already published its opening phase.
            // The recorder seeds itself from that phase rather than assuming no work is
            // in progress, so the first work session gets a real start boundary.
            workSessionRecorder = new WorkSessionRecorder(
                type => eventWriter?.Write(type, DateTimeOffset.UtcNow));
            workSessionRecorder.Attach(statusWindow!);
        }
    }

    private void UnwireUsageEventEmitters()
    {
        tracker?.ReminderSuppressed -= OnReminderSuppressedEvent;
        workSessionRecorder?.Detach();
    }

    private ISuggestionStore? suggestionStore;
    private SuggestionWindow? suggestionWindow;

    private void WireSuggestionPrompting()
    {
        if (ruleRepository == null || statusWindow == null) return;

        suggestionStore ??= new SqliteSuggestionStore(LocalSettingsPaths.DatabaseFile);

        statusWindow.SuggestionRequested += OnSuggestionRequested;
    }

    /// <summary>
    /// The default application-rule suggestions to offer at startup: the defaults that are
    /// neither already a persisted rule nor a suggestion the user has dismissed. Matching
    /// is case-insensitive because process names are compared that way everywhere else.
    /// </summary>
    internal static IReadOnlySet<string> GetDefaultSuggestionProcessNames(
        IEnumerable<string> defaultProcessNames,
        IEnumerable<string> loadedRuleProcessNames,
        IEnumerable<string> dismissedProcessNames)
    {
        ArgumentNullException.ThrowIfNull(defaultProcessNames);
        ArgumentNullException.ThrowIfNull(loadedRuleProcessNames);
        ArgumentNullException.ThrowIfNull(dismissedProcessNames);

        var excluded = new HashSet<string>(loadedRuleProcessNames, StringComparer.OrdinalIgnoreCase);
        excluded.UnionWith(dismissedProcessNames);

        return defaultProcessNames
            .Where(name => !excluded.Contains(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void OnSuggestionRequested(object? sender, SuggestionEventArgs e)
    {
        if (ruleRepository == null || suggestionStore == null) return;

        // The polling path can raise this for a different default process while a
        // suggestion is already showing. One suggestion at a time: ignore the rest.
        if (suggestionWindow?.IsVisible == true) return;

        var window = new SuggestionWindow();
        suggestionWindow = window;
        window.Approved += async (_, _) =>
        {
            try
            {
                await ApproveSuggestionAsync(
                    ruleRepository,
                    rules => statusWindow?.UpdateApplicationRules(rules),
                    e.ProcessName);
            }
            finally
            {
                window.Close();
            }
        };
        window.Dismissed += async (_, _) =>
        {
            try
            {
                await DismissSuggestionAsync(suggestionStore, e.ProcessName);
            }
            finally
            {
                window.Close();
            }
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(suggestionWindow, window))
                suggestionWindow = null;
        };
        window.ShowSuggestion(e.ProcessName);
    }

    /// <summary>
    /// Persists the suggestion as a TrayOnly rule and re-applies the rule set to the
    /// status surface, so the suggestion is not offered again for this process.
    /// </summary>
    internal static async Task ApproveSuggestionAsync(
        IApplicationRuleRepository ruleRepository,
        Action<IReadOnlyList<ApplicationRule>>? applyRules,
        string processName)
    {
        ArgumentNullException.ThrowIfNull(ruleRepository);
        ArgumentNullException.ThrowIfNull(processName);

        var rule = new ApplicationRule
        {
            ProcessName = processName,
            RuleType = ApplicationRuleType.TrayOnly,
        };
        await ruleRepository.SaveAsync(rule);
        var loadedRules = await ruleRepository.LoadAllAsync();
        applyRules?.Invoke(loadedRules);
    }

    /// <summary>
    /// Records the dismissal so the suggestion is not offered again for this process.
    /// </summary>
    internal static async Task DismissSuggestionAsync(
        ISuggestionStore suggestionStore,
        string processName)
    {
        ArgumentNullException.ThrowIfNull(suggestionStore);
        ArgumentNullException.ThrowIfNull(processName);

        await suggestionStore.DismissAsync(processName);
    }

    private void OnReminderSuppressedEvent(object? sender, ReminderSuppressedEventArgs e)
    {
        eventWriter?.Write(UsageEventType.ContextSuppressed, DateTimeOffset.UtcNow);
    }

    private WorkSessionRecorder? workSessionRecorder;

    private void WireUsageEventPersistence()
    {
        tracker = statusWindow?.WorkCycleTracker;
        if (tracker == null) return;

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

        usageEventRepository = repo;
        eventWriter = new BackgroundUsageEventWriter(
            repo,
            msg => Trace.TraceError(msg));

        tracker.ReminderShown += OnReminderShown;
        tracker.BreakStarted += OnBreakStartedEvent;
        tracker.BreakCompleted += OnBreakCompletedEvent;
        tracker.BreakCancelled += OnBreakCancelledEvent;
        tracker.PassivePauseDetected += OnPassivePauseDetectedEvent;
        tracker.ReminderDismissed += OnReminderDismissedEvent;
        tracker.IdleStarted += OnIdleStarted;
        tracker.IdleEnded += OnIdleEnded;
        tracker.CooldownStarted += OnCooldownStarted;
        tracker.CooldownEnded += OnCooldownEnded;
        tracker.Paused += OnPausedEvent;
        tracker.Resumed += OnResumedEvent;
        tracker.FocusModeStarted += OnFocusModeStartedEvent;
        tracker.FocusModeEnded += OnFocusModeEndedEvent;
        tracker.Disabled += OnDisabledEvent;
        tracker.Enabled += OnEnabledEvent;
        tracker.RestDebtLevelChanged += OnRestDebtLevelChangedEvent;
        tracker.ProcessNameChanged += OnProcessNameChangedEvent;
    }

    private void UnwireUsageEventPersistence()
    {
        if (tracker == null) return;

        tracker.ReminderShown -= OnReminderShown;
        tracker.BreakStarted -= OnBreakStartedEvent;
        tracker.BreakCompleted -= OnBreakCompletedEvent;
        tracker.BreakCancelled -= OnBreakCancelledEvent;
        tracker.PassivePauseDetected -= OnPassivePauseDetectedEvent;
        tracker.ReminderDismissed -= OnReminderDismissedEvent;
        tracker.IdleStarted -= OnIdleStarted;
        tracker.IdleEnded -= OnIdleEnded;
        tracker.CooldownStarted -= OnCooldownStarted;
        tracker.CooldownEnded -= OnCooldownEnded;
        tracker.Paused -= OnPausedEvent;
        tracker.Resumed -= OnResumedEvent;
        tracker.FocusModeStarted -= OnFocusModeStartedEvent;
        tracker.FocusModeEnded -= OnFocusModeEndedEvent;
        tracker.Disabled -= OnDisabledEvent;
        tracker.Enabled -= OnEnabledEvent;
        tracker.RestDebtLevelChanged -= OnRestDebtLevelChangedEvent;
        tracker.ProcessNameChanged -= OnProcessNameChangedEvent;

        eventWriter?.Dispose();
        eventWriter = null;
        tracker = null;
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
        eventWriter?.Write(individualType, DateTimeOffset.UtcNow);
    }

    private void OnRestDebtLevelChangedEvent(object? sender, RestDebtLevelChangedEventArgs e) =>
        eventWriter?.Write(UsageEventType.RestDebtLevelChanged, DateTimeOffset.UtcNow,
            new RestDebtLevelChangedPayload(e.Previous, e.Current));

    private void OnProcessNameChangedEvent(object? sender, string? processName) =>
        eventWriter?.Write(UsageEventType.ForegroundProcessChanged, DateTimeOffset.UtcNow,
            new ForegroundProcessChangedPayload(processName ?? string.Empty));

    private void WriteUsageEvent(UsageEventType type) =>
        eventWriter?.Write(type, DateTimeOffset.UtcNow);

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

    internal static string GetStatusTextForDebtLevel(RestDebtLevel level) =>
        $"RestCue – {GetDebtLevelLabel(level)}";

    internal static string GetDebtLevelLabel(RestDebtLevel level)
    {
        return level switch
        {
            RestDebtLevel.Level1 => "輕微疲勞 (Level 1)",
            RestDebtLevel.Level2 => "明顯疲勞 (Level 2)",
            RestDebtLevel.Level3 => "需要休息 (Level 3)",
            RestDebtLevel.Level4 => "急需休息 (Level 4)",
            _ => "監視中 (Level 0)"
        };
    }

    /// <summary>
    /// The single derivation of the tray tooltip from the tray's view state, so the real
    /// tray icon, the appliers and the tests cannot disagree. Names the mode and the
    /// rest-debt level together when both are meaningful.
    /// </summary>
    internal static string GetTrayTooltip(TrayViewState state)
    {
        if (state.IsSuppressed)
            return GetPendingReminderStatusText(state.DebtLevel);

        if (CommandAvailabilityPolicy.IsActiveCycle(state.Mode))
            return GetStatusTextForDebtLevel(state.DebtLevel);

        string modeText = GetStatusTextForPhase(state.Mode);
        return state.DebtLevel == RestDebtLevel.Level0
            ? modeText
            : $"{modeText} · {GetDebtLevelLabel(state.DebtLevel)}";
    }

    /// <summary>
    /// Applies the availability policy to the tray menu. There is no phase switch here:
    /// the policy is the only place that reasons about phases, so this surface cannot
    /// drift away from the main window's.
    /// </summary>
    internal static void ApplyPhaseToTray(
        ITrayIcon tray,
        WorkCyclePhase phase,
        RestDebtLevel debtLevel,
        string? statusText = null)
    {
        ArgumentNullException.ThrowIfNull(tray);

        tray.ApplyViewState(new TrayViewState(phase, debtLevel, IsSuppressed: false));
        ApplyAvailabilityToTray(tray, CommandAvailabilityPolicy.ForPhase(phase));

        // The only override is the live status summary shown while working; everywhere
        // else the tooltip comes from the view state, so mode and debt stay together.
        if (statusText != null)
        {
            tray.SetStatusText(statusText);
        }
    }

    private static void ApplyAvailabilityToTray(ITrayIcon tray, CommandAvailability availability)
    {
        tray.SetPauseText(availability.ShowResume);
        tray.SetPauseEnabled(availability.PauseToggleEnabled);
        tray.SetFocusModeText(availability.ShowEndFocusMode);
        tray.SetFocusModeEnabled(availability.FocusToggleEnabled);
        tray.SetDisableText(availability.ShowEnable);
        tray.SetDisableEnabled(availability.DisableToggleEnabled);
        tray.SetBreakNowEnabled(availability.CanBreakNow);
    }

    internal const string StartingUpStatusText = "RestCue – 啟動中";

    /// <summary>
    /// The tray's opening state: visible, but offering nothing it cannot yet do.
    /// </summary>
    /// <remarks>
    /// The icon is made visible as soon as settings have loaded, which is before the work
    /// cycle exists and well before the tray commands are wired. A user who clicked a
    /// command in that window got nothing — no break, no pause, no error — because the
    /// command runner returned silently against a missing tracker. Applied before the
    /// icon can appear, so the dead interface never exists.
    /// <para>
    /// The tooltip is replaced too: the constructor's generic product name said nothing
    /// about what the application was doing.
    /// </para>
    /// </remarks>
    internal static void ApplyUninitializedToTray(ITrayIcon tray)
    {
        ArgumentNullException.ThrowIfNull(tray);

        ApplyAvailabilityToTray(tray, CommandAvailabilityPolicy.None);
        tray.SetStatusText(StartingUpStatusText);
    }

    /// <summary>
    /// Handles a reminder that was held back to a low-interruption presentation.
    /// </summary>
    /// <remarks>
    /// A named function rather than an inline closure, so the tray-cue behaviour can be
    /// tested by calling it instead of by a test reimplementing it.
    /// </remarks>
    internal static void ApplySuppressedReminderToTray(
        ITrayIcon tray,
        bool showTrayCue,
        RestDebtLevel level = RestDebtLevel.Level0,
        WorkCyclePhase phase = WorkCyclePhase.PendingReminder)
    {
        ArgumentNullException.ThrowIfNull(tray);

        tray.ApplyViewState(new TrayViewState(phase, level, showTrayCue));
    }

    /// <summary>
    /// Handles a reminder presented at the light-touch tier: tray cue plus a toast, and a
    /// sound only when the user has left it enabled.
    /// </summary>
    internal static void ApplyLightTouchReminderToTray(
        ITrayIcon tray, bool soundEnabled, Action playSound,
        NotificationDuration duration = NotificationDuration.Default,
        RestDebtLevel level = RestDebtLevel.Level0,
        WorkCyclePhase phase = WorkCyclePhase.PendingReminder)
    {
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(playSound);

        tray.ApplyViewState(new TrayViewState(phase, level, IsSuppressed: true));
        tray.ShowLightTouchNotification(
            "RestCue – 休息提醒",
            "該休息了！請查看 RestCue 系統列狀態。",
            duration,
            level);

        if (soundEnabled)
        {
            playSound();
        }
    }

    /// <summary>
    /// Re-applies the tray view state for a rest-debt change. Deliberately not gated on
    /// the phase: debt climbing must refresh the tooltip in every phase, including Focus
    /// Mode, Pause and Disabled, not just while an active cycle is running.
    /// </summary>
    internal static void ApplyDebtLevelChangeToTray(
        ITrayIcon tray, WorkCyclePhase phase, RestDebtLevel level, bool isSuppressed)
    {
        ArgumentNullException.ThrowIfNull(tray);

        tray.ApplyViewState(new TrayViewState(phase, level, isSuppressed));
    }

    internal const string PendingReminderStatusText = "RestCue – 休息提醒待處理";

    /// <summary>
    /// Keeps the debt level visible in the tooltip while a reminder is pending, so the
    /// pending cue does not erase the only textual read-out of the level.
    /// </summary>
    internal static string GetPendingReminderStatusText(RestDebtLevel level) =>
        level == RestDebtLevel.Level0
            ? PendingReminderStatusText
            : $"{GetStatusTextForDebtLevel(level)} · 休息提醒待處理";

    private void OnPhaseChanged(object? sender, WorkCyclePhase phase)
    {
        if (trayIcon == null || statusWindow == null) return;

        lastPhase = phase;
        traySuppressed = false;
        ApplyPhaseToTray(
            trayIcon,
            phase,
            statusWindow.CurrentDebtLevel,
            phase == WorkCyclePhase.Working ? statusWindow.CurrentTrayStatusText : null);
    }

    private void OnTrayStatusChanged(object? sender, EventArgs e)
    {
        if (trayIcon == null || statusWindow == null) return;

        if (lastPhase == WorkCyclePhase.Working)
        {
            trayIcon.SetStatusText(statusWindow.CurrentTrayStatusText);
        }
    }

    private void OnDebtLevelChanged(object? sender, RestDebtLevelChangedEventArgs e)
    {
        if (trayIcon == null || statusWindow == null) return;

        ApplyDebtLevelChangeToTray(trayIcon, lastPhase, e.Current, traySuppressed);

        // Debt level rising matters regardless of idle/paused state, so this is
        // deliberately outside the active-cycle check.
        //
        // Deferred to background priority on purpose: Level 1 is crossed in the very
        // tick that ends the work interval, and the phase change that follows still
        // reassigns the tray icon. Reassigning NotifyIcon.Icon cancels a balloon that
        // has not been presented yet, so the balloon must be the last tray write of
        // the tick.
        ITrayIcon tray = trayIcon;
        bool enabled = startup?.CurrentSettings.DebtLevelTrayNotificationEnabled == true;
        NotificationDuration duration =
            startup?.CurrentSettings.NotificationDuration ?? NotificationDuration.Default;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => ApplyDebtLevelNotificationToTray(tray, e.Current, enabled, duration));
    }

    internal static void ApplyDebtLevelNotificationToTray(
        ITrayIcon tray, RestDebtLevel level, bool showNotification,
        NotificationDuration duration = NotificationDuration.Default)
    {
        ArgumentNullException.ThrowIfNull(tray);

        if (!showNotification || level == RestDebtLevel.Level0)
            return;

        tray.ShowLightTouchNotification(
            GetStatusTextForDebtLevel(level),
            "休息需求已提升，建議安排短暫休息。請查看 RestCue 系統列狀態。",
            duration,
            level);
    }
}
