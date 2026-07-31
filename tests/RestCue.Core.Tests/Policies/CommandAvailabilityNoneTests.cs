using System.Reflection;
using RestCue.Core.Policies;
using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.Core.Tests.Policies;

/// <summary>
/// <see cref="CommandAvailabilityPolicy.None"/> is what a surface shows before there is a
/// work cycle to reason about. It lives beside <see cref="CommandAvailabilityPolicy.ForPhase"/>
/// so that "nothing is available yet" cannot become a second table of what is available.
/// </summary>
public sealed class CommandAvailabilityNoneTests
{
    /// <summary>
    /// Asserted by reflection rather than property by property: a field added to
    /// <see cref="CommandAvailability"/> later must be false here too, and a hand-written
    /// list would silently miss it.
    /// </summary>
    [Fact]
    public void Nothing_is_available_and_no_toggle_is_reversed()
    {
        CommandAvailability none = CommandAvailabilityPolicy.None;

        PropertyInfo[] flags = typeof(CommandAvailability)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool))
            .ToArray();

        Assert.NotEmpty(flags);
        foreach (PropertyInfo flag in flags)
        {
            Assert.False((bool)flag.GetValue(none)!, $"{flag.Name} should be false before initialisation.");
        }
    }

    /// <summary>
    /// It must not coincide with any real phase, or a surface stuck in the uninitialised
    /// state would be indistinguishable from one legitimately showing that phase.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPhases))]
    public void It_matches_no_real_phase(WorkCyclePhase phase)
    {
        Assert.NotEqual(CommandAvailabilityPolicy.ForPhase(phase), CommandAvailabilityPolicy.None);
    }

    public static TheoryData<WorkCyclePhase> AllPhases()
    {
        var data = new TheoryData<WorkCyclePhase>();
        foreach (WorkCyclePhase phase in Enum.GetValues<WorkCyclePhase>())
        {
            data.Add(phase);
        }
        return data;
    }
}
