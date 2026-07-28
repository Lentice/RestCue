using System.Diagnostics;
using System.Windows;
using RestCue.App.Lifecycle;
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

    private void WireTrayCommands()
    {
        if (_trayIcon == null || _statusWindow == null) return;

        _trayIcon.PauseRequested += (_, _) => _statusWindow.Pause();
        _trayIcon.ResumeRequested += (_, _) => _statusWindow.Resume();
        _trayIcon.FocusModeRequested += (_, _) => _statusWindow.StartFocusMode();
        _trayIcon.EndFocusModeRequested += (_, _) => _statusWindow.EndFocusMode();
        _trayIcon.DisableRequested += (_, _) => _statusWindow.Disable();
        _trayIcon.EnableRequested += (_, _) => _statusWindow.Enable();
        WireBreakNowCommand(_trayIcon, _statusWindow);

        _statusWindow.PhaseChanged += OnPhaseChanged;
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

    private void OnPhaseChanged(object? sender, WorkCyclePhase phase)
    {
        if (_trayIcon == null) return;

        _trayIcon.SetSuppressedState(false);
        _trayIcon.SetPauseText(false);
        _trayIcon.SetPauseEnabled(true);
        _trayIcon.SetFocusModeText(false);
        _trayIcon.SetFocusModeEnabled(true);
        _trayIcon.SetDisableText(false);
        _trayIcon.SetDisableEnabled(true);
        _trayIcon.SetBreakNowEnabled(true);
        _trayIcon.SetStatusText("RestCue – Eye Break Reminder");

        switch (phase)
        {
            case WorkCyclePhase.Paused:
                _trayIcon.SetPauseText(true);
                _trayIcon.SetFocusModeEnabled(false);
                _trayIcon.SetBreakNowEnabled(false);
                _trayIcon.SetStatusText("RestCue – 已暫停");
                break;

            case WorkCyclePhase.FocusMode:
                _trayIcon.SetFocusModeText(true);
                _trayIcon.SetPauseEnabled(false);
                _trayIcon.SetStatusText("RestCue – 專注模式");
                break;

            case WorkCyclePhase.Disabled:
                _trayIcon.SetDisableText(true);
                _trayIcon.SetPauseEnabled(false);
                _trayIcon.SetFocusModeEnabled(false);
                _trayIcon.SetBreakNowEnabled(false);
                _trayIcon.SetStatusText("RestCue – 已停用");
                break;

            case WorkCyclePhase.BreakInProgress:
                _trayIcon.SetPauseEnabled(false);
                _trayIcon.SetFocusModeEnabled(false);
                _trayIcon.SetBreakNowEnabled(false);
                break;

            case WorkCyclePhase.Idle:
                _trayIcon.SetFocusModeEnabled(false);
                _trayIcon.SetBreakNowEnabled(false);
                break;
        }
    }
}
