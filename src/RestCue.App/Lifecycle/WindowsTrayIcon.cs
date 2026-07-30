using System.Drawing;
using System.Windows.Forms;
using RestCue.Core.Domain;

namespace RestCue.App.Lifecycle;

public sealed class WindowsTrayIcon : ITrayIcon
{
    private readonly NotifyIcon _notifyIcon;

    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _pause15;
    private readonly ToolStripMenuItem _pause30;
    private readonly ToolStripMenuItem _pause60;
    private readonly ToolStripMenuItem _pauseManual;
    private readonly ToolStripMenuItem _resumeItem;
    private readonly ToolStripMenuItem _focusItem;
    private readonly ToolStripMenuItem _disableItem;
    private readonly ToolStripMenuItem _breakNowItem;

    private bool _isPaused;
    private bool _isFocusMode;
    private bool _isDisabled;
    private RestDebtLevel _currentDebtLevel;
    private bool _isSuppressed;
    private ContextMenuStrip _menu;
    private int _pauseMenuIndex;
    private static readonly Icon NormalIcon = TrayIconFactory.Create(Color.FromArgb(47, 111, 235));
    private static readonly Icon Level1Icon = TrayIconFactory.Create(Color.FromArgb(46, 125, 91));
    private static readonly Icon Level2Icon = TrayIconFactory.Create(Color.FromArgb(196, 128, 20));
    private static readonly Icon Level3Icon = TrayIconFactory.Create(Color.FromArgb(211, 95, 24));
    private static readonly Icon Level4Icon = TrayIconFactory.Create(Color.FromArgb(192, 57, 43));
    private static readonly Icon SuppressedIcon = TrayIconFactory.Create(Color.FromArgb(92, 101, 112));

    public WindowsTrayIcon()
    {
        _pause15 = CreatePauseItem(PausePresets.FifteenMinutes);
        _pause30 = CreatePauseItem(PausePresets.ThirtyMinutes);
        _pause60 = CreatePauseItem(PausePresets.OneHour);
        _pauseManual = new ToolStripMenuItem("直到手動恢復", null, (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty));
        _pauseItem = new ToolStripMenuItem("暫停提醒");
        _pauseItem.DropDownItems.AddRange(
            _pause15, _pause30, _pause60,
            new ToolStripSeparator(),
            _pauseManual);
        _resumeItem = new ToolStripMenuItem("繼續提醒", null, TogglePause);
        _focusItem = new ToolStripMenuItem("專注模式", null, ToggleFocusMode);
        _disableItem = new ToolStripMenuItem("停用提醒", null, ToggleDisable);
        _breakNowItem = new ToolStripMenuItem("立即休息", null, (_, _) => BreakNowRequested?.Invoke(this, EventArgs.Empty));

        var menu = new ContextMenuStrip();
        menu.Items.Add("開啟 RestCue", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("今日統計", null, (_, _) => StatisticsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("資料透明檢視", null, (_, _) => DataTransparencyRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("匯出／清除資料", null, (_, _) => DataManagementRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("設定", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("關於與隱私", null, (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        _pauseMenuIndex = menu.Items.Count - 1;
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
        _menu = menu;
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private ToolStripMenuItem CreatePauseItem(PausePreset preset) =>
        new(preset.Label, null, (_, _) => PauseForRequested?.Invoke(this, preset.Duration));

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? PauseRequested;

    public event EventHandler<TimeSpan>? PauseForRequested;

    public event EventHandler? ResumeRequested;

    public event EventHandler? FocusModeRequested;

    public event EventHandler? EndFocusModeRequested;

    public event EventHandler? DisableRequested;

    public event EventHandler? EnableRequested;

    public event EventHandler? BreakNowRequested;

    public event EventHandler? StatisticsRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? AboutRequested;

    public event EventHandler? DataTransparencyRequested;

    public event EventHandler? DataManagementRequested;

    public bool Visible
    {
        get => _notifyIcon.Visible;
        set => _notifyIcon.Visible = value;
    }

    public void SetPauseText(bool isPaused)
    {
        _isPaused = isPaused;
        if (isPaused)
        {
            _menu.Items.RemoveAt(_pauseMenuIndex);
            _menu.Items.Insert(_pauseMenuIndex, _resumeItem);
        }
        else
        {
            _menu.Items.RemoveAt(_pauseMenuIndex);
            _menu.Items.Insert(_pauseMenuIndex, _pauseItem);
        }
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
            RestDebtLevel.Level3 => Level3Icon,
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
