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
                    bool migratedMonobarHeight = config.MonobarHeight == 48;
                    if (migratedMonobarHeight)
                        config.MonobarHeight = 32;
                    var monobarDefaults = MonobarModuleConfig.CreateDefaults();
                    var oldModules = config.MonobarModules ?? new System.Collections.Generic.List<MonobarModuleConfig>();
                    var migratedMonobarModules = new System.Collections.Generic.List<MonobarModuleConfig>();
                    bool monobarModulesChanged = oldModules.Count != monobarDefaults.Count;
                    foreach (var defaultModule in monobarDefaults)
                    {
                        var previous = oldModules.FirstOrDefault(module =>
                            string.Equals(module.Id, defaultModule.Id, StringComparison.OrdinalIgnoreCase));
                        migratedMonobarModules.Add(new MonobarModuleConfig
                        {
                            Id = defaultModule.Id,
                            Enabled = previous?.Enabled ?? defaultModule.Enabled,
                            Order = previous?.Order ?? defaultModule.Order,
                            Alignment = previous?.Alignment ?? defaultModule.Alignment
                        });
                        if (previous is null || !string.Equals(previous.Id, defaultModule.Id, StringComparison.OrdinalIgnoreCase))
                            monobarModulesChanged = true;
                    }
                    config.MonobarModules = migratedMonobarModules;
                    // Migrate legacy BSP/COSMIC preferences to the supported scrolling WM.
                    config.LayoutMode = "Scrolling";
                    bool addedDefaultBinding = false;
                    var workspaceActionMigration = new[]
                    {
                        ("Previous Desktop", "Previous Workspace", "Ctrl + Alt + Left", "Alt + PageUp"),
                        ("Next Desktop", "Next Workspace", "Ctrl + Alt + Right", "Alt + PageDown"),
                        ("Move Window to Previous Desktop", "Move Window to Previous Workspace", "Ctrl + Alt + Shift + Left", "Alt + Shift + PageUp"),
                        ("Move Window to Next Desktop", "Move Window to Next Workspace", "Ctrl + Alt + Shift + Right", "Alt + Shift + PageDown")
                    };
                    foreach (var (oldAction, newAction, oldBinding, newBinding) in workspaceActionMigration)
                    {
                        foreach (var binding in config.Hotkeys.Where(binding => string.Equals(binding.Action, oldAction, StringComparison.OrdinalIgnoreCase)))
                        {
                            binding.Action = newAction;
                            if (string.Equals(binding.Binding, oldBinding, StringComparison.OrdinalIgnoreCase))
                                binding.Binding = newBinding;
                            addedDefaultBinding = true;
                        }
                    }
                    int removedConflictingWorkspaceBindings = config.Hotkeys.RemoveAll(binding =>
                        string.Equals(binding.Category, "Workspaces", StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(binding.Action, "Previous Workspace", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(binding.Action, "Next Workspace", StringComparison.OrdinalIgnoreCase))
                        && new[] { "Alt + Shift + Up", "Alt + Shift + Down", "Alt + Shift + Left", "Alt + Shift + Right", "Alt + Shift + H", "Alt + Shift + J", "Alt + Shift + K", "Alt + Shift + L" }
                            .Contains(binding.Binding, StringComparer.OrdinalIgnoreCase));
                    if (removedConflictingWorkspaceBindings > 0) addedDefaultBinding = true;
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
                        new HotkeyBinding { Category = "Window Management", Action = "Focus Up", Binding = "Alt + Up" },
                        new HotkeyBinding { Category = "Window Management", Action = "Focus Down", Binding = "Alt + Down" },
                        new HotkeyBinding { Category = "Window Management", Action = "Focus Up", Binding = "Alt + K" },
                        new HotkeyBinding { Category = "Window Management", Action = "Focus Down", Binding = "Alt + J" },
                        new HotkeyBinding { Category = "Window Management", Action = "Move Window Left", Binding = "Alt + Shift + Left" },
                        new HotkeyBinding { Category = "Window Management", Action = "Move Window Right", Binding = "Alt + Shift + Right" },
                        new HotkeyBinding { Category = "Window Management", Action = "Move Window Up", Binding = "Alt + Shift + Up" },
                        new HotkeyBinding { Category = "Window Management", Action = "Move Window Down", Binding = "Alt + Shift + Down" },
                        new HotkeyBinding { Category = "Window Management", Action = "Move Window Left", Binding = "Alt + Shift + H" },
                        new HotkeyBinding { Category = "Window Management", Action = "Move Window Right", Binding = "Alt + Shift + L" },
                        new HotkeyBinding { Category = "Window Management", Action = "Move Window Up", Binding = "Alt + Shift + K" },
                        new HotkeyBinding { Category = "Window Management", Action = "Move Window Down", Binding = "Alt + Shift + J" }
                    };
                    foreach (var defaultBinding in navigationDefaults)
                    {
                        if (!config.Hotkeys.Any(binding => string.Equals(binding.Binding, defaultBinding.Binding, StringComparison.OrdinalIgnoreCase)))
                        {
                            config.Hotkeys.Add(defaultBinding);
                            addedDefaultBinding = true;
                        }
                    }
                    foreach (var defaultBinding in new MonolithConfig().Hotkeys.Where(binding => binding.Category == "Workspaces"))
                    {
                        if (!config.Hotkeys.Any(binding => string.Equals(binding.Action, defaultBinding.Action, StringComparison.OrdinalIgnoreCase)))
                        {
                            config.Hotkeys.Add(defaultBinding);
                            addedDefaultBinding = true;
                        }
                    }
                    if (addedDefaultBinding || migratedMonobarHeight || monobarModulesChanged || !string.Equals(sourcePath, ConfigPath, StringComparison.OrdinalIgnoreCase))
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

