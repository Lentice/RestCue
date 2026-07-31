namespace RestCue.Core.Activity;

public readonly record struct ForegroundContext
{
    private ForegroundContext(string? processName, FullscreenState fullscreenState)
    {
        ProcessName = processName;
        FullscreenState = fullscreenState;
    }

    public string? ProcessName { get; }

    public FullscreenState FullscreenState { get; }

    public bool IsFullscreen => FullscreenState == FullscreenState.Confirmed;

    public static ForegroundContext Default { get; } = new(null, FullscreenState.NotFullscreen);

    public static ForegroundContext Create(string? processName, FullscreenState fullscreenState) =>
        new(processName, fullscreenState);
}
