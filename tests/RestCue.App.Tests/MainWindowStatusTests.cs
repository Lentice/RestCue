using System.Windows.Controls;
using RestCue.Core.Activity;
using RestCue.Core.Settings;
using RestCue.Core.Time;
using Xunit;

namespace RestCue.App.Tests;

[Collection(WpfCollection.Name)]
public sealed class MainWindowStatusTests
{
    private readonly WpfApplicationFixture wpf;

    public MainWindowStatusTests(WpfApplicationFixture wpf)
    {
        this.wpf = wpf;
    }

    [Fact]
    public void Shows_work_time_and_next_rest_need_in_window_and_tray_summary()
    {
        wpf.Run(() =>
        {
            var clock = new MutableClock();
            var window = new MainWindow();
            int trayStatusChanges = 0;

            try
            {
                window.TrayStatusChanged += (_, _) => trayStatusChanges++;
                window.StartActivityTracking(
                    new ActiveActivityMonitor(),
                    AppSettings.Default,
                    clock);

                window.WorkCycleTracker!.Tick(TimeSpan.Zero);
                clock.Advance(TimeSpan.FromMinutes(7));
                window.WorkCycleTracker.Tick(TimeSpan.Zero);
                window.PublishCycleStatus();

                Assert.Equal("7 分鐘", Text(window, "EffectiveWorkTimeText"));
                Assert.Equal("約 13 分鐘", Text(window, "NextRestTimeText"));
                Assert.Contains("有效工作 7分", window.CurrentTrayStatusText);
                Assert.Contains("距休息需求 約13分", window.CurrentTrayStatusText);
                Assert.Equal(1, trayStatusChanges);
            }
            finally
            {
                window.StopActivityTracking();
                window.Close();
            }
        });
    }

    private static string Text(MainWindow window, string name) =>
        ((TextBlock)(window.FindName(name)
            ?? throw new InvalidOperationException($"No text block named {name}."))).Text;

    private sealed class ActiveActivityMonitor : IUserActivityMonitor
    {
        public UserActivitySample GetCurrentActivity() =>
            UserActivitySample.Available(TimeSpan.Zero);
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } =
            new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public TimeSpan Elapsed { get; private set; }

        public void Advance(TimeSpan duration)
        {
            UtcNow += duration;
            Elapsed += duration;
        }
    }
}
