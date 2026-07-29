using System.Diagnostics;
using System.Windows;
using RestCue.App.Lifecycle;
using RestCue.App.UsageEvents;
using RestCue.Core.Domain;
using RestCue.Core.Events;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using RestCue.Core.UsageEvents;
using RestCue.Infrastructure.Activity;
using RestCue.Infrastructure.Audio;
using RestCue.Infrastructure.Settings;
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
    private WorkCycleTracker? _tracker;
    private WindowsBreakGuideAudioPlayer? _audioPlayer;
    private WorkCyclePhase _lastPhase;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _statusWindow = new MainWindow();
        _trayIcon = new WindowsTrayIcon();
        _lifecycle = new ApplicationLifecycle(
            _trayIcon,
            _statusWindow,
            Shutdown);
        var settingsRepository = new SqliteSettingsRepository(
            LocalSettingsPaths.DatabaseFile,
            new AppSettingsValidator());
        _startup = new ApplicationStartup(settingsRepository, _lifecycle);
        try
        {
            await _startup.InitializeAsync();
            _audioPlayer = new WindowsBreakGuideAudioPlayer();
            _statusWindow.AudioPlayer = _audioPlayer;
            _statusWindow.StartActivityTracking(
                new WindowsUserActivityMonitor(),
                _startup.CurrentSettings,
                foregroundContextProvider: new WindowsForegroundContextProvider(
                    _startup.CurrentSettings.CollectForegroundProcessNames),
                applicationRules: RestCue.Core.Reminders.DefaultApplicationRules.All);

            _statusWindow.WireLifecycleEvents();
            WireUsageEventPersistence();
            WireTrayCommands();
        }
        catch (Exception exception)
        {
            ApplicationStartupFailureHandler.Handle(
                exception,
                message => Trace.TraceError(message),
                Shutdown);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
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

    internal void WireStatisticsCommand(ITrayIcon trayIcon)
    {
        trayIcon.StatisticsRequested += (_, _) =>
        {
            if (_usageEventRepository == null)
            {
                Trace.TraceError("RestCue: statistics unavailable, no repository.");
                return;
            }

            var window = new StatisticsWindow(
                new DailyStatisticsService(_usageEventRepository));
            window.Show();
        };
    }

    internal static void WireBreakNowCommand(ITrayIcon trayIcon, IStatusWindow statusWindow)
    {
        trayIcon.BreakNowRequested += (_, _) => statusWindow.StartBreakNow();
    }

    internal static void WireModeCommands(ITrayIcon trayIcon, IStatusWindow statusWindow)
    {
        trayIcon.PauseRequested += (_, _) => statusWindow.Pause();
        trayIcon.ResumeRequested += (_, _) => statusWindow.Resume();
        trayIcon.FocusModeRequested += (_, _) => statusWindow.StartFocusMode();
        trayIcon.EndFocusModeRequested += (_, _) => statusWindow.EndFocusMode();
        trayIcon.DisableRequested += (_, _) => statusWindow.Disable();
        trayIcon.EnableRequested += (_, _) => statusWindow.Enable();
    }

    internal static void ExecutePause(WorkCycleTracker tracker, Action closeReminder)
    {
        closeReminder();
        tracker.Pause();
    }

    internal static void ExecuteStartFocusMode(WorkCycleTracker tracker, Action closeReminder)
    {
        closeReminder();
        tracker.StartFocusMode();
    }

    private void WireTrayCommands()
    {
        if (_trayIcon == null || _statusWindow == null) return;

        WireModeCommands(_trayIcon, _statusWindow);
        WireBreakNowCommand(_trayIcon, _statusWindow);
        WireStatisticsCommand(_trayIcon);

        _statusWindow.PhaseChanged += OnPhaseChanged;
        _statusWindow.DebtLevelChanged += OnDebtLevelChanged;
        _statusWindow.LowInterruptionReminderRequested += (_, e) =>
        {
            if (e.ShowTrayCue)
            {
                _trayIcon?.SetSuppressedState(true);
                _trayIcon?.SetStatusText("RestCue – 休息提醒待處理");
            }
            else
            {
                _trayIcon?.SetSuppressedState(false);
                _trayIcon?.SetStatusText("RestCue – Eye Break Reminder");
            }
        };
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

    private void OnReminderDismissedEvent(object? sender, ReminderDismissedEventArgs e) =>
        _eventWriter?.Write(UsageEventType.ReminderDismissed, DateTimeOffset.UtcNow,
            new ReminderDismissedPayload(e.Result));

    private void OnRestDebtLevelChangedEvent(object? sender, RestDebtLevelChangedEventArgs e) =>
        _eventWriter?.Write(UsageEventType.RestDebtLevelChanged, DateTimeOffset.UtcNow,
            new RestDebtLevelChangedPayload(e.Previous, e.Current));

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

    internal static void ApplyPhaseToTray(ITrayIcon tray, WorkCyclePhase phase, RestDebtLevel debtLevel)
    {
        tray.SetSuppressedState(false);
        tray.SetPauseText(false);
        tray.SetPauseEnabled(true);
        tray.SetFocusModeText(false);
        tray.SetFocusModeEnabled(true);
        tray.SetDisableText(false);
        tray.SetDisableEnabled(true);
        tray.SetBreakNowEnabled(true);
        tray.SetDebtLevel(debtLevel);
        tray.SetStatusText(GetStatusTextForPhase(phase));

        switch (phase)
        {
            case WorkCyclePhase.Paused:
                tray.SetPauseText(true);
                tray.SetFocusModeEnabled(false);
                tray.SetBreakNowEnabled(false);
                tray.SetStatusText("RestCue – 已暫停");
                break;

            case WorkCyclePhase.FocusMode:
                tray.SetFocusModeText(true);
                tray.SetPauseEnabled(false);
                tray.SetStatusText("RestCue – 專注模式");
                break;

            case WorkCyclePhase.Disabled:
                tray.SetDisableText(true);
                tray.SetPauseEnabled(false);
                tray.SetFocusModeEnabled(false);
                tray.SetBreakNowEnabled(false);
                tray.SetStatusText("RestCue – 已停用");
                break;

            case WorkCyclePhase.BreakInProgress:
                tray.SetPauseEnabled(false);
                tray.SetFocusModeEnabled(false);
                tray.SetBreakNowEnabled(false);
                tray.SetStatusText("RestCue – 休息中");
                break;

            case WorkCyclePhase.Idle:
                tray.SetFocusModeEnabled(false);
                tray.SetBreakNowEnabled(false);
                tray.SetStatusText("RestCue – 離開中");
                break;

            case WorkCyclePhase.Working:
            case WorkCyclePhase.PendingReminder:
            case WorkCyclePhase.ReminderVisible:
            case WorkCyclePhase.Snoozed:
                tray.SetStatusText(GetStatusTextForDebtLevel(debtLevel));
                break;
        }
    }

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
