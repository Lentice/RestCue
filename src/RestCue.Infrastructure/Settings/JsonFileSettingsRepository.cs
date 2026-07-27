using System.Text.Json;
using RestCue.Core.Settings;

namespace RestCue.Infrastructure.Settings;

public sealed class JsonFileSettingsRepository : ISettingsRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string settingsPath;
    private readonly ISettingsValidator validator;

    public JsonFileSettingsRepository(string settingsPath, ISettingsValidator validator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        this.settingsPath = Path.GetFullPath(settingsPath);
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath))
        {
            return new(AppSettings.Default);
        }

        try
        {
            await using FileStream stream = File.OpenRead(settingsPath);
            AppSettings settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken) ?? throw new JsonException("The settings file contains no settings object.");
            EnsureValid(settings);
            return new(settings);
        }
        catch (Exception exception) when (IsCorruptSettings(exception))
        {
            string backupPath = CreateCorruptBackupPath();
            File.Copy(settingsPath, backupPath);
            await SaveAsync(AppSettings.Default, CancellationToken.None);
            return new(AppSettings.Default, RecoveredFromCorruption: true, backupPath);
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureValid(settings);

        string? directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private void EnsureValid(AppSettings settings)
    {
        IReadOnlyList<SettingsValidationError> errors = validator.Validate(settings);
        if (errors.Count > 0)
        {
            throw new SettingsValidationException(errors);
        }
    }

    private static bool IsCorruptSettings(Exception exception) =>
        exception is JsonException or NotSupportedException or SettingsValidationException;

    private string CreateCorruptBackupPath() =>
        $"{settingsPath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak";
}
