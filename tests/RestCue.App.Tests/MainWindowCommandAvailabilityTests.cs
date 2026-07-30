using System.Windows;
using System.Windows.Controls;
using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// The main window offers the same commands as the tray, because both read the same
/// policy. Asserted against the policy rather than a hand-written table, so a third
/// surface cannot drift the way these two did.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class MainWindowCommandAvailabilityTests
{
    private readonly WpfApplicationFixture wpf;

    public MainWindowCommandAvailabilityTests(WpfApplicationFixture wpf)
    {
        this.wpf = wpf;
    }

    [Theory]
    [MemberData(nameof(AllPhases))]
    public void Menu_and_buttons_match_the_policy(WorkCyclePhase phase)
    {
        CommandAvailability expected = CommandAvailabilityPolicy.ForPhase(phase);

        wpf.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                window.ApplyCommandAvailability(expected);

                Assert.Equal(expected.PauseToggleEnabled, Button(window, "PauseResumeButton").IsEnabled);
                Assert.Equal(expected.FocusToggleEnabled, Button(window, "FocusButton").IsEnabled);
                Assert.Equal(expected.DisableToggleEnabled, Button(window, "DisableButton").IsEnabled);
                Assert.Equal(expected.CanBreakNow, Button(window, "BreakNowButton").IsEnabled);

                Assert.Equal(expected.FocusToggleEnabled, MenuItem(window, "FocusMenuItem").IsEnabled);
                Assert.Equal(expected.DisableToggleEnabled, MenuItem(window, "DisableMenuItem").IsEnabled);
                Assert.Equal(expected.CanBreakNow, MenuItem(window, "BreakNowMenuItem").IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [MemberData(nameof(AllPhases))]
    public void Labels_match_the_policy(WorkCyclePhase phase)
    {
        CommandAvailability expected = CommandAvailabilityPolicy.ForPhase(phase);

        wpf.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                window.ApplyCommandAvailability(expected);

                Assert.Equal(
                    expected.ShowResume ? "繼續" : "暫停",
                    Button(window, "PauseResumeButton").Content);
                Assert.Equal(
                    expected.ShowEndFocusMode ? "結束專注模式" : "專注模式",
                    Button(window, "FocusButton").Content);
                Assert.Equal(
                    expected.ShowEnable ? "啟用提醒" : "停用提醒",
                    Button(window, "DisableButton").Content);
                Assert.Equal(
                    expected.ShowEndFocusMode ? "結束專注模式" : "專注模式",
                    MenuItem(window, "FocusMenuItem").Header);
                Assert.Equal(
                    expected.ShowEnable ? "啟用提醒" : "停用提醒",
                    MenuItem(window, "DisableMenuItem").Header);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [MemberData(nameof(AllPhases))]
    public void Resume_menu_item_is_visible_exactly_when_resume_is_the_direction(WorkCyclePhase phase)
    {
        CommandAvailability expected = CommandAvailabilityPolicy.ForPhase(phase);

        wpf.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                window.ApplyCommandAvailability(expected);

                Assert.Equal(
                    expected.ShowResume ? Visibility.Visible : Visibility.Collapsed,
                    MenuItem(window, "ResumeMenuItem").Visibility);

                // The timed-pause submenu is an elaboration of pause, so it appears
                // exactly when pause is legal.
                Assert.Equal(
                    expected.CanPause ? Visibility.Visible : Visibility.Collapsed,
                    MenuItem(window, "PauseSubmenu").Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [MemberData(nameof(AllPhases))]
    public void Disable_is_reachable_in_every_phase(WorkCyclePhase phase)
    {
        CommandAvailability expected = CommandAvailabilityPolicy.ForPhase(phase);

        wpf.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                window.ApplyCommandAvailability(expected);

                // The main window used to forbid disabling in four phases where the engine
                // accepts it and the tray offered it.
                Assert.True(
                    Button(window, "DisableButton").IsEnabled,
                    $"Disable button is dead in {phase}.");
                Assert.True(
                    MenuItem(window, "DisableMenuItem").IsEnabled,
                    $"Disable menu item is dead in {phase}.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    public static TheoryData<WorkCyclePhase> AllPhases()
    {
        var data = new TheoryData<WorkCyclePhase>();
        foreach (WorkCyclePhase phase in Enum.GetValues<WorkCyclePhase>())
        {
            data.Add(phase);
        }
        return data;
    }

    private static Button Button(MainWindow window, string name) =>
        (Button)(window.FindName(name) ?? throw new InvalidOperationException($"No button named {name}."));

    private static MenuItem MenuItem(MainWindow window, string name) =>
        (MenuItem)(window.FindName(name) ?? throw new InvalidOperationException($"No menu item named {name}."));
}
