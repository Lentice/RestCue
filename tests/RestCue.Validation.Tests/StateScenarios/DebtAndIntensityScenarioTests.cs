using RestCue.Core.Activity;
using RestCue.Core.Domain;
using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.Validation.Tests.StateScenarios;

public sealed class DebtAndIntensityScenarioTests
{
    private static readonly TimeSpan Level0 = TimeSpan.FromMinutes(25);
    private static readonly TimeSpan Level1 = TimeSpan.FromMinutes(25);
    private static readonly TimeSpan Level2 = TimeSpan.FromMinutes(35);
    private static readonly TimeSpan Level3 = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan Level4 = TimeSpan.FromMinutes(60);

    [Theory]
    [InlineData(0, RestDebtLevel.Level0)]
    [InlineData(24, RestDebtLevel.Level0)]
    [InlineData(25, RestDebtLevel.Level1)]
    [InlineData(34, RestDebtLevel.Level1)]
    [InlineData(35, RestDebtLevel.Level2)]
    [InlineData(44, RestDebtLevel.Level2)]
    [InlineData(45, RestDebtLevel.Level3)]
    [InlineData(59, RestDebtLevel.Level3)]
    [InlineData(60, RestDebtLevel.Level4)]
    [InlineData(120, RestDebtLevel.Level4)]
    public void DebtPolicy_evaluates_all_levels(int accumulatedMinutes, RestDebtLevel expected)
    {
        var result = DebtPolicy.Evaluate(
            TimeSpan.FromMinutes(accumulatedMinutes), Level0, Level2, Level3, Level4);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(RestDebtLevel.Level0, PresentationIntensity.TrayOnly)]
    [InlineData(RestDebtLevel.Level1, PresentationIntensity.TrayOnly)]
    [InlineData(RestDebtLevel.Level2, PresentationIntensity.TrayOnly)]
    [InlineData(RestDebtLevel.Level3, PresentationIntensity.EdgePopup)]
    [InlineData(RestDebtLevel.Level4, PresentationIntensity.PopupAndSound)]
    public void Debt_recommendation_matches_level(RestDebtLevel level, PresentationIntensity expected)
    {
        var result = PresentationIntensityPolicy.GetDebtRecommendation(level);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(FullscreenState.Confirmed, PresentationIntensity.LightTouch)]
    [InlineData(FullscreenState.Uncertain, PresentationIntensity.LightTouch)]
    [InlineData(FullscreenState.NotFullscreen, PresentationIntensity.PopupAndSound)]
    public void Fullscreen_state_maps_to_correct_cap(FullscreenState state, PresentationIntensity expected)
    {
        var result = PresentationIntensityPolicy.FromFullscreenState(state);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(ApplicationRuleType.Normal, PresentationIntensity.PopupAndSound)]
    [InlineData(ApplicationRuleType.TrayOnly, PresentationIntensity.LightTouch)]
    [InlineData(ApplicationRuleType.Silent, PresentationIntensity.None)]
    [InlineData(ApplicationRuleType.CustomInterval, PresentationIntensity.PopupAndSound)]
    public void Application_rule_maps_to_correct_cap(ApplicationRuleType ruleType, PresentationIntensity expected)
    {
        var result = PresentationIntensityPolicy.FromApplicationRuleType(ruleType);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Effective_intensity_takes_minimum_of_all_caps()
    {
        var result = PresentationIntensityPolicy.Effective(
            PresentationIntensity.PopupAndSound,
            PresentationIntensity.TrayOnly,
            PresentationIntensity.PopupAndSound);

        Assert.Equal(PresentationIntensity.TrayOnly, result);
    }

    [Fact]
    public void Silent_rule_overrides_everything()
    {
        var result = PresentationIntensityPolicy.Effective(
            PresentationIntensity.PopupAndSound,
            PresentationIntensity.PopupAndSound,
            PresentationIntensity.None);

        Assert.Equal(PresentationIntensity.None, result);
    }
}
