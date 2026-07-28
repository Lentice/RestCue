using System.Drawing;
using System.Windows.Forms;
using RestCue.Core.Domain;

namespace RestCue.App.Lifecycle;

public sealed class WindowsTrayIcon : ITrayIcon
{
    private readonly NotifyIcon _notifyIcon;

    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _focusItem;
    private readonly ToolStripMenuItem _disableItem;
    private readonly ToolStripMenuItem _breakNowItem;

    private bool _isPaused;
    private bool _isFocusMode;
    private bool _isDisabled;
    private RestDebtLevel _currentDebtLevel;
    private bool _isSuppressed;

    private static readonly Icon NormalIcon = SystemIcons.Information;
    private static readonly Icon SuppressedIcon = SystemIcons.Exclamation;
    private static readonly Icon Level1Icon = SystemIcons.Shield;
    private static readonly Icon Level2Icon = SystemIcons.Warning;
    private static readonly Icon Level4Icon = SystemIcons.Error;

    public WindowsTrayIcon()
    {
        _pauseItem = new ToolStripMenuItem("暫停提醒", null, TogglePause);
        _focusItem = new ToolStripMenuItem("專注模式", null, ToggleFocusMode);
        _disableItem = new ToolStripMenuItem("停用提醒", null, ToggleDisable);
        _breakNowItem = new ToolStripMenuItem("立即休息", null, (_, _) => BreakNowRequested?.Invoke(this, EventArgs.Empty));

        var menu = new ContextMenuStrip();
        menu.Items.Add("開啟 RestCue", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_focusItem);
        menu.Items.Add(_disableItem);
        menu.Items.Add(_breakNowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束 RestCue", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = NormalIcon,
            Text = "RestCue – Eye Break Reminder"
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? PauseRequested;

    public event EventHandler? ResumeRequested;

    public event EventHandler? FocusModeRequested;

    public event EventHandler? EndFocusModeRequested;

    public event EventHandler? DisableRequested;

    public event EventHandler? EnableRequested;

    public event EventHandler? BreakNowRequested;

    public bool Visible
    {
        get => _notifyIcon.Visible;
        set => _notifyIcon.Visible = value;
    }

    public void SetPauseText(bool isPaused)
    {
        _isPaused = isPaused;
        _pauseItem.Text = isPaused ? "繼續提醒" : "暫停提醒";
    }

    public void SetFocusModeText(bool isFocusMode)
    {
        _isFocusMode = isFocusMode;
        _focusItem.Text = isFocusMode ? "結束專注模式" : "專注模式";
    }

    public void SetDisableText(bool isDisabled)
    {
        _isDisabled = isDisabled;
        _disableItem.Text = isDisabled ? "啟用提醒" : "停用提醒";
    }

    public void SetStatusText(string text)
    {
        _notifyIcon.Text = text;
    }

    public void SetPauseEnabled(bool enabled) => _pauseItem.Enabled = enabled;

    public void SetFocusModeEnabled(bool enabled) => _focusItem.Enabled = enabled;

    public void SetDisableEnabled(bool enabled) => _disableItem.Enabled = enabled;

    public void SetBreakNowEnabled(bool enabled) => _breakNowItem.Enabled = enabled;

    public void SetSuppressedState(bool isSuppressed)
    {
        _isSuppressed = isSuppressed;
        _notifyIcon.Icon = GetIconForCurrentState();
    }

    public void SetDebtLevel(RestDebtLevel level)
    {
        _currentDebtLevel = level;
        if (!_isSuppressed)
        {
            _notifyIcon.Icon = GetIconForDebtLevel(level);
        }
    }

    private Icon GetIconForDebtLevel(RestDebtLevel level)
    {
        return level switch
        {
            RestDebtLevel.Level1 => Level1Icon,
            RestDebtLevel.Level2 => Level2Icon,
            RestDebtLevel.Level3 => SuppressedIcon,
            RestDebtLevel.Level4 => Level4Icon,
            _ => NormalIcon
        };
    }

    private Icon GetIconForCurrentState()
    {
        if (_isSuppressed)
            return SuppressedIcon;
        return GetIconForDebtLevel(_currentDebtLevel);
    }

    private void TogglePause(object? sender, EventArgs e)
    {
        if (_isPaused)
        {
            ResumeRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            PauseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ToggleFocusMode(object? sender, EventArgs e)
    {
        if (_isFocusMode)
        {
            EndFocusModeRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            FocusModeRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ToggleDisable(object? sender, EventArgs e)
    {
        if (_isDisabled)
        {
            EnableRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            DisableRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => _notifyIcon.Dispose();
}