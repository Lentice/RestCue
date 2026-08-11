using System.Windows.Controls;
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
    public void Toast_is_transparent_non_activating_and_click_through()
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
                Assert.False(window.IsHitTestVisible);
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
            window.ShowToast("標題", "內容", NotificationDuration.UntilDismissed);
            try
            {
                Assert.True(window.IsVisible);
                Assert.Equal("標題", ((TextBlock)window.FindName("TitleText")!).Text);
                Assert.Equal("內容", ((TextBlock)window.FindName("MessageText")!).Text);
            }
            finally
            {
                window.Dispose();
            }

            Assert.False(window.IsVisible);
        });
    }
}
