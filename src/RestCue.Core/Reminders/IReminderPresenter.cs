namespace RestCue.Core.Reminders;

public interface IReminderPresenter
{
    void Show();
    void Hide();
    event EventHandler? BreakRequested;
}
