using System.Collections.ObjectModel;
using System.Windows;
using RestCue.App.Lifecycle;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;

namespace RestCue.App;

public sealed partial class SettingsWindow : Window
{
    private readonly ISettingsRepository repository;
    private readonly IApplicationRuleRepository ruleRepository;
    private readonly AppSettings currentSettings;
    private readonly AppSettingsValidator validator = new();
    private readonly ObservableCollection<ApplicationRule> rules = [];
    private string? editingProcessName;
    private readonly SemaphoreSlim ruleGate = new(1, 1);

    public event EventHandler? ApplicationRulesChanged;

    public SettingsWindow(
        ISettingsRepository repository,
        IApplicationRuleRepository ruleRepository,
        AppSettings currentSettings)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(ruleRepository);
        ArgumentNullException.ThrowIfNull(currentSettings);
        this.repository = repository;
        this.ruleRepository = ruleRepository;
        this.currentSettings = currentSettings;
        InitializeComponent();
        LoadSettings();
        LoadStartupState();
        Loaded += async (_, _) => await LoadRulesAsync();
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
            case RestCue.Core.Settings.BreakGuideMode.Cue:
                GuideCueRadio.IsChecked = true;
                break;
            case RestCue.Core.Settings.BreakGuideMode.Voice:
                GuideVoiceRadio.IsChecked = true;
                break;
            case RestCue.Core.Settings.BreakGuideMode.NumberlessVisual:
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

    private void OnAddRuleTypeChanged(object sender, RoutedEventArgs e)
    {
        if (CustomIntervalPanel is null)
            return;
        CustomIntervalPanel.Visibility = AddRuleCustomRadio.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task LoadRulesAsync()
    {
        try
        {
            var loaded = await ruleRepository.LoadAllAsync();
            rules.Clear();
            foreach (var rule in loaded)
            {
                rules.Add(rule);
            }
            RefreshRuleList();
        }
        catch (Exception ex)
        {
            ShowStatus($"無法載入應用程式規則: {ex.Message}", isError: true);
        }
    }

    private void NotifyRulesChanged()
    {
        ApplicationRulesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshRuleList()
    {
        RuleListStack.Children.Clear();

        if (rules.Count == 0)
        {
            RuleListStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "尚未新增任何規則。",
                Style = (Style)FindResource("FieldHintStyle"),
            });
            return;
        }

        foreach (var rule in rules)
        {
            var row = new System.Windows.Controls.Grid();
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = $"{rule.ProcessName} — {RuleTypeLabel(rule.RuleType)}",
                Style = (Style)FindResource("BodyTextStyle"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };
            System.Windows.Controls.Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var editButton = BuildRuleButton("編輯", "SecondaryButtonStyle", OnEditRuleClick, rule.ProcessName, this);
            System.Windows.Controls.Grid.SetColumn(editButton, 1);
            row.Children.Add(editButton);

            var deleteButton = BuildRuleButton("刪除", "DangerButtonStyle", OnDeleteRuleClick, rule.ProcessName, this);
            System.Windows.Controls.Grid.SetColumn(deleteButton, 2);
            row.Children.Add(deleteButton);

            row.Margin = new System.Windows.Thickness(0, 0, 0, 6);
            RuleListStack.Children.Add(row);
        }
    }

    private static System.Windows.Controls.Button BuildRuleButton(
        string text, string styleKey, System.Windows.RoutedEventHandler clickHandler, string processName,
        System.Windows.FrameworkElement resourceSource)
    {
        return new System.Windows.Controls.Button
        {
            Content = text,
            Style = (Style)resourceSource.FindResource(styleKey),
            MinWidth = 50,
            MinHeight = 26,
            Margin = new System.Windows.Thickness(4, 2, 0, 2),
            Padding = new System.Windows.Thickness(8, 2, 8, 2),
            Tag = processName,
        };
    }

    private static string RuleTypeLabel(ApplicationRuleType type) => type switch
    {
        ApplicationRuleType.Normal => "一般",
        ApplicationRuleType.TrayOnly => "僅系統列",
        ApplicationRuleType.Silent => "無聲",
        ApplicationRuleType.CustomInterval => "自訂間隔",
        _ => type.ToString(),
    };

    private ApplicationRuleType SelectedRuleType
    {
        get
        {
            if (AddRuleNormalRadio.IsChecked == true) return ApplicationRuleType.Normal;
            if (AddRuleTrayRadio.IsChecked == true) return ApplicationRuleType.TrayOnly;
            if (AddRuleSilentRadio.IsChecked == true) return ApplicationRuleType.Silent;
            return ApplicationRuleType.CustomInterval;
        }
        set
        {
            AddRuleNormalRadio.IsChecked = value == ApplicationRuleType.Normal;
            AddRuleTrayRadio.IsChecked = value == ApplicationRuleType.TrayOnly;
            AddRuleSilentRadio.IsChecked = value == ApplicationRuleType.Silent;
            AddRuleCustomRadio.IsChecked = value == ApplicationRuleType.CustomInterval;
        }
    }

    private void OnEditRuleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string processName })
            return;

        var rule = rules.FirstOrDefault(r =>
            string.Equals(r.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
        if (rule == null)
            return;

        editingProcessName = rule.ProcessName;
        AddRuleProcessBox.Text = rule.ProcessName;
        AddRuleProcessBox.IsEnabled = false;
        SelectedRuleType = rule.RuleType;

        if (rule.RuleType == ApplicationRuleType.CustomInterval)
        {
            AddRuleIntervalBox.Text = ((int)(rule.CustomInterval?.TotalMinutes ?? 30)).ToString();
        }

        AddRuleButton.Content = "更新";
        CancelEditButton.Visibility = Visibility.Visible;
    }

    private async void OnDeleteRuleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string processName })
            return;

        await ruleGate.WaitAsync();
        try
        {
            await ruleRepository.DeleteAsync(processName);
            var toRemove = rules.FirstOrDefault(r =>
                string.Equals(r.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
            if (toRemove != null)
            {
                rules.Remove(toRemove);
            }
            RefreshRuleList();
            NotifyRulesChanged();
            ShowStatus($"已刪除「{processName}」的規則。", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"刪除失敗: {ex.Message}", isError: true);
        }
        finally
        {
            ruleGate.Release();
        }
    }

    private void CancelEdit()
    {
        editingProcessName = null;
        AddRuleProcessBox.Text = "";
        AddRuleProcessBox.IsEnabled = true;
        AddRuleIntervalBox.Text = "";
        SelectedRuleType = ApplicationRuleType.TrayOnly;
        AddRuleButton.Content = "新增";
        CancelEditButton.Visibility = Visibility.Collapsed;
    }

    private void OnCancelEditClick(object sender, RoutedEventArgs e)
    {
        CancelEdit();
    }

    private async void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        string processName = AddRuleProcessBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            ShowStatus("請輸入處理程序名稱。", isError: true);
            AddRuleProcessBox.Focus();
            return;
        }

        var ruleType = SelectedRuleType;

        TimeSpan? customInterval = null;
        if (ruleType == ApplicationRuleType.CustomInterval)
        {
            if (!int.TryParse(AddRuleIntervalBox.Text, out int minutes) || minutes < 1 || minutes > 120)
            {
                ShowStatus("自訂間隔須為 1–120 的整數（分鐘）。", isError: true);
                AddRuleIntervalBox.Focus();
                return;
            }
            customInterval = TimeSpan.FromMinutes(minutes);
        }

        var rule = new ApplicationRule
        {
            ProcessName = processName,
            RuleType = ruleType,
            CustomInterval = customInterval,
        };

        await ruleGate.WaitAsync();
        try
        {
            if (editingProcessName != null)
            {
                await ruleRepository.SaveAsync(rule);
                var toRemove = rules.FirstOrDefault(r =>
                    string.Equals(r.ProcessName, editingProcessName, StringComparison.OrdinalIgnoreCase));
                if (toRemove != null)
                    rules.Remove(toRemove);
                rules.Add(rule);
                RefreshRuleList();
                CancelEdit();
                NotifyRulesChanged();
                ShowStatus($"已更新「{rule.ProcessName}」的規則。", isError: false);
            }
            else
            {
                if (rules.Any(r => string.Equals(r.ProcessName, processName, StringComparison.OrdinalIgnoreCase)))
                {
                    ShowStatus($"「{processName}」的規則已存在。", isError: true);
                    return;
                }

                await ruleRepository.SaveAsync(rule);
                rules.Add(rule);
                RefreshRuleList();
                CancelEdit();
                NotifyRulesChanged();
                ShowStatus($"已新增「{rule.ProcessName}」的規則。", isError: false);
            }
        }
        catch (Exception ex)
        {
            ShowStatus(editingProcessName != null ? $"更新失敗: {ex.Message}" : $"新增失敗: {ex.Message}", isError: true);
        }
        finally
        {
            ruleGate.Release();
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
            ShowStatus($"儲存失敗: {ex.Message}", isError: true);
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

        ShowStatus("設定已儲存。部分設定將於下次啟動時生效。", isError: false);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Shows the banner above the form. Success and failure differ by colour
    /// and by wording, so the state is not conveyed by colour alone.
    /// </summary>
    private void ShowStatus(string message, bool isError)
    {
        StatusMessage.Text = message;
        StatusMessage.Foreground = (System.Windows.Media.Brush)FindResource(
            isError ? "DangerTextBrush" : "SuccessBrush");
        StatusBanner.Visibility = Visibility.Visible;
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

        var mode = GuideCueRadio.IsChecked == true ? RestCue.Core.Settings.BreakGuideMode.Cue
            : GuideVoiceRadio.IsChecked == true ? RestCue.Core.Settings.BreakGuideMode.Voice
            : RestCue.Core.Settings.BreakGuideMode.NumberlessVisual;

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
        ShowStatus(message, isError: true);
        box.Focus();
        box.SelectAll();
    }

    private void ClearErrors()
    {
        StatusBanner.Visibility = Visibility.Collapsed;
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
        ShowStatus("請修正以下錯誤：", isError: true);

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
