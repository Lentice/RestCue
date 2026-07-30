namespace RestCue.App;

/// <summary>
/// The dedicated global shortcut for cancelling a running break.
/// </summary>
/// <remarks>
/// The combination deliberately avoids anything Windows reserves. The previous choice was
/// <c>Ctrl+Shift+Esc</c> — the Task Manager shortcut — so registration was expected to
/// fail, and did so silently.
/// </remarks>
internal static class CancelBreakShortcut
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;

    /// <summary>Virtual-key code for 'B'.</summary>
    public const uint VirtualKey = 0x42;

    public const uint Modifiers = ModControl | ModAlt | ModShift;

    /// <summary>Human-readable form, used in the diagnostic so a clash is discoverable.</summary>
    public const string Description = "Ctrl+Alt+Shift+B";

    /// <summary>
    /// Attempts registration and records a diagnostic naming the combination when it
    /// fails, so a clash with another application is discoverable rather than silent.
    /// </summary>
    public static bool TryRegister(Func<bool> register, Action<string> logDiagnostic)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(logDiagnostic);

        if (register())
            return true;

        logDiagnostic(
            $"RestCue: could not register the global break-cancel shortcut ({Description}). " +
            "Another application may already hold it; cancelling a break by clicking still works.");
        return false;
    }
}
