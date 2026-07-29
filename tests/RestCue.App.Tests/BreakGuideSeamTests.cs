using RestCue.Core.Reminders;
using Xunit;

namespace RestCue.App.Tests;

public sealed class BreakGuideSeamTests
{
    [Fact]
    public void BreakGuideText_contains_no_digits_for_all_cues()
    {
        foreach (BreakGuideCue cue in Enum.GetValues<BreakGuideCue>())
        {
            string text = BreakGuideText.ForCue(cue);
            Assert.DoesNotContain(text, c => char.IsDigit(c));
        }
    }
}
