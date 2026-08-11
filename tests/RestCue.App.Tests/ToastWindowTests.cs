using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using RestCue.App.Lifecycle;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using Xunit;

namespace RestCue.App.Tests;

[Collection(WpfCollection.Name)]
public sealed class ToastWindowTests
{
    private readonly WpfApplicationFixture wpf;

    public ToastWindowTests(WpfApplicationFixture wpf)
    {
        this.wpf = wpf;
    }

    [Fact]
    public void Toast_is_transparent_non_activating_and_clickable()
    {
        wpf.Run(() =>
        {
            var window = new ToastWindow();
            try
            {
                Assert.True(window.AllowsTransparency);
                Assert.True(window.Opacity < 1.0);
                Assert.False(window.ShowActivated);
                Assert.False(window.ShowInTaskbar);
                Assert.True(window.IsHitTestVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ShowToast_sets_content_and_dispose_closes_the_window()
    {
        wpf.Run(() =>
        {
            var window = new ToastWindow();
            window.ShowToast(
                "標題",
                "內容",
                NotificationDuration.UntilDismissed,
                new TrayViewState(WorkCyclePhase.PendingReminder, RestDebtLevel.Level4, true));
            try
            {
                Assert.True(window.IsVisible);
                Assert.Equal("標題", ((TextBlock)window.FindName("TitleText")!).Text);
                Assert.Equal("內容", ((TextBlock)window.FindName("MessageText")!).Text);
                Assert.IsAssignableFrom<BitmapSource>(
                    ((System.Windows.Controls.Image)window.FindName("IconImage")!).Source);
            }
            finally
            {
                window.Dispose();
            }

            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void Toast_offers_an_enabled_break_now_button_without_activating()
    {
        wpf.Run(() =>
        {
            var window = new ToastWindow();
            int requests = 0;
            window.BreakNowRequested += (_, _) => requests++;
            window.ShowToast(
                "標題",
                "內容",
                NotificationDuration.UntilDismissed,
                new TrayViewState(WorkCyclePhase.PendingReminder, RestDebtLevel.Level4, true));
            try
            {
                var button = (Button)(window.FindName("BreakNowButton")
                    ?? throw new InvalidOperationException("Toast is missing BreakNowButton."));

                Assert.Equal("立即休息", button.Content);
                Assert.True(button.IsEnabled);
                button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(1, requests);
                Assert.False(window.IsVisible);
                Assert.False(window.ShowActivated);
            }
            finally
            {
                window.Dispose();
            }
        });
    }
}
