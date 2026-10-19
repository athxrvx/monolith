using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Monolith.Studio.Models;

namespace Monolith.Studio.Services;

public class ConfigService
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
                    // Migrate legacy BSP/COSMIC preferences to the supported scrolling WM.
                    config.LayoutMode = "Scrolling";
                    bool addedDefaultBinding = false;
                    var newDefaults = new[]
                    {
                        new HotkeyBinding { Category = "Window Management", Action = "Toggle Monocle", Binding = "Alt + M" },
                        new HotkeyBinding { Category = "Window Management", Action = "Close Active Window", Binding = "Ctrl + Alt + Q" },
                        new HotkeyBinding { Category = "Applications & Commands", Action = "Launch Terminal", Binding = "Ctrl + Alt + T" }
                    };
                    foreach (var defaultBinding in newDefaults)
                    {
                        if (!config.Hotkeys.Any(binding => string.Equals(binding.Action, defaultBinding.Action, StringComparison.OrdinalIgnoreCase)))
                        {
                            config.Hotkeys.Add(defaultBinding);
                            addedDefaultBinding = true;
                        }
                    }
                    var navigationDefaults = new[]
                    {
                        new HotkeyBinding { Category = "Window Management", Action = "Focus Left", Binding = "Alt + H" },
                        new HotkeyBinding { Category = "Window Management", Action = "Focus Right", Binding = "Alt + L" },
                        new HotkeyBinding { Category = "Window Management", Action = "Focus Left", Binding = "Alt + Left" },
                        new HotkeyBinding { Category = "Window Management", Action = "Focus Right", Binding = "Alt + Right" },
                        new HotkeyBinding { Category = "Window Management", Action = "Focus Up", Binding = "Alt + K" },
                        new HotkeyBinding { Category = "Window Management", Action = "Focus Down", Binding = "Alt + J" }
                    };
                    foreach (var defaultBinding in navigationDefaults)
                    {
                        if (!config.Hotkeys.Any(binding => string.Equals(binding.Binding, defaultBinding.Binding, StringComparison.OrdinalIgnoreCase)))
                        {
                            config.Hotkeys.Add(defaultBinding);
                            addedDefaultBinding = true;
                        }
                    }
                    if (addedDefaultBinding || !string.Equals(sourcePath, ConfigPath, StringComparison.OrdinalIgnoreCase))
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
            string json = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }
}

