namespace RestCue.Core.Reminders;

public sealed class SuggestionEventArgs : EventArgs
{
    public string ProcessName { get; }

    public SuggestionEventArgs(string processName)
    {
        ProcessName = processName;
    }
}
