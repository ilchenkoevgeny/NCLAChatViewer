using System.Text.Json;

namespace NclaChatViewer.Services;

public sealed class SettingsService
{
    private const string SettingsFileName = "settings.json";
    private const string LegacySettingsFileName = "appsettings.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private readonly string settingsDirectory;
    private readonly string settingsPath;
    private readonly string legacySettingsPath;

    public SettingsService()
    {
        settingsDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NCLA Chat Viewer");

        settingsPath = System.IO.Path.Combine(settingsDirectory, SettingsFileName);
        legacySettingsPath = System.IO.Path.Combine(AppContext.BaseDirectory, LegacySettingsFileName);
    }

    public string SettingsPath => settingsPath;

    public AppSettings Load()
    {
        AppSettings settings = LoadFromUserProfile()
            ?? LoadFromLegacyAppSettings()
            ?? new AppSettings();

        EnsureUserProfileSettingsFile(settings);

        return settings;
    }

    public void Save(AppSettings settings)
    {
        System.IO.Directory.CreateDirectory(settingsDirectory);

        string json = JsonSerializer.Serialize(settings, Options);
        System.IO.File.WriteAllText(settingsPath, json);
    }

    private AppSettings? LoadFromUserProfile()
    {
        if (!System.IO.File.Exists(settingsPath))
        {
            return null;
        }

        try
        {
            string json = System.IO.File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options);
        }
        catch
        {
            TryBackupBrokenSettingsFile();
            return null;
        }
    }

    private AppSettings? LoadFromLegacyAppSettings()
    {
        if (!System.IO.File.Exists(legacySettingsPath))
        {
            return null;
        }

        try
        {
            string json = System.IO.File.ReadAllText(legacySettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    private void EnsureUserProfileSettingsFile(AppSettings settings)
    {
        if (System.IO.File.Exists(settingsPath))
        {
            return;
        }

        Save(settings);
    }

    private void TryBackupBrokenSettingsFile()
    {
        try
        {
            if (!System.IO.File.Exists(settingsPath))
            {
                return;
            }

            string backupPath = System.IO.Path.Combine(
                settingsDirectory,
                $"settings.broken.{DateTime.Now:yyyyMMdd_HHmmss}.json");

            System.IO.File.Move(settingsPath, backupPath, overwrite: true);
        }
        catch
        {
            // Если резервную копию создать не удалось, просто загрузим настройки по умолчанию.
        }
    }
}
