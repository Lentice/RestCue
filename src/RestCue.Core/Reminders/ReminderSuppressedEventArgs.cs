namespace RestCue.Core.Reminders;

public sealed class ReminderSuppressedEventArgs : EventArgs
{
    public bool ShowTrayCue { get; }

    public ReminderSuppressedEventArgs(bool showTrayCue)
    {
        ShowTrayCue = showTrayCue;
    }
}
