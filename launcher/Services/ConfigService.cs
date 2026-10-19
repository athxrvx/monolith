using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Monolith.Models;

namespace Monolith.Services;

public static class ConfigService
{
    private static readonly string ConfigFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "monolith");
    private static readonly string LegacyConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Monolith", "config.json");
    private static readonly string ConfigPath = Path.Combine(ConfigFolder, "config.json");
    private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Monolith", "monolith.log");

    public static string GetConfigPath() => ConfigPath;
    public static string GetLogPath() => LogPath;

    public static async Task<MonolithConfig> LoadConfigAsync()
    {
        try
        {
            string sourcePath = File.Exists(ConfigPath) ? ConfigPath : LegacyConfigPath;
            if (File.Exists(sourcePath))
            {
                string json = await File.ReadAllTextAsync(sourcePath);
                var config = JsonSerializer.Deserialize<MonolithConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config != null)
                {
                    config.LayoutMode = "Scrolling";
                    if (!string.Equals(sourcePath, ConfigPath, StringComparison.OrdinalIgnoreCase))
                        await SaveConfigAsync(config);
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
        }

        var defaultConfig = new MonolithConfig();
        await SaveConfigAsync(defaultConfig);
        return defaultConfig;
    }

    public static async Task SaveConfigAsync(MonolithConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigFolder);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            config.LayoutMode = "Scrolling";
            JsonObject document = new();
            if (File.Exists(ConfigPath))
            {
                try { document = JsonNode.Parse(await File.ReadAllTextAsync(ConfigPath)) as JsonObject ?? new JsonObject(); }
                catch { /* Replace invalid JSON with the app's current settings. */ }
            }
            if (!File.Exists(ConfigPath) && File.Exists(LegacyConfigPath))
            {
                try { document = JsonNode.Parse(await File.ReadAllTextAsync(LegacyConfigPath)) as JsonObject ?? new JsonObject(); }
                catch { }
            }
            var updates = JsonSerializer.SerializeToNode(config, options) as JsonObject ?? new JsonObject();
            foreach (var pair in updates)
                document[pair.Key] = pair.Value?.DeepClone();
            string json = document.ToJsonString(options);
            await File.WriteAllTextAsync(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }
}

