using System.ComponentModel;
using System.Windows;
using RestCue.App.Lifecycle;

namespace RestCue.App;

public partial class MainWindow : System.Windows.Window, IStatusWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public void ShowOrActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
