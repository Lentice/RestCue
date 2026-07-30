using RestCue.Core.Activity;
using RestCue.Core.Domain;
using RestCue.Core.Reminders;

namespace RestCue.Core.Policies;

public static class PresentationIntensityPolicy
{
    public static PresentationIntensity DefaultUserCap => PresentationIntensity.PopupAndSound;
    public static PresentationIntensity DefaultContextCap => PresentationIntensity.PopupAndSound;
    public static PresentationIntensity FullscreenCap => PresentationIntensity.LightTouch;
    public static PresentationIntensity TrayOnlyCap => PresentationIntensity.LightTouch;
    public static PresentationIntensity SilentCap => PresentationIntensity.None;

    public static PresentationIntensity GetDebtRecommendation(RestDebtLevel level)
    {
        return level switch
        {
            RestDebtLevel.Level0 => PresentationIntensity.TrayOnly,
            RestDebtLevel.Level1 => PresentationIntensity.TrayOnly,
            RestDebtLevel.Level2 => PresentationIntensity.TrayOnly,
            RestDebtLevel.Level3 => PresentationIntensity.EdgePopup,
            RestDebtLevel.Level4 => PresentationIntensity.PopupAndSound,
            _ => PresentationIntensity.TrayOnly
        };
    }

    public static PresentationIntensity FromFullscreenState(FullscreenState state)
    {
        return state switch
        {
            FullscreenState.Confirmed => FullscreenCap,
            FullscreenState.Uncertain => FullscreenCap,
            FullscreenState.NotFullscreen => DefaultContextCap,
            _ => FullscreenCap
        };
    }

    public static PresentationIntensity FromApplicationRuleType(ApplicationRuleType ruleType)
    {
        return ruleType switch
        {
            ApplicationRuleType.Normal => DefaultContextCap,
            ApplicationRuleType.TrayOnly => TrayOnlyCap,
            ApplicationRuleType.Silent => SilentCap,
            ApplicationRuleType.CustomInterval => DefaultContextCap,
            _ => DefaultContextCap
        };
    }

    public static PresentationIntensity Effective(
        PresentationIntensity debtRecommendation,
        PresentationIntensity contextCap,
        PresentationIntensity userCap)
    {
        var a = (int)debtRecommendation;
        var b = (int)contextCap;
        var c = (int)userCap;
        var min = a < b ? (a < c ? a : c) : (b < c ? b : c);
        return (PresentationIntensity)Math.Clamp(min, (int)PresentationIntensity.None, (int)PresentationIntensity.PopupAndSound);
    }
}
