using RestCue.App.Lifecycle;
using Xunit;

namespace RestCue.App.Tests;

public sealed class ApplicationStartupFailureHandlerTests
{
    [Fact]
    public void Handle_logs_non_sensitive_message_and_shuts_down()
    {
        string? logMessage = null;
        bool shutdownCalled = false;

        ApplicationStartupFailureHandler.Handle(
            new IOException(@"Sensitive path C:\Users\someone\secret.db"),
            message => logMessage = message,
            () => shutdownCalled = true);

        Assert.Equal("RestCue could not initialize local settings and will exit.", logMessage);
        Assert.DoesNotContain("Sensitive", logMessage);
        Assert.True(shutdownCalled);
    }
}
