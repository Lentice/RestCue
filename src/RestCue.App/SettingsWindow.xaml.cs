using System.Windows;
using RestCue.App.Lifecycle;
using RestCue.Core.Settings;

namespace RestCue.App;

public sealed partial class SettingsWindow : Window
{
    private readonly ISettingsRepository repository;
    private readonly AppSettings currentSettings;
    private readonly AppSettingsValidator validator = new();

    public SettingsWindow(ISettingsRepository repository, AppSettings currentSettings)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(currentSettings);
        this.repository = repository;
        this.currentSettings = currentSettings;
        InitializeComponent();
        LoadSettings();
        LoadStartupState();
    }

    private void LoadSettings()
    {
        WorkIntervalBox.Text = ((int)currentSettings.WorkInterval.TotalMinutes).ToString();
        NaturalPauseBox.Text = ((int)currentSettings.NaturalPauseThreshold.TotalSeconds).ToString();
        MaxReminderWaitBox.Text = ((int)currentSettings.MaximumReminderWait.TotalMinutes).ToString();
        IdleThresholdBox.Text = ((int)currentSettings.IdleThreshold.TotalMinutes).ToString();
        PassiveBreakBox.Text = ((int)currentSettings.PassiveBreakThreshold.TotalSeconds).ToString();
        BreakDurationBox.Text = ((int)currentSettings.BreakDuration.TotalSeconds).ToString();
        SnoozeDurationBox.Text = ((int)currentSettings.SnoozeDuration.TotalMinutes).ToString();
        ReminderDisplayBox.Text = ((int)currentSettings.ReminderDisplayDuration.TotalSeconds).ToString();
        RetryCooldownBox.Text = ((int)currentSettings.RetryCooldown.TotalMinutes).ToString();

        DebtLevel2Box.Text = ((int)currentSettings.DebtLevel2Threshold.TotalMinutes).ToString();
        DebtLevel3Box.Text = ((int)currentSettings.DebtLevel3Threshold.TotalMinutes).ToString();
        DebtLevel4Box.Text = ((int)currentSettings.DebtLevel4Threshold.TotalMinutes).ToString();

        ReminderOpacitySlider.Value = currentSettings.ReminderOpacity;
        ReminderOpacitySlider.ValueChanged += (_, _) =>
            ReminderOpacityText.Text = $"{(int)(ReminderOpacitySlider.Value * 100)}%";

        CollectProcessNamesCheck.IsChecked = currentSettings.CollectForegroundProcessNames;

        switch (currentSettings.BreakGuideMode)
        {
            case BreakGuideMode.Cue:
                GuideCueRadio.IsChecked = true;
                break;
            case BreakGuideMode.Voice:
                GuideVoiceRadio.IsChecked = true;
                break;
            case BreakGuideMode.NumberlessVisual:
                GuideVisualRadio.IsChecked = true;
                break;
        }
    }

    private void LoadStartupState()
    {
        try
        {
            StartupCheck.IsChecked = StartupManager.IsEnabled;
            StartupDiagnostics.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Text = $"開機啟動狀態讀取失敗: {ex.Message}";
            StartupDiagnostics.Visibility = Visibility.Visible;
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ClearErrors();

        var parsed = ParseValues();
        if (parsed == null)
            return;

        var errors = validator.Validate(parsed);
        if (errors.Count > 0)
        {
            ShowValidationErrors(errors);
            return;
        }

        try
        {
            await repository.SaveAsync(parsed);
        }
        catch (Exception ex)
        {
            StatusMessage.Text = $"儲存失敗: {ex.Message}";
            StatusMessage.Visibility = Visibility.Visible;
            StatusMessage.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        if (StartupCheck.IsChecked == true != StartupManager.IsEnabled)
        {
            try
            {
                if (StartupCheck.IsChecked == true)
                    StartupManager.Enable();
                else
                    StartupManager.Disable();
                StartupDiagnostics.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Text = $"開機啟動設定失敗: {ex.Message}";
                StartupDiagnostics.Visibility = Visibility.Visible;
                return;
            }
        }

        StatusMessage.Text = "設定已儲存。部分設定將於下次啟動時生效。";
        StatusMessage.Visibility = Visibility.Visible;
        StatusMessage.Foreground = System.Windows.Media.Brushes.Green;
    }

    private AppSettings? ParseValues()
    {
        if (!TryParseInt(WorkIntervalBox, out int workInterval, "工作間隔"))
            return null;
        if (!TryParseInt(NaturalPauseBox, out int naturalPause, "自然停頓門檻"))
            return null;
        if (!TryParseInt(MaxReminderWaitBox, out int maxWait, "最長提醒等待"))
            return null;
        if (!TryParseInt(IdleThresholdBox, out int idleThreshold, "離開判斷時間"))
            return null;
        if (!TryParseInt(PassiveBreakBox, out int passiveBreak, "被動休息門檻"))
            return null;
        if (!TryParseInt(BreakDurationBox, out int breakDuration, "休息長度"))
            return null;
        if (!TryParseInt(SnoozeDurationBox, out int snooze, "延後長度"))
            return null;
        if (!TryParseInt(ReminderDisplayBox, out int reminderDisplay, "提醒顯示時間"))
            return null;
        if (!TryParseInt(RetryCooldownBox, out int retryCooldown, "提醒重試冷卻"))
            return null;
        if (!TryParseInt(DebtLevel2Box, out int debtL2, "Level 2"))
            return null;
        if (!TryParseInt(DebtLevel3Box, out int debtL3, "Level 3"))
            return null;
        if (!TryParseInt(DebtLevel4Box, out int debtL4, "Level 4"))
            return null;

        var mode = GuideCueRadio.IsChecked == true ? BreakGuideMode.Cue
            : GuideVoiceRadio.IsChecked == true ? BreakGuideMode.Voice
            : BreakGuideMode.NumberlessVisual;

        return currentSettings with
        {
            WorkInterval = TimeSpan.FromMinutes(workInterval),
            NaturalPauseThreshold = TimeSpan.FromSeconds(naturalPause),
            MaximumReminderWait = TimeSpan.FromMinutes(maxWait),
            IdleThreshold = TimeSpan.FromMinutes(idleThreshold),
            PassiveBreakThreshold = TimeSpan.FromSeconds(passiveBreak),
            BreakDuration = TimeSpan.FromSeconds(breakDuration),
            SnoozeDuration = TimeSpan.FromMinutes(snooze),
            ReminderDisplayDuration = TimeSpan.FromSeconds(reminderDisplay),
            RetryCooldown = TimeSpan.FromMinutes(retryCooldown),
            DebtLevel2Threshold = TimeSpan.FromMinutes(debtL2),
            DebtLevel3Threshold = TimeSpan.FromMinutes(debtL3),
            DebtLevel4Threshold = TimeSpan.FromMinutes(debtL4),
            ReminderOpacity = ReminderOpacitySlider.Value,
            CollectForegroundProcessNames = CollectProcessNamesCheck.IsChecked == true,
            BreakGuideMode = mode
        };
    }

    private bool TryParseInt(System.Windows.Controls.TextBox box, out int value, string name)
    {
        if (int.TryParse(box.Text, out value))
            return true;
        ShowFieldError(box, $"{name} 必須為整數。");
        return false;
    }

    private void ShowFieldError(System.Windows.Controls.TextBox box, string message)
    {
        StatusMessage.Text = message;
        StatusMessage.Visibility = Visibility.Visible;
        StatusMessage.Foreground = System.Windows.Media.Brushes.Red;
        box.Focus();
        box.SelectAll();
    }

    private void ClearErrors()
    {
        StatusMessage.Visibility = Visibility.Collapsed;
        foreach (var errorBox in new[]
        {
            WorkIntervalError, NaturalPauseError, MaxReminderWaitError,
            IdleThresholdError, PassiveBreakError, BreakDurationError,
            SnoozeDurationError, ReminderDisplayError, RetryCooldownError,
            DebtLevel2Error, DebtLevel3Error, DebtLevel4Error,
            DebtOrderError
        })
        {
            errorBox.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowValidationErrors(IReadOnlyList<SettingsValidationError> errors)
    {
        StatusMessage.Text = "請修正以下錯誤：";
        StatusMessage.Visibility = Visibility.Visible;
        StatusMessage.Foreground = System.Windows.Media.Brushes.Red;

        foreach (var error in errors)
        {
            var target = error.Field switch
            {
                nameof(AppSettings.WorkInterval) => WorkIntervalError,
                nameof(AppSettings.NaturalPauseThreshold) => NaturalPauseError,
                nameof(AppSettings.MaximumReminderWait) => MaxReminderWaitError,
                nameof(AppSettings.IdleThreshold) => IdleThresholdError,
                nameof(AppSettings.PassiveBreakThreshold) => PassiveBreakError,
                nameof(AppSettings.BreakDuration) => BreakDurationError,
                nameof(AppSettings.SnoozeDuration) => SnoozeDurationError,
                nameof(AppSettings.ReminderDisplayDuration) => ReminderDisplayError,
                nameof(AppSettings.RetryCooldown) => RetryCooldownError,
                nameof(AppSettings.DebtLevel2Threshold) => DebtLevel2Error,
                nameof(AppSettings.DebtLevel3Threshold) => DebtLevel3Error,
                nameof(AppSettings.DebtLevel4Threshold) => DebtLevel4Error,
                nameof(AppSettings.ReminderOpacity) => null,
                nameof(AppSettings.BreakGuideMode) => null,
                _ => null
            };

            if (target != null)
            {
                target.Text = error.Message;
                target.Visibility = Visibility.Visible;
            }
            else if (error.Field.StartsWith("DebtLevel"))
            {
                DebtOrderError.Text = error.Message;
                DebtOrderError.Visibility = Visibility.Visible;
            }
            else
            {
                if (StatusMessage.Text.Length < 200)
                    StatusMessage.Text += $"\n{error.Message}";
            }
        }
    }
}
