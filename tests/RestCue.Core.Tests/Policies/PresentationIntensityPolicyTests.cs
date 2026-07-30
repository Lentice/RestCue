using RestCue.Core.Activity;
using RestCue.Core.Domain;
using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.Core.Tests.Policies;

public sealed class PresentationIntensityPolicyTests
{
    [Theory]
    [InlineData(RestDebtLevel.Level0, PresentationIntensity.TrayOnly)]
    [InlineData(RestDebtLevel.Level1, PresentationIntensity.TrayOnly)]
    [InlineData(RestDebtLevel.Level2, PresentationIntensity.TrayOnly)]
    [InlineData(RestDebtLevel.Level3, PresentationIntensity.EdgePopup)]
    [InlineData(RestDebtLevel.Level4, PresentationIntensity.PopupAndSound)]
    public void GetDebtRecommendation_returns_expected(RestDebtLevel level, PresentationIntensity expected)
    {
        Assert.Equal(expected, PresentationIntensityPolicy.GetDebtRecommendation(level));
    }

    [Fact]
    public void GetDebtRecommendation_unknown_level_falls_back_safely()
    {
        var unknown = (RestDebtLevel)999;
        Assert.Equal(PresentationIntensity.TrayOnly, PresentationIntensityPolicy.GetDebtRecommendation(unknown));
    }

    [Theory]
    [InlineData(FullscreenState.Confirmed, PresentationIntensity.LightTouch)]
    [InlineData(FullscreenState.Uncertain, PresentationIntensity.LightTouch)]
    [InlineData(FullscreenState.NotFullscreen, PresentationIntensity.PopupAndSound)]
    public void FromFullscreenState_returns_expected(FullscreenState state, PresentationIntensity expected)
    {
        Assert.Equal(expected, PresentationIntensityPolicy.FromFullscreenState(state));
    }

    [Fact]
    public void FromFullscreenState_unknown_falls_back_safely()
    {
        var unknown = (FullscreenState)999;
        Assert.Equal(PresentationIntensity.LightTouch, PresentationIntensityPolicy.FromFullscreenState(unknown));
    }

    [Theory]
    [InlineData(ApplicationRuleType.Normal, PresentationIntensity.PopupAndSound)]
    [InlineData(ApplicationRuleType.TrayOnly, PresentationIntensity.LightTouch)]
    [InlineData(ApplicationRuleType.Silent, PresentationIntensity.None)]
    [InlineData(ApplicationRuleType.CustomInterval, PresentationIntensity.PopupAndSound)]
    public void FromApplicationRuleType_returns_expected(ApplicationRuleType ruleType, PresentationIntensity expected)
    {
        Assert.Equal(expected, PresentationIntensityPolicy.FromApplicationRuleType(ruleType));
    }

    [Fact]
    public void FromApplicationRuleType_unknown_falls_back_safely()
    {
        var unknown = (ApplicationRuleType)999;
        Assert.Equal(PresentationIntensity.PopupAndSound, PresentationIntensityPolicy.FromApplicationRuleType(unknown));
    }

    [Theory]
    [InlineData(PresentationIntensity.TrayOnly, PresentationIntensity.TrayOnly, PresentationIntensity.PopupAndSound, PresentationIntensity.TrayOnly)]
    [InlineData(PresentationIntensity.EdgePopup, PresentationIntensity.TrayOnly, PresentationIntensity.PopupAndSound, PresentationIntensity.TrayOnly)]
    [InlineData(PresentationIntensity.PopupAndSound, PresentationIntensity.PopupAndSound, PresentationIntensity.PopupAndSound, PresentationIntensity.PopupAndSound)]
    [InlineData(PresentationIntensity.EdgePopup, PresentationIntensity.PopupAndSound, PresentationIntensity.PopupAndSound, PresentationIntensity.EdgePopup)]
    [InlineData(PresentationIntensity.PopupAndSound, PresentationIntensity.None, PresentationIntensity.PopupAndSound, PresentationIntensity.None)]
    [InlineData(PresentationIntensity.TrayOnly, PresentationIntensity.PopupAndSound, PresentationIntensity.None, PresentationIntensity.None)]
    public void Effective_returns_min_of_three(
        PresentationIntensity debt, PresentationIntensity context, PresentationIntensity user, PresentationIntensity expected)
    {
        Assert.Equal(expected, PresentationIntensityPolicy.Effective(debt, context, user));
    }

    [Fact]
    public void Effective_clamps_out_of_range_values()
    {
        var low = (PresentationIntensity)(-1);
        var high = (PresentationIntensity)999;
        Assert.Equal(PresentationIntensity.None, PresentationIntensityPolicy.Effective(low, low, low));
        Assert.Equal(PresentationIntensity.PopupAndSound, PresentationIntensityPolicy.Effective(high, high, high));
        Assert.Equal(PresentationIntensity.None, PresentationIntensityPolicy.Effective(PresentationIntensity.EdgePopup, high, low));
    }

    [Fact]
    public void DefaultUserCap_is_PopupAndSound()
    {
        Assert.Equal(PresentationIntensity.PopupAndSound, PresentationIntensityPolicy.DefaultUserCap);
    }

    [Fact]
    public void DefaultContextCap_is_PopupAndSound()
    {
        Assert.Equal(PresentationIntensity.PopupAndSound, PresentationIntensityPolicy.DefaultContextCap);
    }

    [Fact]
    public void FullscreenCap_is_LightTouch()
    {
        Assert.Equal(PresentationIntensity.LightTouch, PresentationIntensityPolicy.FullscreenCap);
    }

    [Fact]
    public void TrayOnlyCap_is_LightTouch()
    {
        Assert.Equal(PresentationIntensity.LightTouch, PresentationIntensityPolicy.TrayOnlyCap);
    }

    [Fact]
    public void SilentCap_is_None()
    {
        Assert.Equal(PresentationIntensity.None, PresentationIntensityPolicy.SilentCap);
    }
}
