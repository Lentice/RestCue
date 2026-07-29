namespace RestCue.Core.Reminders;

public enum BreakGuideCue
{
    Start,
    Middle,
    End
}

public static class BreakGuideText
{
    public static string ForCue(BreakGuideCue cue) => cue switch
    {
        BreakGuideCue.Start => "看向約六公尺外",
        BreakGuideCue.Middle => "慢慢眨眼、放鬆肩膀",
        BreakGuideCue.End => "休息結束",
        _ => throw new ArgumentOutOfRangeException(nameof(cue))
    };
}
