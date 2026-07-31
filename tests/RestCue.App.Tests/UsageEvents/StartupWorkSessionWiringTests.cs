using RestCue.App.UsageEvents;
using RestCue.Core.Activity;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using RestCue.Core.Time;
using RestCue.Core.UsageEvents;
using Xunit;

namespace RestCue.App.Tests.UsageEvents;

/// <summary>
/// Startup cannot attach the work-session recorder before the status window starts
/// tracking, because the recorder's writer is built from the tracker the status window
/// creates. This exercises that real order against a real <see cref="MainWindow"/>:
/// start tracking first, attach second, exactly as application startup does.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class StartupWorkSessionWiringTests
{
    private readonly WpfApplicationFixture wpf;

    public StartupWorkSessionWiringTests(WpfApplicationFixture wpf)
    {
        this.wpf = wpf;
    }

    [Fact]
    public void Attaching_after_activity_tracking_still_records_the_first_work_session()
    {
        wpf.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                var recorded = new List<UsageEventType>();
                var recorder = new WorkSessionRecorder(recorded.Add);

                StartTracking(window);

                // The opening phase has already gone out by now — this is the transition
                // the recorder used to miss.
                Assert.Equal(WorkCyclePhase.Working, window.WorkCycleTracker!.CurrentPhase);

                recorder.Attach(window);

                Assert.Equal([UsageEventType.WorkSessionStarted], recorded);
                Assert.True(recorder.IsWorkInProgress);
            }
            finally
            {
                window.StopActivityTracking();
                window.Close();
            }
        });
    }

    [Fact]
    public void The_first_command_after_startup_closes_the_work_session()
    {
        wpf.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                var recorded = new List<UsageEventType>();
                var recorder = new WorkSessionRecorder(recorded.Add);

                StartTracking(window);
                recorder.Attach(window);

                window.WorkCycleTracker!.Pause();
                window.PublishCycleStatus();

                Assert.Equal(
                    [UsageEventType.WorkSessionStarted, UsageEventType.WorkSessionEnded],
                    recorded);
            }
            finally
            {
                window.StopActivityTracking();
                window.Close();
            }
        });
    }

    private static void StartTracking(MainWindow window) =>
        window.StartActivityTracking(
            new ActiveActivityMonitor(),
            AppSettings.Default,
            new FakeClock());

    private sealed class ActiveActivityMonitor : IUserActivityMonitor
    {
        public UserActivitySample GetCurrentActivity() =>
            UserActivitySample.Available(TimeSpan.Zero);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public TimeSpan Elapsed => TimeSpan.Zero;
    }
}
