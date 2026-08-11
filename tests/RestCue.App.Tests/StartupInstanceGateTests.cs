using RestCue.App.Lifecycle;
using Xunit;

namespace RestCue.App.Tests;

public sealed class StartupInstanceGateTests
{
    [Fact]
    public void PrimaryInstance_Proceeds_WithoutWarningOrShutdown()
    {
        string name = $"RestCueGateTest_{Guid.NewGuid():N}";
        bool warningShown = false;
        bool shutdownCalled = false;

        bool proceed = App.StartAsPrimaryInstance(
            () => SessionInstanceGuard.Acquire(name),
            () => warningShown = true,
            _ => { },
            () => shutdownCalled = true,
            out var acquired);

        Assert.True(proceed);
        Assert.True(acquired!.IsPrimary);
        Assert.False(warningShown);
        Assert.False(shutdownCalled);
        acquired.Dispose();
    }

    [Fact]
    public void DuplicateInstance_ShowsWarning_AndSkipsNormalStartup()
    {
        string name = $"RestCueGateTest_{Guid.NewGuid():N}";
        using var primary = SessionInstanceGuard.Acquire(name);
        bool warningShown = false;
        bool shutdownCalled = false;

        bool proceed = App.StartAsPrimaryInstance(
            () => SessionInstanceGuard.Acquire(name),
            () => warningShown = true,
            _ => { },
            () => shutdownCalled = true,
            out var acquired);

        Assert.False(proceed);
        Assert.True(warningShown);
        Assert.False(shutdownCalled);
        Assert.Null(acquired);
    }

    [Fact]
    public void UnexpectedError_LogsAndShutsDown_WithoutDuplicateWarning()
    {
        bool warningShown = false;
        bool shutdownCalled = false;
        string? logged = null;

        bool proceed = App.StartAsPrimaryInstance(
            () => throw new InvalidOperationException("mutex exploded"),
            () => warningShown = true,
            message => logged = message,
            () => shutdownCalled = true,
            out var acquired);

        Assert.False(proceed);
        Assert.False(warningShown);
        Assert.True(shutdownCalled);
        Assert.NotNull(logged);
        Assert.Null(acquired);
    }
}
