using System.Drawing;
using System.Windows.Forms;

namespace RestCue.App.Lifecycle;

public sealed class WindowsTrayIcon : ITrayIcon
{
    private readonly NotifyIcon _notifyIcon;

    public WindowsTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("開啟 RestCue", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束 RestCue", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = SystemIcons.Information,
            Text = "RestCue – Eye Break Reminder"
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public bool Visible
    {
        get => _notifyIcon.Visible;
        set => _notifyIcon.Visible = value;
    }

    public void Dispose() => _notifyIcon.Dispose();
}
