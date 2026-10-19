using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Monolith.Models;

public class MonolithConfig
{
    // Master Services Toggles
    [JsonPropertyName("is_tiler_enabled")]
    public bool IsTilerEnabled { get; set; } = true;

    [JsonPropertyName("is_launcher_enabled")]
    public bool IsLauncherEnabled { get; set; } = true;

    // General Settings
    [JsonPropertyName("engine_startup")]
    public bool EngineStartup { get; set; } = true;

    [JsonPropertyName("run_launcher_admin")]
    public bool RunLauncherAdmin { get; set; } = false;

    [JsonPropertyName("hide_taskbar")]
    public bool HideTaskbar { get; set; } = false;

    [JsonPropertyName("log_level")]
    public string LogLevel { get; set; } = "Info";

    // Workspaces Settings
    [JsonPropertyName("default_workspace_count")]
    public int DefaultWorkspaceCount { get; set; } = 4;

    [JsonPropertyName("wrap_around")]
    public bool WrapAround { get; set; } = true;

    [JsonPropertyName("workspace_binding_mode")]
    public string WorkspaceBindingMode { get; set; } = "Unified";

    [JsonPropertyName("focus_follows_cursor")]
    public bool FocusFollowsCursor { get; set; } = true;

    // Tiling & Layout Settings
    [JsonPropertyName("layout_mode")]
    public string LayoutMode { get; set; } = "BSP";

    [JsonPropertyName("outer_gaps_top")]
    public int OuterGapsTop { get; set; } = 12;

    [JsonPropertyName("outer_gaps_bottom")]
    public int OuterGapsBottom { get; set; } = 12;

    [JsonPropertyName("outer_gaps_left")]
    public int OuterGapsLeft { get; set; } = 12;

    [JsonPropertyName("outer_gaps_right")]
    public int OuterGapsRight { get; set; } = 12;

    [JsonPropertyName("inner_gaps")]
    public int InnerGaps { get; set; } = 8;

    [JsonPropertyName("aspect_ratio_lock")]
    public bool AspectRatioLock { get; set; } = false;

    [JsonPropertyName("resize_step_percentage")]
    public int ResizeStepPercentage { get; set; } = 5;

    [JsonPropertyName("min_width")]
    public int MinWidth { get; set; } = 240;

    [JsonPropertyName("min_height")]
    public int MinHeight { get; set; } = 180;

    // Window Rules
    [JsonPropertyName("window_rules")]
    public List<WindowRule> WindowRules { get; set; } = new()
    {
        new WindowRule { ProcessName = "spotify.exe", TitleRegex = ".*", Action = "Force Tile", TargetWorkspace = 4, ManageBorderVisibility = true },
        new WindowRule { ProcessName = "discord.exe", TitleRegex = ".*", Action = "Send to Workspace", TargetWorkspace = 3, ManageBorderVisibility = false },
        new WindowRule { ProcessName = "taskmgr.exe", TitleRegex = ".*", Action = "Force Float", TargetWorkspace = 1, ManageBorderVisibility = false },
        new WindowRule { ProcessName = "calculatorApp.exe", TitleRegex = ".*", Action = "Force Float", TargetWorkspace = 1, ManageBorderVisibility = false }
    };

    // Appearance & Themes
    [JsonPropertyName("enable_active_border")]
    public bool EnableActiveBorder { get; set; } = true;

    [JsonPropertyName("active_border_color")]
    public string ActiveBorderColor { get; set; } = "#00E5FF";

    [JsonPropertyName("inactive_border_color")]
    public string InactiveBorderColor { get; set; } = "#26FFFFFF";

    [JsonPropertyName("border_thickness")]
    public int BorderThickness { get; set; } = 2;

    [JsonPropertyName("corner_radius")]
    public int CornerRadius { get; set; } = 8;

    [JsonPropertyName("backdrop_material")]
    public string BackdropMaterial { get; set; } = "Acrylic (Frosted Glass)";

    [JsonPropertyName("launcher_opacity")]
    public int LauncherOpacity { get; set; } = 60;

    [JsonPropertyName("enable_animations")]
    public bool EnableAnimations { get; set; } = true;

    [JsonPropertyName("reflow_duration")]
    public int ReflowDuration { get; set; } = 120;

    // Hotkeys
    [JsonPropertyName("hotkeys")]
    public List<HotkeyBinding> Hotkeys { get; set; } = new()
    {
        new HotkeyBinding { Category = "Launcher & System", Action = "Toggle Launcher", Binding = "Alt + Space" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Left", Binding = "Alt + H" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Down", Binding = "Alt + J" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Up", Binding = "Alt + K" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Right", Binding = "Alt + L" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Left", Binding = "Alt + Shift + H" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Down", Binding = "Alt + Shift + J" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Up", Binding = "Alt + Shift + K" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Right", Binding = "Alt + Shift + L" },
        new HotkeyBinding { Category = "Window Management", Action = "Toggle Float", Binding = "Alt + Shift + Space" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 1", Binding = "Alt + 1" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 2", Binding = "Alt + 2" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 3", Binding = "Alt + 3" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 4", Binding = "Alt + 4" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move to Workspace 1", Binding = "Alt + Shift + 1" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move to Workspace 2", Binding = "Alt + Shift + 2" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move to Workspace 3", Binding = "Alt + Shift + 3" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move to Workspace 4", Binding = "Alt + Shift + 4" }
    };
}

public class WindowRule
{
    [JsonPropertyName("process_name")]
    public string ProcessName { get; set; } = string.Empty;

    [JsonPropertyName("title_regex")]
    public string TitleRegex { get; set; } = ".*";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "Force Tile";

    [JsonPropertyName("target_workspace")]
    public int TargetWorkspace { get; set; } = 1;

    [JsonPropertyName("manage_border_visibility")]
    public bool ManageBorderVisibility { get; set; } = true;
}

public class HotkeyBinding
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("binding")]
    public string Binding { get; set; } = string.Empty;
}

