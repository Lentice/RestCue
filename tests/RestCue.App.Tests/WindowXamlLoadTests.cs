using System.Windows;
using System.Windows.Threading;
using RestCue.Core.Reminders;
using RestCue.Core.Settings;
using RestCue.Core.Transparency;
using RestCue.Core.UsageEvents;
using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// Constructs every window so that its compiled XAML is actually parsed.
/// A missing or misspelled StaticResource, a Click handler with no matching
/// code-behind method, or a broken template only fails at load time — these
/// tests turn that into a build-time-adjacent failure instead of a crash the
/// first time a user opens the window from the tray.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class WindowXamlLoadTests
{
    private readonly WpfApplicationFixture wpf;

    public WindowXamlLoadTests(WpfApplicationFixture wpf)
    {
        this.wpf = wpf;
    }

    [Fact]
    public void MainWindow_loads()
    {
        wpf.Construct(() => new MainWindow());
    }

    [Fact]
    public void ReminderWindow_loads()
    {
        wpf.Construct(() => new ReminderWindow());
    }

    [Fact]
    public void AboutWindow_loads()
    {
        wpf.Construct(() => new AboutWindow());
    }

    [Fact]
    public void SettingsWindow_loads()
    {
        wpf.Construct(() => new SettingsWindow(new StubSettingsRepository(), new StubApplicationRuleRepository(), AppSettings.Default));
    }

    [Fact]
    public void StatisticsWindow_loads()
    {
        wpf.Construct(() => new StatisticsWindow(new StubStatisticsService()));
    }

    [Fact]
    public void TransparencyWindow_loads()
    {
        wpf.Construct(() => new TransparencyWindow(new StubTransparencyService()));
    }

    [Fact]
    public void DataManagementWindow_loads()
    {
        wpf.Construct(() => new DataManagementWindow(
            new StubUsageEventRepository(),
            new StubSettingsRepository()));
    }

    private sealed class StubApplicationRuleRepository : IApplicationRuleRepository
    {
        public Task<IReadOnlyList<ApplicationRule>> LoadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationRule>>([]);

        public Task SaveAsync(ApplicationRule rule, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string processName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubSettingsRepository : ISettingsRepository
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubStatisticsService : IDailyStatisticsService
    {
        public Task<DailyStatistics> ComputeAsync(
            DateOnly date,
            TimeZoneInfo timezone,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubTransparencyService : IDataTransparencyService
    {
        public Task<DataTransparencySnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubUsageEventRepository : IUsageEventRepository
    {
        public Task WriteAsync(
            UsageEventType eventType,
            DateTimeOffset occurredUtc,
            UsageEventPayload? payload = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<UsageEvent>> QueryAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

[CollectionDefinition(Name)]
public sealed class WpfCollection : ICollectionFixture<WpfApplicationFixture>
{
    public const string Name = "WPF application";
}

/// <summary>
/// Hosts a single WPF <see cref="Application"/> on a dedicated STA thread with
/// the shared theme merged in, mirroring how App.xaml wires it at runtime.
/// Only one Application may exist per process, hence the collection fixture.
/// </summary>
public sealed class WpfApplicationFixture : IDisposable
{
    private readonly Dispatcher dispatcher;

    public WpfApplicationFixture()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? started = null;

        var thread = new Thread(() =>
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/RestCue;component/Themes/Theme.xaml",
                    UriKind.Absolute)
            });

            started = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(30));

        dispatcher = started ?? throw new InvalidOperationException(
            "WPF dispatcher thread did not start.");
    }

    /// <summary>
    /// Builds a window on the UI thread and immediately closes it. Any XAML
    /// load failure surfaces as the exception thrown by the factory.
    /// </summary>
    public void Construct(Func<Window> factory)
    {
        dispatcher.Invoke(() =>
        {
            var window = factory();
            window.Close();
        });
    }

    public void Dispose()
    {
        dispatcher.InvokeShutdown();
    }
}
