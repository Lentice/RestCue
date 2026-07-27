namespace RestCue.App.Lifecycle;

public static class ApplicationStartupFailureHandler
{
    public const string SafeFailureMessage =
        "RestCue could not initialize local settings and will exit.";

    public static void Handle(
        Exception exception,
        Action<string> logError,
        Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(logError);
        ArgumentNullException.ThrowIfNull(shutdown);

        logError(SafeFailureMessage);
        shutdown();
    }
}
