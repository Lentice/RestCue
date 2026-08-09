using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// Application-rule suggestions are presented on a non-modal, non-activating surface;
/// approving still persists the rule and dismissing still records the dismissal.
/// </summary>
public sealed class SuggestionWiringTests
{
    [Fact]
    public async Task Approving_a_suggestion_persists_a_tray_only_rule_for_the_process()
    {
        var repository = new RecordingRuleRepository();
        IReadOnlyList<ApplicationRule>? applied = null;

        await App.ApproveSuggestionAsync(
            repository,
            rules => applied = rules,
            "zoom");

        var saved = Assert.Single(repository.Rules);
        Assert.Equal("zoom", saved.ProcessName);
        Assert.Equal(ApplicationRuleType.TrayOnly, saved.RuleType);
        Assert.Same(repository.Rules, applied);
    }

    [Fact]
    public async Task Dismissing_a_suggestion_records_the_dismissal()
    {
        var store = new RecordingSuggestionStore();

        await App.DismissSuggestionAsync(store, "zoom");

        Assert.Contains("zoom", store.Dismissed);
    }

    [Fact]
    public async Task It_rejects_missing_inputs()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            App.ApproveSuggestionAsync(null!, null!, "zoom"));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            App.ApproveSuggestionAsync(new RecordingRuleRepository(), null!, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            App.DismissSuggestionAsync(null!, "zoom"));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            App.DismissSuggestionAsync(new RecordingSuggestionStore(), null!));
    }

    private sealed class RecordingRuleRepository : IApplicationRuleRepository
    {
        public List<ApplicationRule> Rules { get; } = [];

        public Task<IReadOnlyList<ApplicationRule>> LoadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationRule>>(Rules);

        public Task SaveAsync(ApplicationRule rule, CancellationToken cancellationToken = default)
        {
            Rules.Add(rule);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string processName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingSuggestionStore : ISuggestionStore
    {
        public List<string> Dismissed { get; } = [];

        public Task<IReadOnlySet<string>> GetDismissedProcessNamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task DismissAsync(string processName, CancellationToken cancellationToken = default)
        {
            Dismissed.Add(processName);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// The suggestion surface must present and respond without ever activating, stealing
/// focus, or appearing in the taskbar.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class SuggestionWindowTests
{
    private readonly WpfApplicationFixture wpf;

    public SuggestionWindowTests(WpfApplicationFixture wpf)
    {
        this.wpf = wpf;
    }

    [Fact]
    public void The_suggestion_surface_does_not_activate_and_stays_out_of_the_taskbar()
    {
        wpf.Run(() =>
        {
            var window = new SuggestionWindow();
            try
            {
                Assert.False(window.ShowActivated);
                Assert.False(window.ShowInTaskbar);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Approve_and_dismiss_raise_their_events_from_the_surface()
    {
        wpf.Run(() =>
        {
            var window = new SuggestionWindow();
            try
            {
                int approved = 0, dismissed = 0;
                window.Approved += (_, _) => approved++;
                window.Dismissed += (_, _) => dismissed++;

                window.ShowSuggestion("zoom");
                Click(window, "ApproveButton");
                Click(window, "DismissButton");

                Assert.Equal(1, approved);
                Assert.Equal(1, dismissed);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Click(SuggestionWindow window, string name) =>
        Button(window, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static Button Button(SuggestionWindow window, string name) =>
        (Button)(window.FindName(name) ?? throw new InvalidOperationException($"No button named {name}."));
}
