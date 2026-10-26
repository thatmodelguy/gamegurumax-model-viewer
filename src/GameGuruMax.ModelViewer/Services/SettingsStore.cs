using System.IO;
using System.Text.Json;
using GameGuruMax.ModelViewer.Core;

namespace GameGuruMax.ModelViewer.Services;

public sealed class AppSettings
{
    public string? FilesRoot { get; set; }
}

public static class SettingsStore
{
    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GameGuruMax.ModelViewer",
            "settings.json");

    public static AppPaths LoadPaths()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (!string.IsNullOrWhiteSpace(settings?.FilesRoot)
                    && Directory.Exists(settings.FilesRoot))
                {
                    return new AppPaths(settings.FilesRoot);
                }
            }
        }
        catch
        {
            // fall through to default
        }
        return AppPaths.Default();
    }

    public static void SavePaths(AppPaths paths)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var settings = new AppSettings { FilesRoot = paths.FilesRoot };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
