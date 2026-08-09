using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RestCue.Infrastructure.Activity;

namespace RestCue.App;

/// <summary>
/// A passive surface for an application-rule suggestion. Non-modal and non-activating:
/// it never takes focus or blocks the foreground app's input, mirroring the reminder
/// surface's <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c> extended style.
/// </summary>
public partial class SuggestionWindow : Window
{
    private readonly IFullscreenWin32Api win32 = new FullscreenWin32Api();

    public event EventHandler? Approved;
    public event EventHandler? Dismissed;

    public SuggestionWindow()
    {
        InitializeComponent();
    }

    public void ShowSuggestion(string processName)
    {
        MessageText.Text =
            $"已檢測到「{processName}」正在執行。\n\n" +
            "RestCue 建議為此應用程式套用「僅系統列」規則，避免休息提醒干擾。\n\n" +
            "要套用此建議嗎？";
        PositionOnWorkAreaRightEdge();
        Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = win32.GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void PositionOnWorkAreaRightEdge()
    {
        var workArea = SystemParameters.WorkArea;
        var position = ReminderWindowPlacement.RightEdge(
            (int)workArea.Left,
            (int)workArea.Top,
            (int)workArea.Right,
            (int)workArea.Bottom,
            (int)Width,
            (int)Height,
            4);
        Left = position.X;
        Top = position.Y;
    }

    private void OnApproveButtonClick(object sender, RoutedEventArgs e)
    {
        Approved?.Invoke(this, EventArgs.Empty);
    }

    private void OnDismissButtonClick(object sender, RoutedEventArgs e)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = false)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
