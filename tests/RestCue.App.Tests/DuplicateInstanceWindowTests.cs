using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace RestCue.App.Tests;

[Collection(WpfCollection.Name)]
public sealed class DuplicateInstanceWindowTests
{
    private readonly WpfApplicationFixture wpf;

    public DuplicateInstanceWindowTests(WpfApplicationFixture wpf)
    {
        this.wpf = wpf;
    }

    [Fact]
    public void ConfirmButton_RaisesConfirmed()
    {
        wpf.Run(() =>
        {
            var window = new DuplicateInstanceWindow();
            bool confirmed = false;
            window.Confirmed += (_, _) => confirmed = true;
            window.Show();

            var button = (Button)window.FindName("ConfirmButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(confirmed);
        });
    }

    [Fact]
    public void ClosingWithoutConfirm_StillRaisesConfirmed()
    {
        wpf.Run(() =>
        {
            var window = new DuplicateInstanceWindow();
            bool confirmed = false;
            window.Confirmed += (_, _) => confirmed = true;
            window.Show();

            window.Close();

            Assert.True(confirmed);
        });
    }
}
