using System.Drawing;
using System.Windows.Forms;
using System.Windows.Threading;
using RestCue.App;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;

namespace RestCue.App.Lifecycle;

public sealed class WindowsTrayIcon : ITrayIcon
{
    private readonly NotifyIcon notifyIcon;

    private readonly ToolStripMenuItem pauseItem;
    private readonly ToolStripMenuItem pause15;
    private readonly ToolStripMenuItem pause30;
    private readonly ToolStripMenuItem pause60;
    private readonly ToolStripMenuItem pauseManual;
    private readonly ToolStripMenuItem resumeItem;
    private readonly ToolStripMenuItem focusItem;
    private readonly ToolStripMenuItem disableItem;
    private readonly ToolStripMenuItem breakNowItem;

    private bool isPaused;
    private bool isFocusMode;
    private bool isDisabled;
    private ContextMenuStrip menu;
    private int pauseMenuIndex;
    private static readonly Color NormalColor = Color.FromArgb(47, 111, 235);
    private static readonly Color Level1Color = Color.FromArgb(46, 125, 91);
    private static readonly Color Level2Color = Color.FromArgb(196, 128, 20);
    private static readonly Color Level3Color = Color.FromArgb(211, 95, 24);
    private static readonly Color Level4Color = Color.FromArgb(192, 57, 43);
    private static readonly Color SuppressedColor = Color.FromArgb(92, 101, 112);
    private static readonly Color RemindersOffColor = Color.FromArgb(94, 126, 108);
    private static readonly Icon NormalIcon = TrayIconFactory.Create(TrayIconKind.Normal, NormalColor);
    private static readonly Icon Level1Icon = TrayIconFactory.Create(TrayIconKind.Level1, Level1Color);
    private static readonly Icon Level2Icon = TrayIconFactory.Create(TrayIconKind.Level2, Level2Color);
    private static readonly Icon Level3Icon = TrayIconFactory.Create(TrayIconKind.Level3, Level3Color);
    private static readonly Icon Level4Icon = TrayIconFactory.Create(TrayIconKind.Level4, Level4Color);
    private static readonly Icon SuppressedIcon = TrayIconFactory.Create(TrayIconKind.Suppressed, SuppressedColor);
    private static readonly Icon RemindersOffIcon = TrayIconFactory.Create(TrayIconKind.RemindersOff, RemindersOffColor);

    private readonly Dispatcher uiDispatcher;
    private ToastWindow? toastWindow;

    public WindowsTrayIcon()
    {
        uiDispatcher = Dispatcher.CurrentDispatcher;
        pause15 = CreatePauseItem(PausePresets.FifteenMinutes);
        pause30 = CreatePauseItem(PausePresets.ThirtyMinutes);
        pause60 = CreatePauseItem(PausePresets.OneHour);
        pauseManual = new ToolStripMenuItem("直到手動恢復", null, (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty));
        pauseItem = new ToolStripMenuItem("暫停提醒");
        pauseItem.DropDownItems.AddRange(
            pause15, pause30, pause60,
            new ToolStripSeparator(),
            pauseManual);
        resumeItem = new ToolStripMenuItem("繼續提醒", null, TogglePause);
        focusItem = new ToolStripMenuItem("專注模式", null, ToggleFocusMode);
        disableItem = new ToolStripMenuItem("停用提醒", null, ToggleDisable);
        breakNowItem = new ToolStripMenuItem("立即休息", null, (_, _) => BreakNowRequested?.Invoke(this, EventArgs.Empty));

        var menu = new ContextMenuStrip();
        menu.Items.Add("開啟 RestCue", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("今日統計", null, (_, _) => StatisticsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("資料透明檢視", null, (_, _) => DataTransparencyRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("匯出／清除資料", null, (_, _) => DataManagementRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("設定", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("關於與隱私", null, (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(pauseItem);
        pauseMenuIndex = menu.Items.Count - 1;
        menu.Items.Add(focusItem);
        menu.Items.Add(disableItem);
        menu.Items.Add(breakNowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束 RestCue", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = NormalIcon,
            Text = "RestCue – Eye Break Reminder"
        };
        this.menu = menu;
        notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
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
        get => notifyIcon.Visible;
        set => notifyIcon.Visible = value;
    }

    public void SetPauseText(bool isPaused)
    {
        this.isPaused = isPaused;
        if (isPaused)
        {
            menu.Items.RemoveAt(pauseMenuIndex);
            menu.Items.Insert(pauseMenuIndex, resumeItem);
        }
        else
        {
            menu.Items.RemoveAt(pauseMenuIndex);
            menu.Items.Insert(pauseMenuIndex, pauseItem);
        }
    }

    public void SetFocusModeText(bool isFocusMode)
    {
        this.isFocusMode = isFocusMode;
        focusItem.Text = isFocusMode ? "結束專注模式" : "專注模式";
    }

    public void SetDisableText(bool isDisabled)
    {
        this.isDisabled = isDisabled;
        disableItem.Text = isDisabled ? "啟用提醒" : "停用提醒";
    }

    public void SetStatusText(string text)
    {
        notifyIcon.Text = text;
    }

    public void SetPauseEnabled(bool enabled) => pauseItem.Enabled = enabled;

    public void SetFocusModeEnabled(bool enabled) => focusItem.Enabled = enabled;

    public void SetDisableEnabled(bool enabled) => disableItem.Enabled = enabled;

    public void SetBreakNowEnabled(bool enabled) => breakNowItem.Enabled = enabled;

    public void ApplyViewState(TrayViewState state)
    {
        notifyIcon.Icon = GetIconForState(state);
        notifyIcon.Text = App.GetTrayTooltip(state);
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

    private Icon GetIconForState(TrayViewState state)
    {
        // Pause and Disable mean the user has asked for calm: reminders are off, and no
        // rest-debt urgency may override that, no matter how high the level has climbed.
        if (state.Mode is WorkCyclePhase.Paused or WorkCyclePhase.Disabled)
            return RemindersOffIcon;

        // A pending reminder only takes over the icon while there is no debt to show.
        // Above Level 0 the debt signal is the more useful one, and greying it out
        // would hide the severity exactly when it matters.
        if (state.IsSuppressed && state.DebtLevel == RestDebtLevel.Level0)
            return SuppressedIcon;
        return GetIconForDebtLevel(state.DebtLevel);
    }

    private void TogglePause(object? sender, EventArgs e)
    {
        if (isPaused)
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
        if (isFocusMode)
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
        if (isDisabled)
        {
            EnableRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            DisableRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        void DisposeToast()
        {
            toastWindow?.Dispose();
            toastWindow = null;
        }

        if (uiDispatcher.CheckAccess())
        {
            DisposeToast();
        }
        else if (!uiDispatcher.HasShutdownStarted)
        {
            uiDispatcher.Invoke(DisposeToast);
        }

        notifyIcon.Dispose();
    }

    public void ShowLightTouchNotification(string title, string text, NotificationDuration duration)
    {
        void ShowToast()
        {
            toastWindow ??= new ToastWindow();
            toastWindow.ShowToast(title, text, duration);
        }

        if (uiDispatcher.CheckAccess())
        {
            ShowToast();
        }
        else
        {
            uiDispatcher.Invoke(ShowToast);
        }
    }
}
