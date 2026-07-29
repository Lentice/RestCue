using RestCue.App.Lifecycle;
using RestCue.Core.Domain;
using RestCue.Core.Events;
using Xunit;

namespace RestCue.App.Tests;

public sealed class ApplicationLifecycleTests
{
    [Fact]
    public void Start_MakesSingleTrayIconVisible_WhenCalledMoreThanOnce()
    {
        var tray = new FakeTrayIcon();
        using var lifecycle = new ApplicationLifecycle(tray, new FakeStatusWindow(), () => { });

        lifecycle.Start();
        lifecycle.Start();

        Assert.True(tray.Visible);
        Assert.Equal(1, tray.VisibleSetToTrueCount);
    }

    [Fact]
    public void OpenRequest_OpensExistingStatusWindow()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeStatusWindow();
        using var lifecycle = new ApplicationLifecycle(tray, window, () => { });
        lifecycle.Start();

        tray.RequestOpen();
        tray.RequestOpen();

        Assert.Equal(2, window.ShowOrActivateCount);
        Assert.Equal(1, tray.VisibleSetToTrueCount);
    }

    [Fact]
    public void ExitRequest_DisposesTrayIconBeforeShuttingDown()
    {
        var tray = new FakeTrayIcon();
        var shutdownCalled = false;
        var lifecycle = new ApplicationLifecycle(
            tray,
            new FakeStatusWindow(),
            () =>
            {
                Assert.True(tray.IsDisposed);
                shutdownCalled = true;
            });
        lifecycle.Start();

        tray.RequestExit();

        Assert.True(shutdownCalled);
        Assert.False(tray.Visible);
    }

    [Fact]
    public void ExposesTrayIcon()
    {
        var tray = new FakeTrayIcon();
        using var lifecycle = new ApplicationLifecycle(tray, new FakeStatusWindow(), () => { });
        lifecycle.Start();

        Assert.Same(tray, lifecycle.TrayIcon);
    }

    private sealed class FakeTrayIcon : ITrayIcon
    {
        private bool _visible;

#pragma warning disable CS0067
        public event EventHandler? OpenRequested;

        public event EventHandler? ExitRequested;

        public event EventHandler? PauseRequested;

        public event EventHandler? ResumeRequested;

        public event EventHandler? FocusModeRequested;

        public event EventHandler? EndFocusModeRequested;

        public event EventHandler? DisableRequested;

        public event EventHandler? EnableRequested;

        public event EventHandler? BreakNowRequested;

        public event EventHandler? StatisticsRequested;

        public event EventHandler? SettingsRequested;

        public event EventHandler? AboutRequested;

        public event EventHandler? DataTransparencyRequested;
#pragma warning restore CS0067

        public int VisibleSetToTrueCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool PauseEnabled { get; private set; } = true;

        public bool FocusModeEnabled { get; private set; } = true;

        public bool DisableEnabled { get; private set; } = true;

        public bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (value)
                {
                    VisibleSetToTrueCount++;
                }
            }
        }

        public void RequestOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);

        public void RequestExit() => ExitRequested?.Invoke(this, EventArgs.Empty);

        public void RequestBreakNow() => BreakNowRequested?.Invoke(this, EventArgs.Empty);

        public void RequestPause() => PauseRequested?.Invoke(this, EventArgs.Empty);

        public void RequestResume() => ResumeRequested?.Invoke(this, EventArgs.Empty);

        public void RequestFocusMode() => FocusModeRequested?.Invoke(this, EventArgs.Empty);

        public void RequestEndFocusMode() => EndFocusModeRequested?.Invoke(this, EventArgs.Empty);

        public void RequestDisable() => DisableRequested?.Invoke(this, EventArgs.Empty);

        public void RequestEnable() => EnableRequested?.Invoke(this, EventArgs.Empty);

        public void Dispose() => IsDisposed = true;

        public void SetPauseEnabled(bool enabled) => PauseEnabled = enabled;

        public void SetFocusModeEnabled(bool enabled) => FocusModeEnabled = enabled;

        public void SetDisableEnabled(bool enabled) => DisableEnabled = enabled;

        public void SetBreakNowEnabled(bool enabled)
        {
        }

        public void SetPauseText(bool isPaused)
        {
        }

        public void SetFocusModeText(bool isFocusMode)
        {
        }

        public void SetDisableText(bool isDisabled)
        {
        }

        public void SetStatusText(string text)
        {
        }

        public void SetSuppressedState(bool isSuppressed)
        {
        }

        public void SetDebtLevel(RestDebtLevel level) { }
    }

    [Fact]
    public void WireBreakNowCommand_binds_event_to_StartBreakNow()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeStatusWindow();
        App.WireBreakNowCommand(tray, window);
        tray.RequestBreakNow();
        Assert.Equal(1, window.StartBreakNowCount);
    }

    [Fact]
    public void WireModeCommands_binds_all_six_events()
    {
        var tray = new FakeTrayIcon();
        var window = new FakeStatusWindow();
        App.WireModeCommands(tray, window);

        tray.RequestPause();
        tray.RequestResume();
        tray.RequestFocusMode();
        tray.RequestEndFocusMode();
        tray.RequestDisable();
        tray.RequestEnable();

        Assert.Equal(1, window.PauseCount);
        Assert.Equal(1, window.ResumeCount);
        Assert.Equal(1, window.StartFocusModeCount);
        Assert.Equal(1, window.EndFocusModeCount);
        Assert.Equal(1, window.DisableCount);
        Assert.Equal(1, window.EnableCount);
    }

    private sealed class FakeStatusWindow : IStatusWindow
    {
#pragma warning disable CS0067
        public event EventHandler<RestDebtLevelChangedEventArgs>? DebtLevelChanged;
#pragma warning restore CS0067

        public RestDebtLevel CurrentDebtLevel => RestDebtLevel.Level0;

        public int ShowOrActivateCount { get; private set; }

        public int StartBreakNowCount { get; private set; }

        public int PauseCount { get; private set; }

        public int ResumeCount { get; private set; }

        public int StartFocusModeCount { get; private set; }

        public int EndFocusModeCount { get; private set; }

        public int DisableCount { get; private set; }

        public int EnableCount { get; private set; }

        public void ShowOrActivate() => ShowOrActivateCount++;

        public void StartBreakNow() => StartBreakNowCount++;

        public void Pause() => PauseCount++;

        public void Resume() => ResumeCount++;

        public void StartFocusMode() => StartFocusModeCount++;

        public void EndFocusMode() => EndFocusModeCount++;

        public void Disable() => DisableCount++;

        public void Enable() => EnableCount++;
    }
}