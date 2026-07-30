using RestCue.Core.Policies;
using Xunit;

namespace RestCue.Core.Tests.Policies;

public sealed class ReminderTimingPolicyTests
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PassiveBreakThreshold = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan NaturalPauseThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(30);

    [Fact]
    public void EvaluatePendingReminder_returns_ShowReminder_when_natural_pause_reached()
    {
        var p = new TimingParameters(
            IdleDuration: TimeSpan.FromSeconds(6),
            ElapsedInPhase: TimeSpan.Zero,
            NaturalPauseThreshold: NaturalPauseThreshold,
            MaximumReminderWait: MaxWait,
            PassiveBreakThreshold: PassiveBreakThreshold,
            IdleThreshold: IdleThreshold,
            ReminderDisplayDuration: DisplayDuration,
            IsPaused: false, IsFocused: false, IsFullscreen: false, IsMuted: false);

        var result = ReminderTimingPolicy.EvaluatePendingReminder(p);
        Assert.Equal(TimingDecision.ShowReminder, result);
    }

    [Fact]
    public void EvaluatePendingReminder_returns_ShowReminderMaxWait_when_max_wait_elapsed()
    {
        var p = new TimingParameters(
            IdleDuration: TimeSpan.Zero,
            ElapsedInPhase: TimeSpan.FromMinutes(4),
            NaturalPauseThreshold: NaturalPauseThreshold,
            MaximumReminderWait: MaxWait,
            PassiveBreakThreshold: PassiveBreakThreshold,
            IdleThreshold: IdleThreshold,
            ReminderDisplayDuration: DisplayDuration,
            IsPaused: false, IsFocused: false, IsFullscreen: false, IsMuted: false);

        var result = ReminderTimingPolicy.EvaluatePendingReminder(p);
        Assert.Equal(TimingDecision.ShowReminderMaxWait, result);
    }

    [Fact]
    public void EvaluatePendingReminder_returns_PassivePauseDetected_when_passive_pause_reached()
    {
        var p = new TimingParameters(
            IdleDuration: TimeSpan.FromSeconds(21),
            ElapsedInPhase: TimeSpan.Zero,
            NaturalPauseThreshold: NaturalPauseThreshold,
            MaximumReminderWait: MaxWait,
            PassiveBreakThreshold: PassiveBreakThreshold,
            IdleThreshold: IdleThreshold,
            ReminderDisplayDuration: DisplayDuration,
            IsPaused: false, IsFocused: false, IsFullscreen: false, IsMuted: false);

        var result = ReminderTimingPolicy.EvaluatePendingReminder(p);
        Assert.Equal(TimingDecision.PassivePauseDetected, result);
    }

    [Fact]
    public void EvaluatePendingReminder_returns_EnterIdle_when_idle_threshold_reached()
    {
        var p = new TimingParameters(
            IdleDuration: TimeSpan.FromMinutes(3),
            ElapsedInPhase: TimeSpan.Zero,
            NaturalPauseThreshold: NaturalPauseThreshold,
            MaximumReminderWait: MaxWait,
            PassiveBreakThreshold: PassiveBreakThreshold,
            IdleThreshold: IdleThreshold,
            ReminderDisplayDuration: DisplayDuration,
            IsPaused: false, IsFocused: false, IsFullscreen: false, IsMuted: false);

        var result = ReminderTimingPolicy.EvaluatePendingReminder(p);
        Assert.Equal(TimingDecision.EnterIdle, result);
    }

    [Fact]
    public void EvaluatePendingReminder_returns_None_when_no_condition_met()
    {
        var p = new TimingParameters(
            IdleDuration: TimeSpan.FromSeconds(3),
            ElapsedInPhase: TimeSpan.FromMinutes(1),
            NaturalPauseThreshold: NaturalPauseThreshold,
            MaximumReminderWait: MaxWait,
            PassiveBreakThreshold: PassiveBreakThreshold,
            IdleThreshold: IdleThreshold,
            ReminderDisplayDuration: DisplayDuration,
            IsPaused: false, IsFocused: false, IsFullscreen: false, IsMuted: false);

        var result = ReminderTimingPolicy.EvaluatePendingReminder(p);
        Assert.Equal(TimingDecision.None, result);
    }

    [Fact]
    public void EvaluateReminderVisible_returns_AutoDismiss_when_display_duration_elapsed()
    {
        var p = new TimingParameters(
            IdleDuration: TimeSpan.Zero,
            ElapsedInPhase: TimeSpan.FromSeconds(31),
            NaturalPauseThreshold: NaturalPauseThreshold,
            MaximumReminderWait: MaxWait,
            PassiveBreakThreshold: PassiveBreakThreshold,
            IdleThreshold: IdleThreshold,
            ReminderDisplayDuration: DisplayDuration,
            IsPaused: false, IsFocused: false, IsFullscreen: false, IsMuted: false);

        var result = ReminderTimingPolicy.EvaluateReminderVisible(p);
        Assert.Equal(TimingDecision.AutoDismiss, result);
    }

    [Fact]
    public void EvaluateReminderVisible_returns_EnterIdle_when_idle_during_reminder()
    {
        var p = new TimingParameters(
            IdleDuration: TimeSpan.FromMinutes(3),
            ElapsedInPhase: TimeSpan.FromSeconds(5),
            NaturalPauseThreshold: NaturalPauseThreshold,
            MaximumReminderWait: MaxWait,
            PassiveBreakThreshold: PassiveBreakThreshold,
            IdleThreshold: IdleThreshold,
            ReminderDisplayDuration: DisplayDuration,
            IsPaused: false, IsFocused: false, IsFullscreen: false, IsMuted: false);

        var result = ReminderTimingPolicy.EvaluateReminderVisible(p);
        Assert.Equal(TimingDecision.EnterIdle, result);
    }

    [Fact]
    public void EvaluateReminderVisible_returns_PassivePauseDetected_when_passive_pause_during_reminder()
    {
        var p = new TimingParameters(
            IdleDuration: TimeSpan.FromSeconds(21),
            ElapsedInPhase: TimeSpan.FromSeconds(5),
            NaturalPauseThreshold: NaturalPauseThreshold,
            MaximumReminderWait: MaxWait,
            PassiveBreakThreshold: PassiveBreakThreshold,
            IdleThreshold: IdleThreshold,
            ReminderDisplayDuration: DisplayDuration,
            IsPaused: false, IsFocused: false, IsFullscreen: false, IsMuted: false);

        var result = ReminderTimingPolicy.EvaluateReminderVisible(p);
        Assert.Equal(TimingDecision.PassivePauseDetected, result);
    }

    [Fact]
    public void IsNaturalPause_returns_true_when_idle_duration_meets_threshold()
    {
        Assert.True(ReminderTimingPolicy.IsNaturalPause(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(5)));
        Assert.False(ReminderTimingPolicy.IsNaturalPause(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void IsIdle_returns_true_when_idle_duration_meets_threshold()
    {
        Assert.True(ReminderTimingPolicy.IsIdle(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(2)));
        Assert.False(ReminderTimingPolicy.IsIdle(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void IsPassivePause_returns_true_when_passive_break_threshold_met()
    {
        Assert.True(ReminderTimingPolicy.IsPassivePause(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(20)));
        Assert.False(ReminderTimingPolicy.IsPassivePause(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)));
    }
}
