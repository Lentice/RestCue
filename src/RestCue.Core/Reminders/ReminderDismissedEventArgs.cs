namespace RestCue.Core.Reminders;

public sealed class ReminderDismissedEventArgs : EventArgs
{
    public ReminderResult Result { get; }
    public ReminderDismissedEventArgs(ReminderResult result) => Result = result;
}
