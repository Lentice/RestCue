using RestCue.App.Lifecycle;
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

        public void Dispose() => IsDisposed = true;

        public void SetPauseEnabled(bool enabled) => PauseEnabled = enabled;

        public void SetFocusModeEnabled(bool enabled) => FocusModeEnabled = enabled;

        public void SetDisableEnabled(bool enabled) => DisableEnabled = enabled;

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
    }

    private sealed class FakeStatusWindow : IStatusWindow
    {
        public int ShowOrActivateCount { get; private set; }

        public void ShowOrActivate() => ShowOrActivateCount++;
    }
}