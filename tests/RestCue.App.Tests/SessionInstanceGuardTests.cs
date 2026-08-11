using RestCue.App.Lifecycle;
using Xunit;

namespace RestCue.App.Tests;

public sealed class SessionInstanceGuardTests
{
    [Fact]
    public void SameName_OnlyFirstAcquireIsPrimary()
    {
        string name = $"RestCueGuardTest_{Guid.NewGuid():N}";
        using var first = SessionInstanceGuard.Acquire(name);
        using var second = SessionInstanceGuard.Acquire(name);

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);
    }

    [Fact]
    public void DifferentName_EachAcquireIsPrimary()
    {
        using var first = SessionInstanceGuard.Acquire($"RestCueGuardTest_{Guid.NewGuid():N}");
        using var second = SessionInstanceGuard.Acquire($"RestCueGuardTest_{Guid.NewGuid():N}");

        Assert.True(first.IsPrimary);
        Assert.True(second.IsPrimary);
    }

    [Fact]
    public void AfterPrimaryReleased_SameNameAcquiresAgain()
    {
        string name = $"RestCueGuardTest_{Guid.NewGuid():N}";

        using (var first = SessionInstanceGuard.Acquire(name))
        {
            Assert.True(first.IsPrimary);
        }

        using var second = SessionInstanceGuard.Acquire(name);
        Assert.True(second.IsPrimary);
    }

    [Fact]
    public void Acquire_InvalidName_ThrowsInsteadOfReportingDuplicate()
    {
        Assert.ThrowsAny<Exception>(() => SessionInstanceGuard.Acquire(@"Local\Bad\Name"));
    }
}
