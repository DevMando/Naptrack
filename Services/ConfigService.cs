using System.Text.Json;
using System.Text.Json.Serialization;
using Naptrack.Models;

namespace Naptrack.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
internal partial class AppConfigContext : JsonSerializerContext;

public class ConfigService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Naptrack");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = AppConfigContext.Default
    };

    public AppConfig Config { get; private set; } = new();

    public async Task LoadAsync()
    {
        if (File.Exists(ConfigPath))
        {
            var json = await File.ReadAllTextAsync(ConfigPath);
            Config = JsonSerializer.Deserialize(json, AppConfigContext.Default.AppConfig) ?? new AppConfig();
        }
        else
        {
            Config = new AppConfig();
            await SaveAsync();
        }

        Directory.CreateDirectory(Config.DownloadFolder);
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(Config, AppConfigContext.Default.AppConfig);
        await File.WriteAllTextAsync(ConfigPath, json);
    }
}
