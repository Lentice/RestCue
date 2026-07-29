using RestCue.App.Lifecycle;
using RestCue.Core.Domain;
using Xunit;

namespace RestCue.App.Tests;

public sealed class DataManagementWiringTests
{
    [Fact]
    public void Tray_menu_item_raises_request_once_per_click()
    {
        var tray = new FakeTrayIcon();
        var eventCount = 0;
        tray.DataManagementRequested += (_, _) => eventCount++;

        tray.RequestDataManagement();
        tray.RequestDataManagement();

        Assert.Equal(2, eventCount);
    }

    private sealed class FakeTrayIcon : ITrayIcon
    {
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
        public event EventHandler? DataManagementRequested;
#pragma warning restore CS0067

        public bool Visible { get; set; }

        public void RequestDataManagement() =>
            DataManagementRequested?.Invoke(this, EventArgs.Empty);

        public void Dispose() { }
        public void SetPauseText(bool isPaused) { }
        public void SetFocusModeText(bool isFocusMode) { }
        public void SetDisableText(bool isDisabled) { }
        public void SetStatusText(string text) { }
        public void SetPauseEnabled(bool enabled) { }
        public void SetFocusModeEnabled(bool enabled) { }
        public void SetDisableEnabled(bool enabled) { }
        public void SetBreakNowEnabled(bool enabled) { }
        public void SetSuppressedState(bool isSuppressed) { }
        public void SetDebtLevel(RestDebtLevel level) { }
    }
}
