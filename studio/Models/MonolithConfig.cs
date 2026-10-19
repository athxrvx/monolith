using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Monolith.Studio.Models;

public class MonolithConfig
{
    // General Settings
    [JsonPropertyName("monolith_enabled")]
    public bool MonolithEnabled { get; set; } = true;

    [JsonPropertyName("engine_startup")]
    public bool EngineStartup { get; set; } = true;

    public bool LauncherEnabled { get; set; } = true;

    [JsonPropertyName("edge_navigation_enabled")]
    public bool EdgeNavigationEnabled { get; set; } = true;

    [JsonPropertyName("onboarding_completed")]
    public bool OnboardingCompleted { get; set; } = false;

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

    // Scrolling window manager settings
    [JsonPropertyName("layout_mode")]
    public string LayoutMode { get; set; } = "Scrolling";

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
        new WindowRule { ProcessName = "spotify.exe", TitleRegex = ".*", Action = "Force Tile", ManageBorderVisibility = true },
        new WindowRule { ProcessName = "taskmgr.exe", TitleRegex = ".*", Action = "Force Float", ManageBorderVisibility = false },
        new WindowRule { ProcessName = "calculatorApp.exe", TitleRegex = ".*", Action = "Force Float", ManageBorderVisibility = false }
    };

    // Appearance & Themes
    [JsonPropertyName("enable_active_border")]
    public bool EnableActiveBorder { get; set; } = true;

    [JsonPropertyName("active_border_color")]
    public string ActiveBorderColor { get; set; } = "#60CDFF";

    [JsonPropertyName("inactive_border_color")]
    public string InactiveBorderColor { get; set; } = "#26FFFFFF";

    [JsonPropertyName("border_thickness")]
    public int BorderThickness { get; set; } = 2;

    [JsonPropertyName("backdrop_material")]
    public string BackdropMaterial { get; set; } = "Acrylic (Frosted Glass)";

    [JsonPropertyName("launcher_opacity")]
    public int LauncherOpacity { get; set; } = 60;

    [JsonPropertyName("enable_animations")]
    public bool EnableAnimations { get; set; } = true;

    [JsonPropertyName("animation_mode")]
    public string AnimationMode { get; set; } = "Smooth";

    [JsonPropertyName("reflow_duration")]
    public int ReflowDuration { get; set; } = 180;

    // Hotkeys
    [JsonPropertyName("hotkeys")]
    public List<HotkeyBinding> Hotkeys { get; set; } = new()
    {
        new HotkeyBinding { Category = "Launcher & System", Action = "Toggle Launcher", Binding = "Alt + Space" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Left", Binding = "Alt + H" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Right", Binding = "Alt + L" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Left", Binding = "Alt + Left" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Right", Binding = "Alt + Right" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Up", Binding = "Alt + K" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Down", Binding = "Alt + J" },
        new HotkeyBinding { Category = "Window Management", Action = "Toggle Float", Binding = "Alt + Shift + Space" },
        new HotkeyBinding { Category = "Window Management", Action = "Tile Vertically", Binding = "Alt + Shift + V" },
        new HotkeyBinding { Category = "Window Management", Action = "Toggle Monocle", Binding = "Alt + M" },
        new HotkeyBinding { Category = "Window Management", Action = "Close Active Window", Binding = "Ctrl + Alt + Q" },
        new HotkeyBinding { Category = "Applications & Commands", Action = "Launch Terminal", Binding = "Ctrl + Alt + T" },
        new HotkeyBinding { Category = "Window Resizing", Action = "Widen Window", Binding = "Ctrl + Alt + Shift + H" },
        new HotkeyBinding { Category = "Window Resizing", Action = "Narrow Window", Binding = "Ctrl + Alt + Shift + L" },
        new HotkeyBinding { Category = "Window Resizing", Action = "Increase Window Height", Binding = "Ctrl + Alt + Shift + J" },
        new HotkeyBinding { Category = "Window Resizing", Action = "Decrease Window Height", Binding = "Ctrl + Alt + Shift + K" },
        new HotkeyBinding { Category = "Workspaces", Action = "Previous Desktop", Binding = "Ctrl + Alt + Left" },
        new HotkeyBinding { Category = "Workspaces", Action = "Next Desktop", Binding = "Ctrl + Alt + Right" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Previous Desktop", Binding = "Ctrl + Alt + Shift + Left" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Next Desktop", Binding = "Ctrl + Alt + Shift + Right" }
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

    [JsonPropertyName("argument")]
    public string Argument { get; set; } = string.Empty;
}

