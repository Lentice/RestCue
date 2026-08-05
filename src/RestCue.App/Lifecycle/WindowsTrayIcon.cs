using System.Drawing;
using System.Windows.Forms;
using Microsoft.Toolkit.Uwp.Notifications;
using RestCue.Core.Domain;
using RestCue.Core.Settings;
using Windows.UI.Notifications;

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
    private RestDebtLevel currentDebtLevel;
    private bool isSuppressed;
    private ContextMenuStrip menu;
    private int pauseMenuIndex;
    private static readonly Color NormalColor = Color.FromArgb(47, 111, 235);
    private static readonly Color Level1Color = Color.FromArgb(46, 125, 91);
    private static readonly Color Level2Color = Color.FromArgb(196, 128, 20);
    private static readonly Color Level3Color = Color.FromArgb(211, 95, 24);
    private static readonly Color Level4Color = Color.FromArgb(192, 57, 43);
    private static readonly Icon NormalIcon = TrayIconFactory.Create(NormalColor);
    private static readonly Icon Level1Icon = TrayIconFactory.Create(Level1Color);
    private static readonly Icon Level2Icon = TrayIconFactory.Create(Level2Color);
    private static readonly Icon Level3Icon = TrayIconFactory.Create(Level3Color);
    private static readonly Icon Level4Icon = TrayIconFactory.Create(Level4Color);
    private static readonly Icon SuppressedIcon = TrayIconFactory.Create(Color.FromArgb(92, 101, 112));

    internal const string BreakNowToastArgument = "restcue-break-now";
    private readonly SynchronizationContext? uiContext;

    public WindowsTrayIcon()
    {
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

        uiContext = SynchronizationContext.Current;
        try
        {
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        }
        catch (Exception)
        {
            // Registering the toast activator needs a working notification platform. Losing
            // the toast button is survivable; the tray menu still offers 立即休息.
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e) =>
        HandleToastActivation(e.Argument);

    /// <summary>
    /// Toast activations arrive on a platform thread, so the request is marshalled back to
    /// the UI context the tray icon was created on before any window work happens.
    /// </summary>
    internal void HandleToastActivation(string? argument)
    {
        if (!string.Equals(argument, BreakNowToastArgument, StringComparison.Ordinal))
            return;

        void Raise()
        {
            if (breakNowItem.Enabled)
            {
                BreakNowRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        if (uiContext != null && uiContext != SynchronizationContext.Current)
        {
            uiContext.Post(_ => Raise(), null);
        }
        else
        {
            Raise();
        }
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

    public void SetSuppressedState(bool isSuppressed)
    {
        this.isSuppressed = isSuppressed;
        notifyIcon.Icon = GetIconForCurrentState();
    }

    public void SetDebtLevel(RestDebtLevel level)
    {
        currentDebtLevel = level;
        notifyIcon.Icon = GetIconForCurrentState();
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

    private static Color GetColorForDebtLevel(RestDebtLevel level)
    {
        return level switch
        {
            RestDebtLevel.Level1 => Level1Color,
            RestDebtLevel.Level2 => Level2Color,
            RestDebtLevel.Level3 => Level3Color,
            RestDebtLevel.Level4 => Level4Color,
            _ => NormalColor
        };
    }

    private Icon GetIconForCurrentState()
    {
        // A pending reminder only takes over the icon while there is no debt to show.
        // Above Level 0 the debt colour is the more useful signal, and greying it out
        // would hide the severity exactly when it matters.
        if (isSuppressed && currentDebtLevel == RestDebtLevel.Level0)
            return SuppressedIcon;
        return GetIconForDebtLevel(currentDebtLevel);
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
        try
        {
            ToastNotificationManagerCompat.OnActivated -= OnToastActivated;

            // A toast left in Action Center outlives the process, and its 立即休息 button
            // cannot be honoured once RestCue is gone. Clearing on shutdown keeps the
            // notification list free of buttons that would do nothing.
            ToastNotificationManagerCompat.History.Clear();
        }
        catch (Exception)
        {
            // Mirrors the guarded subscription in the constructor.
        }

        notifyIcon.Dispose();
    }

    /// <summary>
    /// Shows a real WinRT toast. The legacy <see cref="NotifyIcon.ShowBalloonTip"/> path is
    /// kept only as a fallback: its timeout argument is ignored by Windows, so it cannot
    /// honour <paramref name="duration"/>.
    /// </summary>
    public void ShowLightTouchNotification(string title, string text, NotificationDuration duration)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(text);

            string? accent = ToastAccentImage.TryGetPath(GetColorForDebtLevel(currentDebtLevel));
            if (accent != null)
            {
                builder.AddAppLogoOverride(new Uri(accent), ToastGenericAppLogoCrop.Circle);
            }

            builder.AddButton(new ToastButton()
                .SetContent("立即休息")
                .AddArgument(BreakNowToastArgument)
                .SetBackgroundActivation());

            if (duration == NotificationDuration.UntilDismissed)
            {
                // Only the reminder scenario keeps a toast on screen indefinitely, and
                // Windows expects such a toast to carry a way out of it.
                builder.SetToastScenario(ToastScenario.Reminder)
                    .AddButton(new ToastButton().SetContent("知道了").SetDismissActivation());
            }

            ToastContent content = builder.GetToastContent();
            content.Duration = duration == NotificationDuration.Default
                ? ToastDuration.Short
                : ToastDuration.Long;

            ToastNotificationManagerCompat.CreateToastNotifier()
                .Show(new ToastNotification(content.GetXml()));
        }
        catch (Exception)
        {
            // Toasts can be unavailable entirely (group policy, broken notification
            // platform). A reminder the user never sees is worse than one of the wrong
            // length, so degrade to the balloon rather than swallow the cue.
            notifyIcon.ShowBalloonTip(10000, title, text, ToolTipIcon.Info);
        }
    }
}
