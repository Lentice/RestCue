namespace RestCue.Core.Time;

/// <summary>
/// The two notions of time the app needs, deliberately kept apart (ADR-0008).
/// </summary>
public interface IClock
{
    /// <summary>
    /// Civil time. Use only where an actual point on the calendar is required — a
    /// usage-event timestamp that has to bucket into the right day, for example.
    /// It is not monotonic: it steps when the user edits the system time, when a
    /// large time-synchronisation correction lands, and when a virtual machine or
    /// laptop resumes from a suspended state.
    /// </summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Monotonic elapsed time since an arbitrary origin. Only differences between
    /// two readings are meaningful; the absolute value means nothing. Use this for
    /// every "how long has this been going?" question — break completion, pause
    /// expiry, Focus Mode expiry, work-time accumulation — so that none of them can
    /// be moved by a clock step.
    /// </summary>
    TimeSpan Elapsed { get; }
}
