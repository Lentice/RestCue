using System.Windows;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// A saved setting that the application never picks up is a setting that does not exist.
/// The dialog must hand the saved snapshot back so live-appliable changes — foreground
/// process-name collection first among them, because it is a privacy control — take
/// effect without a relaunch.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class SettingsLiveApplyTests
{
    private readonly WpfApplicationFixture wpf;

    public SettingsLiveApplyTests(WpfApplicationFixture wpf)
    {
        this.wpf = wpf;
    }

    [Fact]
    public void Saving_a_process_name_collection_change_publishes_the_new_settings()
    {
        wpf.Run(() =>
        {
            var repository = new RecordingSettingsRepository();
            var window = CreateWindow(repository, AppSettings.Default with { CollectForegroundProcessNames = false });
            try
            {
                AppSettings? published = null;
                window.SettingsSaved += (_, saved) => published = saved;

                SetProcessNameCollection(window, enabled: true);
                Save(window);

                Assert.NotNull(published);
                Assert.True(published!.CollectForegroundProcessNames);
                Assert.True(repository.Saved!.CollectForegroundProcessNames);

                // Collection is not an engine parameter, so nothing waits for a relaunch.
                Assert.Empty(RestartRequiredSettings.Changed(AppSettings.Default, published));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Saving_publishes_live_appliable_opacity()
    {
        wpf.Run(() =>
        {
            var repository = new RecordingSettingsRepository();
            var window = CreateWindow(repository, AppSettings.Default);
            try
            {
                AppSettings? published = null;
                window.SettingsSaved += (_, saved) => published = saved;

                SetOpacity(window, 0.35);
                Save(window);

                Assert.NotNull(published);
                Assert.Equal(0.35, published!.ReminderOpacity, 3);
                Assert.Empty(RestartRequiredSettings.Changed(AppSettings.Default, published));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void An_invalid_value_publishes_nothing_and_saves_nothing()
    {
        wpf.Run(() =>
        {
            var repository = new RecordingSettingsRepository();
            var window = CreateWindow(repository, AppSettings.Default);
            try
            {
                bool published = false;
                window.SettingsSaved += (_, _) => published = true;

                // Natural pause at or above passive pause is the combination that used to
                // save successfully and silently retire natural-pause reminders.
                SetText(window, "NaturalPauseBox", "30");
                SetText(window, "PassiveBreakBox", "10");
                Save(window);

                Assert.False(published);
                Assert.Null(repository.Saved);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_restart_requiring_change_is_still_published_but_named_as_pending()
    {
        wpf.Run(() =>
        {
            var repository = new RecordingSettingsRepository();
            var window = CreateWindow(repository, AppSettings.Default);
            try
            {
                AppSettings? published = null;
                window.SettingsSaved += (_, saved) => published = saved;

                SetText(window, "IdleThresholdBox", "5");
                Save(window);

                Assert.NotNull(published);
                Assert.Equal(TimeSpan.FromMinutes(5), published!.IdleThreshold);

                IReadOnlyList<string> pending =
                    RestartRequiredSettings.Changed(AppSettings.Default, published);
                Assert.Equal([nameof(AppSettings.IdleThreshold)], pending);
                Assert.Contains("離開判斷時間", SettingsSaveMessage.Build(pending));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Applying_live_settings_does_not_reset_accumulated_work_time()
    {
        // ApplyLiveSettings must not rebuild the engine. Asserted at the engine seam,
        // because "the engine was not rebuilt" is only observable as work time surviving.
        var clock = new StepClock();
        var tracker = WorkCycleTrackerFactory.Create(
            AppSettings.Default with { WorkInterval = TimeSpan.FromMinutes(15) }, clock);

        for (int i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            tracker.Tick(TimeSpan.Zero);
        }

        TimeSpan before = tracker.AccumulatedWorkTime;
        Assert.NotEqual(TimeSpan.Zero, before);

        wpf.Run(() =>
        {
            var main = new MainWindow();
            try
            {
                main.ApplyLiveSettings(AppSettings.Default with
                {
                    CollectForegroundProcessNames = true,
                    ReduceMotion = true,
                    ReminderOpacity = 0.3,
                });
            }
            finally
            {
                main.Close();
            }
        });

        Assert.Equal(before, tracker.AccumulatedWorkTime);
    }

    private static SettingsWindow CreateWindow(ISettingsRepository repository, AppSettings current) =>
        new(repository, new StubRuleRepository(), current);

    private static void Save(SettingsWindow window) =>
        Invoke(window, "OnSaveClick");

    private static void SetProcessNameCollection(SettingsWindow window, bool enabled) =>
        Field<System.Windows.Controls.CheckBox>(window, "CollectProcessNamesCheck").IsChecked = enabled;

    private static void SetOpacity(SettingsWindow window, double value) =>
        Field<System.Windows.Controls.Slider>(window, "ReminderOpacitySlider").Value = value;

    private static void SetText(SettingsWindow window, string name, string text) =>
        Field<System.Windows.Controls.TextBox>(window, name).Text = text;

    private static T Field<T>(SettingsWindow window, string name) =>
        (T)(window.FindName(name) ?? throw new InvalidOperationException($"No control named {name}."));

    private static void Invoke(SettingsWindow window, string method)
    {
        var handler = typeof(SettingsWindow).GetMethod(
            method,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"No method named {method}.");
        handler.Invoke(window, [window, new RoutedEventArgs()]);
    }

    private sealed class RecordingSettingsRepository : ISettingsRepository
    {
        public AppSettings? Saved { get; private set; }

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsLoadResult(AppSettings.Default));

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class StubRuleRepository : IApplicationRuleRepository
    {
        public Task<IReadOnlyList<ApplicationRule>> LoadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationRule>>([]);

        public Task SaveAsync(ApplicationRule rule, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string processName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StepClock : RestCue.Core.Time.IClock
    {
        private DateTimeOffset utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
