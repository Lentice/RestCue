using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// The reminder surface must never be a dead surface. It is wired once at construction with
/// its full action set, so it cannot matter whether it opened for a reminder or for a
/// manually started break.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class ReminderSurfaceWiringTests
{
    private readonly WpfApplicationFixture wpf;

    public ReminderSurfaceWiringTests(WpfApplicationFixture wpf)
    {
        this.wpf = wpf;
    }

    [Fact]
    public void A_surface_opened_for_a_manual_break_has_a_working_cancel_action()
    {
        wpf.Run(() =>
        {
            var window = new ReminderWindow();
            try
            {
                int cancelled = 0;
                window.CancelRequested += (_, _) => cancelled++;

                window.StartBreakGuide();
                Click(window, "CancelButton");

                Assert.Equal(1, cancelled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void After_a_cancelled_break_a_reminder_exposes_every_action()
    {
        wpf.Run(() =>
        {
            var window = new ReminderWindow();
            try
            {
                int breaks = 0, snoozes = 0, ignores = 0;
                window.BreakRequested += (_, _) => breaks++;
                window.SnoozeRequested += (_, _) => snoozes++;
                window.IgnoreRequested += (_, _) => ignores++;

                // Manual break, cancelled.
                window.StartBreakGuide();
                window.StopBreakGuide();

                // A later reminder on the same surface.
                window.ShowReminder();
                Click(window, "ActionButton");
                Click(window, "SnoozeButton");
                Click(window, "IgnoreButton");

                Assert.Equal(1, breaks);
                Assert.Equal(1, snoozes);
                Assert.Equal(1, ignores);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Double_activation_of_the_primary_action_requests_one_break()
    {
        wpf.Run(() =>
        {
            var window = new ReminderWindow();
            try
            {
                int breaks = 0;
                window.BreakRequested += (_, _) => breaks++;

                window.ShowReminder();
                Click(window, "ActionButton");
                Click(window, "ActionButton");

                Assert.Equal(1, breaks);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_cancelled_break_does_not_permanently_disable_the_primary_action()
    {
        wpf.Run(() =>
        {
            var window = new ReminderWindow();
            try
            {
                int breaks = 0;
                window.BreakRequested += (_, _) => breaks++;

                window.ShowReminder();
                Click(window, "ActionButton");
                Assert.Equal(1, breaks);

                // The break is cancelled rather than completed. The guard used to clear
                // only on completion, leaving the primary action dead for good.
                window.StopBreakGuide();
                window.ShowReminder();
                Click(window, "ActionButton");

                Assert.Equal(2, breaks);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_completed_break_does_not_disable_the_primary_action()
    {
        wpf.Run(() =>
        {
            var window = new ReminderWindow();
            try
            {
                int breaks = 0;
                window.BreakRequested += (_, _) => breaks++;

                window.ShowReminder();
                Click(window, "ActionButton");
                window.CompleteBreak();

                window.ShowReminder();
                Click(window, "ActionButton");

                Assert.Equal(2, breaks);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Showing_a_reminder_offers_the_reminder_actions_and_hides_cancel()
    {
        wpf.Run(() =>
        {
            var window = new ReminderWindow();
            try
            {
                window.ShowReminder();

                Assert.Equal(Visibility.Visible, Button(window, "ActionButton").Visibility);
                Assert.Equal(Visibility.Visible, Button(window, "SnoozeButton").Visibility);
                Assert.Equal(Visibility.Visible, Button(window, "IgnoreButton").Visibility);
                Assert.Equal(Visibility.Collapsed, Button(window, "CancelButton").Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Running_a_break_guide_offers_cancel_and_hides_the_reminder_actions()
    {
        wpf.Run(() =>
        {
            var window = new ReminderWindow();
            try
            {
                window.StartBreakGuide();

                Assert.Equal(Visibility.Visible, Button(window, "CancelButton").Visibility);
                Assert.Equal(Visibility.Collapsed, Button(window, "ActionButton").Visibility);
                Assert.Equal(Visibility.Collapsed, Button(window, "SnoozeButton").Visibility);
                Assert.Equal(Visibility.Collapsed, Button(window, "IgnoreButton").Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Click(ReminderWindow window, string name) =>
        Button(window, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static Button Button(ReminderWindow window, string name) =>
        (Button)(window.FindName(name) ?? throw new InvalidOperationException($"No button named {name}."));
}
