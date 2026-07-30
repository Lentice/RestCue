using RestCue.Core.Settings;

namespace RestCue.App.Lifecycle;

public sealed class ApplicationStartup
{
    private readonly ISettingsRepository settingsRepository;
    private readonly IApplicationLifecycle lifecycle;

    public ApplicationStartup(
        ISettingsRepository settingsRepository,
        IApplicationLifecycle lifecycle)
    {
        this.settingsRepository = settingsRepository;
        this.lifecycle = lifecycle;
    }

    public const string EngineFallbackDiagnostic =
        "RestCue could not build the reminder engine from the stored settings and has fallen back to defaults.";

    public AppSettings CurrentSettings { get; internal set; } = AppSettings.Default;

    /// <summary>
    /// True when <see cref="ResolveEngineSettings"/> degraded <see cref="CurrentSettings"/>
    /// to defaults.
    /// </summary>
    public bool FellBackToDefaultSettings { get; private set; }

    public async Task<SettingsLoadResult> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        SettingsLoadResult result = await settingsRepository.LoadAsync(cancellationToken);
        CurrentSettings = result.Settings;
        lifecycle.Start();
        return result;
    }

    /// <summary>
    /// Confirms the loaded settings can actually drive the reminder engine, and degrades
    /// to defaults if they cannot.
    /// </summary>
    /// <remarks>
    /// The repository's own recovery path already handles corrupt documents and values
    /// that fail validation. This covers the remaining case — a value that validation
    /// accepts but that a downstream constructor guard still refuses — which would
    /// otherwise fail identically on every launch, because the offending value is
    /// already persisted and looks legal. It is defence in depth, not the primary
    /// mechanism, so it never suppresses the failure: the diagnostic always records why.
    /// </remarks>
    public AppSettings ResolveEngineSettings(
        Action<AppSettings> constructEngine,
        Action<string> logDiagnostic)
    {
        ArgumentNullException.ThrowIfNull(constructEngine);
        ArgumentNullException.ThrowIfNull(logDiagnostic);

        try
        {
            constructEngine(CurrentSettings);
            FellBackToDefaultSettings = false;
        }
        catch (Exception exception)
        {
            logDiagnostic($"{EngineFallbackDiagnostic} {exception.Message}");
            CurrentSettings = AppSettings.Default;
            FellBackToDefaultSettings = true;
        }

        return CurrentSettings;
    }
}
