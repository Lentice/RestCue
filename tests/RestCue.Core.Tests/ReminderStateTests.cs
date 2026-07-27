using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.Core.Tests;

public sealed class ReminderStateTests
{
    [Fact]
    public void Initial_working_state_is_available()
    {
        Assert.Equal("Working", ReminderState.Working.ToString());
    }
}
