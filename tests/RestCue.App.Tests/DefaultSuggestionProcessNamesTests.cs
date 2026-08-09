using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// Startup computes which default application-rule suggestions to offer; a suggestion
/// already turned into a rule or dismissed by the user must not be offered again.
/// </summary>
public sealed class DefaultSuggestionProcessNamesTests
{
    private static readonly string[] Defaults = ["vlc", "zoom", "powerpnt"];

    [Fact]
    public void Dismissed_names_are_excluded()
    {
        IReadOnlySet<string> result = App.GetDefaultSuggestionProcessNames(
            Defaults, [], ["zoom"]);

        Assert.Equal(new HashSet<string> { "vlc", "powerpnt" }, result);
        Assert.DoesNotContain("zoom", result);
    }

    [Fact]
    public void Loaded_rule_names_are_excluded()
    {
        IReadOnlySet<string> result = App.GetDefaultSuggestionProcessNames(
            Defaults, ["zoom"], []);

        Assert.Equal(new HashSet<string> { "vlc", "powerpnt" }, result);
        Assert.DoesNotContain("zoom", result);
    }

    [Fact]
    public void Dismissal_survives_restart()
    {
        // A dismissal from a previous session is loaded at startup and still excluded.
        IReadOnlySet<string> afterRestart = App.GetDefaultSuggestionProcessNames(
            Defaults, [], ["vlc", "zoom"]);

        Assert.DoesNotContain("vlc", afterRestart);
        Assert.DoesNotContain("zoom", afterRestart);
        Assert.Contains("powerpnt", afterRestart);
    }

    [Fact]
    public void Approved_rule_still_offered_when_not_dismissed()
    {
        IReadOnlySet<string> result = App.GetDefaultSuggestionProcessNames(
            Defaults, [], []);

        Assert.Contains("zoom", result);
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        IReadOnlySet<string> result = App.GetDefaultSuggestionProcessNames(
            Defaults, [], ["VLC"]);

        Assert.DoesNotContain("vlc", result);
        Assert.Contains("zoom", result);
    }

    [Fact]
    public void It_rejects_missing_inputs()
    {
        Assert.Throws<ArgumentNullException>(() =>
            App.GetDefaultSuggestionProcessNames(null!, [], []));
        Assert.Throws<ArgumentNullException>(() =>
            App.GetDefaultSuggestionProcessNames(Defaults, null!, []));
        Assert.Throws<ArgumentNullException>(() =>
            App.GetDefaultSuggestionProcessNames(Defaults, [], null!));
    }
}
