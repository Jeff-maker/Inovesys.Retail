using System.Text.Json;

namespace Inovesys.Retail.Services;

public static class UserSettingsService
{
    private const string FileName = "user_settings.json";

    public static void Save(UserSettings settings)
    {
        var json = JsonSerializer.Serialize(settings);
        File.WriteAllText(GetPath(), json);
    }

    public static UserSettings Load()
    {
        var path = GetPath();
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserSettings>(json) ?? new();
        }

        return new UserSettings();
    }

    public static void Clear()
    {
        var path = GetPath();
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string GetPath()
    {
        var folder = FileSystem.AppDataDirectory;
        return Path.Combine(folder, FileName);
    }
}
