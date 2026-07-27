using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RestCue.App;

public partial class ReminderWindow : Window
{
    private readonly DispatcherTimer countdownTimer;
    private int countdownSeconds;
    private bool isBreakStarting;

    public event EventHandler? BreakRequested;
    public event EventHandler? BreakCompleted;
    public event EventHandler? SnoozeRequested;
    public event EventHandler? IgnoreRequested;

    public TimeSpan SnoozeDuration { get; set; } = TimeSpan.FromMinutes(5);

    public ReminderWindow()
    {
        InitializeComponent();
        countdownTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        countdownTimer.Tick += OnCountdownTick;

        Loaded += OnLoaded;
    }

    public void ShowReminder()
    {
        PhaseText.Text = "Look at something\n6 meters away.";
        CountdownText.Text = "";
        ActionButton.Content = "Start 20s Break";
        ActionButton.IsEnabled = true;
        ActionButton.Visibility = Visibility.Visible;
        SnoozeButton.Content = $"Snooze {(int)SnoozeDuration.TotalMinutes}min";
        SnoozeButton.Visibility = Visibility.Visible;
        IgnoreButton.Visibility = Visibility.Visible;
        PositionOnPrimaryScreenRightEdge();
        Show();
    }

    public void StartBreakCountdown(int durationSeconds)
    {
        countdownSeconds = durationSeconds;
        CountdownText.Text = $"{countdownSeconds}s";
        PhaseText.Text = "Break in progress...";
        ActionButton.IsEnabled = false;
        ActionButton.Content = "Break in progress...";
        SnoozeButton.Visibility = Visibility.Collapsed;
        IgnoreButton.Visibility = Visibility.Collapsed;
        countdownTimer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionOnPrimaryScreenRightEdge();
    }

    private void PositionOnPrimaryScreenRightEdge()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 4;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        if (isBreakStarting) return;

        isBreakStarting = true;
        BreakRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSnoozeButtonClick(object sender, RoutedEventArgs e)
    {
        SnoozeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnIgnoreButtonClick(object sender, RoutedEventArgs e)
    {
        IgnoreRequested?.Invoke(this, EventArgs.Empty);
    }

    public void CompleteBreak()
    {
        countdownTimer.Stop();
        OnBreakCompleted();
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        countdownSeconds--;

        if (countdownSeconds <= 0)
        {
            countdownTimer.Stop();
            CountdownText.Text = "Done!";
        }
        else
        {
            CountdownText.Text = $"{countdownSeconds}s";
        }
    }

    private void OnBreakCompleted()
    {
        isBreakStarting = false;
        Hide();
        BreakCompleted?.Invoke(this, EventArgs.Empty);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = false)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
