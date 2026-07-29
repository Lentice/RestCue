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

    public AppSettings CurrentSettings { get; internal set; } = AppSettings.Default;

    public async Task<SettingsLoadResult> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        SettingsLoadResult result = await settingsRepository.LoadAsync(cancellationToken);
        CurrentSettings = result.Settings;
        lifecycle.Start();
        return result;
    }
}
