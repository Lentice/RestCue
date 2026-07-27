namespace RestCue.Infrastructure.Settings;

public static class LocalSettingsPaths
{
    public static string DatabaseFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RestCue",
            "restcue.db");
}
