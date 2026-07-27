using RestCue.Core.Time;

namespace RestCue.Core.Reminders;

public sealed class WorkCycleTracker
{
    private readonly IClock clock;
    private readonly TimeSpan idleThreshold;
    private readonly TimeSpan naturalPauseThreshold;
    private readonly TimeSpan maximumReminderWait;
    private readonly TimeSpan breakDuration;
    private readonly TimeSpan passiveBreakThreshold;

    private DateTimeOffset? pendingSinceUtc;
    private DateTimeOffset? breakStartUtc;
    private DateTimeOffset? lastTickUtc;
    private bool wasWorking;

    private readonly TimeSpan workInterval;

    public WorkCycleTracker(
        IClock clock,
        TimeSpan workInterval,
        TimeSpan idleThreshold,
        TimeSpan naturalPauseThreshold,
        TimeSpan maximumReminderWait,
        TimeSpan breakDuration,
        TimeSpan passiveBreakThreshold)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ValidateThreshold(workInterval, nameof(workInterval));
        ValidateThreshold(idleThreshold, nameof(idleThreshold));
        ValidateThreshold(naturalPauseThreshold, nameof(naturalPauseThreshold));
        ValidateThreshold(maximumReminderWait, nameof(maximumReminderWait));
        ValidateThreshold(breakDuration, nameof(breakDuration));
        ValidateThreshold(passiveBreakThreshold, nameof(passiveBreakThreshold));

        this.clock = clock;
        this.workInterval = workInterval;
        this.idleThreshold = idleThreshold;
        this.naturalPauseThreshold = naturalPauseThreshold;
        this.maximumReminderWait = maximumReminderWait;
        this.breakDuration = breakDuration;
        this.passiveBreakThreshold = passiveBreakThreshold;
    }

    public WorkCyclePhase CurrentPhase { get; private set; } = WorkCyclePhase.Working;

    public TimeSpan AccumulatedWorkTime { get; private set; }

    public TimeSpan BreakDuration => breakDuration;

    public event EventHandler? ReminderShown;
    public event EventHandler? BreakCompleted;
    public event EventHandler? PassiveBreakCompleted;

    public void Tick(TimeSpan idleDuration)
    {
        if (idleDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleDuration), "Idle duration cannot be negative.");

        var now = clock.UtcNow;
        bool isWorking = idleDuration < idleThreshold;

        switch (CurrentPhase)
        {
            case WorkCyclePhase.Working:
                TickWorking(now, isWorking);
                break;

            case WorkCyclePhase.PendingReminder:
                TickPending(now, isWorking, idleDuration);
                break;

            case WorkCyclePhase.ReminderVisible:
                TickReminderVisible(now, idleDuration);
                break;

            case WorkCyclePhase.BreakInProgress:
                TickBreak(now);
                break;
        }
    }

    public void TickActivityUnavailable()
    {
        var now = clock.UtcNow;

        switch (CurrentPhase)
        {
            case WorkCyclePhase.Working:
                wasWorking = false;
                lastTickUtc = now;
                break;

            case WorkCyclePhase.PendingReminder:
                if (now - pendingSinceUtc!.Value >= maximumReminderWait)
                {
                    CurrentPhase = WorkCyclePhase.ReminderVisible;
                    ReminderShown?.Invoke(this, EventArgs.Empty);
                }
                break;

            case WorkCyclePhase.BreakInProgress:
                if (now - breakStartUtc!.Value >= breakDuration)
                {
                    ResetCycle();
                    BreakCompleted?.Invoke(this, EventArgs.Empty);
                }
                break;
        }
    }

    public void StartBreak()
    {
        if (CurrentPhase != WorkCyclePhase.ReminderVisible)
            throw new InvalidOperationException(
                $"Cannot start break from phase {CurrentPhase}.");

        CurrentPhase = WorkCyclePhase.BreakInProgress;
        breakStartUtc = clock.UtcNow;
    }

    private void TickWorking(DateTimeOffset now, bool isWorking)
    {
        if (isWorking)
        {
            if (wasWorking && lastTickUtc.HasValue)
            {
                var delta = now - lastTickUtc.Value;
                if (delta > TimeSpan.Zero)
                    AccumulatedWorkTime += delta;
            }

            if (workInterval > TimeSpan.Zero && AccumulatedWorkTime >= workInterval)
            {
                CurrentPhase = WorkCyclePhase.PendingReminder;
                pendingSinceUtc = now;
                AccumulatedWorkTime = TimeSpan.Zero;
            }
        }

        wasWorking = isWorking;
        lastTickUtc = now;
    }

    private void TickPending(DateTimeOffset now, bool isWorking, TimeSpan idleDuration)
    {
        var elapsed = now - pendingSinceUtc!.Value;

        if (idleDuration >= passiveBreakThreshold)
        {
            ResetCycle();
            PassiveBreakCompleted?.Invoke(this, EventArgs.Empty);
        }
        else if (idleDuration >= naturalPauseThreshold)
        {
            CurrentPhase = WorkCyclePhase.ReminderVisible;
            ReminderShown?.Invoke(this, EventArgs.Empty);
        }
        else if (elapsed >= maximumReminderWait)
        {
            CurrentPhase = WorkCyclePhase.ReminderVisible;
            ReminderShown?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TickReminderVisible(DateTimeOffset now, TimeSpan idleDuration)
    {
        if (idleDuration >= passiveBreakThreshold)
        {
            ResetCycle();
            PassiveBreakCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TickBreak(DateTimeOffset now)
    {
        if (now - breakStartUtc!.Value >= breakDuration)
        {
            ResetCycle();
            BreakCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ResetCycle()
    {
        CurrentPhase = WorkCyclePhase.Working;
        AccumulatedWorkTime = TimeSpan.Zero;
        pendingSinceUtc = null;
        breakStartUtc = null;
        lastTickUtc = null;
        wasWorking = false;
    }

    private static void ValidateThreshold(TimeSpan value, string paramName)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                paramName, value, "Threshold must be positive.");
    }
}
