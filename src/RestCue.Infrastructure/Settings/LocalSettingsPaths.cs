namespace RestCue.Infrastructure.Settings;

public static class LocalSettingsPaths
{
    public static string SettingsFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RestCue",
            "settings.json");
}
