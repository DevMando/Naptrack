using System.Text.Json;
using System.Text.Json.Serialization;
using Naptrack.Models;

namespace Naptrack.Services;

// Enums are written as names rather than ordinals: config.json is a file users open and edit,
// and "Mp4" is self-explanatory where "1" is not.
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
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
        var loaded = await TryReadAsync();

        Config = loaded ?? new AppConfig();

        if (loaded is null)
        {
            // Seed the file on a first run, or replace one that would not parse.
            try { await SaveAsync(); }
            catch { /* unwritable location; the defaults still work for this session */ }
        }

        try { Directory.CreateDirectory(Config.DownloadFolder); }
        catch { /* the download itself reports this, with context the user can act on */ }
    }

    private static async Task<AppConfig?> TryReadAsync()
    {
        if (!File.Exists(ConfigPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(ConfigPath);
            return JsonSerializer.Deserialize(json, AppConfigContext.Default.AppConfig);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A truncated or hand-edited config.json must not stop the app from starting.
            // LoadAsync is the first await in OnInitializedAsync, so throwing here kills
            // the app before it paints, leaving no way to recover from inside Naptrack.
            return null;
        }
    }

    /// <summary>
    /// Serialises saves. Startup alone saves from the update check and the yt-dlp check at the
    /// same moment, and on Windows the second writer was refused the open file: its save was
    /// swallowed, and whichever snapshot happened to win was what the next launch read.
    /// </summary>
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public async Task SaveAsync()
    {
        await _saveLock.WaitAsync();

        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(Config, AppConfigContext.Default.AppConfig);

            // Written beside the real file and moved over it, so a crash mid-write leaves the
            // previous config intact rather than a truncated one that loads as defaults and
            // silently resets the download folder.
            var staging = ConfigPath + ".tmp";
            await File.WriteAllTextAsync(staging, json);
            File.Move(staging, ConfigPath, overwrite: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
