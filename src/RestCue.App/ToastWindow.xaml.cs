using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RestCue.App.Lifecycle;
using RestCue.Core.Domain;
using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;

namespace RestCue.App;

public partial class ToastWindow : Window, IDisposable
{
    private readonly DispatcherTimer dismissalTimer;
    private bool isDisposed;

    public event EventHandler? BreakNowRequested;

    public ToastWindow()
    {
        InitializeComponent();

        dismissalTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(7)
        };
        dismissalTimer.Tick += OnDismissalTimerTick;
    }

    public void ShowToast(
        string title,
        string text,
        NotificationDuration duration,
        TrayViewState state = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        TitleText.Text = title;
        MessageText.Text = text;
        IconImage.Source = CreateIconSource(WindowsTrayIcon.GetIconForState(state));
        BreakNowButton.IsEnabled = CommandAvailabilityPolicy.ForPhase(state.Mode).CanBreakNow;

        if (IsVisible)
        {
            UpdateLayout();
            PositionOnPrimaryMonitorBottomRight();
        }
        else
        {
            Show();
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(PositionOnPrimaryMonitorBottomRight));

        dismissalTimer.Stop();
        if (duration != NotificationDuration.UntilDismissed)
        {
            dismissalTimer.Interval = duration == NotificationDuration.Long
                ? TimeSpan.FromSeconds(25)
                : TimeSpan.FromSeconds(7);
            dismissalTimer.Start();
        }
    }

    public void Dispose()
    {
        if (isDisposed) return;

        isDisposed = true;
        dismissalTimer.Stop();
        if (IsVisible)
        {
            Close();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(
            hwnd,
            GWL_EXSTYLE,
            exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    protected override void OnClosed(EventArgs e)
    {
        dismissalTimer.Stop();
        base.OnClosed(e);
    }

    private void OnDismissalTimerTick(object? sender, EventArgs e)
    {
        dismissalTimer.Stop();
        Hide();
    }

    private void PositionOnPrimaryMonitorBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        var position = ReminderWindowPlacement.BottomRight(
            (int)workArea.Left,
            (int)workArea.Top,
            (int)workArea.Right,
            (int)workArea.Bottom,
            (int)Math.Ceiling(ActualWidth),
            (int)Math.Ceiling(ActualHeight),
            12);

        Left = position.X;
        Top = position.Y;
    }

    private static BitmapSource CreateIconSource(System.Drawing.Icon icon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private void OnBreakNowButtonClick(object sender, RoutedEventArgs e)
    {
        dismissalTimer.Stop();
        Hide();
        BreakNowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        dismissalTimer.Stop();
        Hide();
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = false)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
