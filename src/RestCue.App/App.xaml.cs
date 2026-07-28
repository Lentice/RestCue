using System.Diagnostics;
using System.Windows;
using RestCue.App.Lifecycle;
using RestCue.Core.Domain;
using RestCue.Core.Events;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using RestCue.Infrastructure.Activity;
using RestCue.Infrastructure.Settings;

namespace RestCue.App;

public partial class App : System.Windows.Application
{
    private ApplicationLifecycle? _lifecycle;
    private ApplicationStartup? _startup;
    private WindowsTrayIcon? _trayIcon;
    private MainWindow? _statusWindow;
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
            _statusWindow.StartActivityTracking(
                new WindowsUserActivityMonitor(),
                _startup.CurrentSettings,
                foregroundContextProvider: new WindowsForegroundContextProvider(
                    _startup.CurrentSettings.CollectForegroundProcessNames),
                applicationRules: RestCue.Core.Reminders.DefaultApplicationRules.All);

            _statusWindow.WireLifecycleEvents();
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
        _lifecycle?.Dispose();
        base.OnExit(e);
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
