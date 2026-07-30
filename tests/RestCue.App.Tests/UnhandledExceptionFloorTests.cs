using Xunit;

namespace RestCue.App.Tests;

/// <summary>
/// The application's floor: an unexpected failure is recorded and survived, never fatal.
/// </summary>
public sealed class UnhandledExceptionFloorTests
{
    [Fact]
    public void An_unhandled_exception_is_recorded_and_survived()
    {
        var errors = new List<string>();
        int diagnostics = 0;

        App.HandleUnhandledException(
            new InvalidOperationException("phase moved on"),
            errors.Add,
            () => diagnostics++);

        Assert.Single(errors);
        Assert.Contains("phase moved on", errors[0]);
        Assert.Equal(1, diagnostics);
    }

    [Fact]
    public void It_does_not_swallow_silently()
    {
        var errors = new List<string>();

        App.HandleUnhandledException(new Exception("boom"), errors.Add, () => { });

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void A_failure_while_recording_does_not_escape()
    {
        var errors = new List<string>();

        // Recording must never be the thing that takes the application down.
        App.HandleUnhandledException(
            new Exception("boom"),
            errors.Add,
            () => throw new InvalidOperationException("the writer is gone"));

        Assert.Equal(2, errors.Count);
        Assert.Contains("the writer is gone", errors[1]);
    }

    [Fact]
    public void It_rejects_missing_collaborators()
    {
        Assert.Throws<ArgumentNullException>(() =>
            App.HandleUnhandledException(null!, _ => { }, () => { }));
        Assert.Throws<ArgumentNullException>(() =>
            App.HandleUnhandledException(new Exception(), null!, () => { }));
        Assert.Throws<ArgumentNullException>(() =>
            App.HandleUnhandledException(new Exception(), _ => { }, null!));
    }
}
