using System.Windows;
using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// Reminder opacity shipped as a control that did nothing. It must reach the reminder
/// surface, and a low value must not cost the user the ability to click the reminder.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class ReminderOpacityTests
{
    private readonly WpfApplicationFixture wpf;

    public ReminderOpacityTests(WpfApplicationFixture wpf)
    {
        this.wpf = wpf;
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.55)]
    [InlineData(1.0)]
    public void Saved_value_reaches_the_reminder_surface(double opacity)
    {
        wpf.Run(() =>
        {
            var window = new ReminderWindow();
            try
            {
                window.ApplySurfaceOpacity(opacity);
                Assert.Equal(opacity, window.Opacity, 3);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Value_below_the_validated_floor_is_clamped()
    {
        wpf.Run(() =>
        {
            var window = new ReminderWindow();
            try
            {
                window.ApplySurfaceOpacity(0.0);
                Assert.Equal(ReminderWindow.MinimumSurfaceOpacity, window.Opacity, 3);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Value_above_full_opacity_is_clamped()
    {
        wpf.Run(() =>
        {
            var window = new ReminderWindow();
            try
            {
                window.ApplySurfaceOpacity(1.4);
                Assert.Equal(1.0, window.Opacity, 3);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Reminder_stays_hit_testable_at_the_minimum_value()
    {
        wpf.Run(() =>
        {
            var window = new ReminderWindow();
            try
            {
                window.ApplySurfaceOpacity(ReminderWindow.MinimumSurfaceOpacity);

                // WPF opacity is a render-time concern; hit-testing is unaffected. Assert
                // it rather than trusting it, since a translucent-but-dead reminder would
                // be worse than no opacity control at all.
                Assert.True(window.IsHitTestVisible);
                foreach (UIElement element in HitTestableChildren(window))
                {
                    Assert.True(element.IsHitTestVisible);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static IEnumerable<UIElement> HitTestableChildren(ReminderWindow window)
    {
        if (window.Content is UIElement content)
            yield return content;
    }
}
