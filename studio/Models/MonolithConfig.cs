using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
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

    [JsonPropertyName("monobar_enabled")]
    public bool MonobarEnabled { get; set; } = true;

    [JsonPropertyName("monobar_height")]
    public int MonobarHeight { get; set; } = 32;

    [JsonPropertyName("monobar_opacity")]
    public int MonobarOpacity { get; set; } = 88;

    [JsonPropertyName("monobar_material")]
    public string MonobarMaterial { get; set; } = "Acrylic (Frosted Glass)";

    [JsonPropertyName("monobar_modules")]
    public List<MonobarModuleConfig> MonobarModules { get; set; } = MonobarModuleConfig.CreateDefaults();

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

    // Monolith uses its own numbered workspaces. These do not map to Windows
    // virtual desktops; workspaces are managed by the Rust engine.
    [JsonPropertyName("wrap_around")]
    public bool WrapAround { get; set; } = true;

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
        new HotkeyBinding { Category = "Window Management", Action = "Focus Up", Binding = "Alt + K" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Down", Binding = "Alt + J" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Left", Binding = "Alt + Left" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Right", Binding = "Alt + Right" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Up", Binding = "Alt + Up" },
        new HotkeyBinding { Category = "Window Management", Action = "Focus Down", Binding = "Alt + Down" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Left", Binding = "Alt + Shift + H" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Down", Binding = "Alt + Shift + J" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Up", Binding = "Alt + Shift + K" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Right", Binding = "Alt + Shift + L" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Left", Binding = "Alt + Shift + Left" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Right", Binding = "Alt + Shift + Right" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Up", Binding = "Alt + Shift + Up" },
        new HotkeyBinding { Category = "Window Management", Action = "Move Window Down", Binding = "Alt + Shift + Down" },
        new HotkeyBinding { Category = "Window Management", Action = "Toggle Float", Binding = "Alt + Shift + Space" },
        new HotkeyBinding { Category = "Window Management", Action = "Tile Vertically", Binding = "Alt + Shift + V" },
        new HotkeyBinding { Category = "Window Management", Action = "Toggle Monocle", Binding = "Alt + M" },
        new HotkeyBinding { Category = "Window Management", Action = "Close Active Window", Binding = "Ctrl + Alt + Q" },
        new HotkeyBinding { Category = "Applications & Commands", Action = "Launch Terminal", Binding = "Ctrl + Alt + T" },
        new HotkeyBinding { Category = "Window Resizing", Action = "Widen Window", Binding = "Ctrl + Alt + Shift + H" },
        new HotkeyBinding { Category = "Window Resizing", Action = "Narrow Window", Binding = "Ctrl + Alt + Shift + L" },
        new HotkeyBinding { Category = "Window Resizing", Action = "Increase Window Height", Binding = "Ctrl + Alt + Shift + J" },
        new HotkeyBinding { Category = "Window Resizing", Action = "Decrease Window Height", Binding = "Ctrl + Alt + Shift + K" },
        new HotkeyBinding { Category = "Workspaces", Action = "Previous Workspace", Binding = "Alt + PageUp" },
        new HotkeyBinding { Category = "Workspaces", Action = "Next Workspace", Binding = "Alt + PageDown" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Previous Workspace", Binding = "Alt + Shift + PageUp" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Next Workspace", Binding = "Alt + Shift + PageDown" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 1", Binding = "Alt + 1" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 2", Binding = "Alt + 2" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 3", Binding = "Alt + 3" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 4", Binding = "Alt + 4" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 5", Binding = "Alt + 5" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 6", Binding = "Alt + 6" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 7", Binding = "Alt + 7" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 8", Binding = "Alt + 8" },
        new HotkeyBinding { Category = "Workspaces", Action = "Switch to Workspace 9", Binding = "Alt + 9" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Workspace 1", Binding = "Alt + Shift + 1" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Workspace 2", Binding = "Alt + Shift + 2" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Workspace 3", Binding = "Alt + Shift + 3" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Workspace 4", Binding = "Alt + Shift + 4" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Workspace 5", Binding = "Alt + Shift + 5" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Workspace 6", Binding = "Alt + Shift + 6" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Workspace 7", Binding = "Alt + Shift + 7" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Workspace 8", Binding = "Alt + Shift + 8" },
        new HotkeyBinding { Category = "Workspaces", Action = "Move Window to Workspace 9", Binding = "Alt + Shift + 9" }
    };
}

public partial class MonobarModuleConfig : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("id")]
    private string _id = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("enabled")]
    private bool _enabled = true;

    [ObservableProperty]
    [property: JsonPropertyName("order")]
    private int _order;

    [ObservableProperty]
    [property: JsonPropertyName("alignment")]
    private string _alignment = "Left";

    [property: JsonIgnore]
    public string DisplayName => Id switch
    {
        "workspaces" => "Workspaces",
        "clock" => "Clock and date",
        "launcher" => "Launcher button",
        "wifi" => "Wi-Fi",
        "volume" => "Volume",
        "battery" => "Battery and brightness",
        "network-speed" => "Network speed",
        "media" => "Media controls",
        "wallpaper" => "Wallpaper picker",
        "taskbar" => "Running apps (icons only)",
        "system-monitor" => "CPU and memory",
        "task-view" => "Task view",
        "show-desktop" => "Show desktop",
        "recycle-bin" => "Recycle Bin",
        _ => Id
    };

    [property: JsonIgnore]
    public string Description => Id switch
    {
        "workspaces" => "Switch directly between Monolith workspaces.",
        "clock" => "Show the current time and date.",
        "launcher" => "Open the Monolith command launcher.",
        "wifi" => "Show Wi-Fi status and network controls.",
        "volume" => "Open the system volume slider.",
        "battery" => "Show battery status and adjust display brightness.",
        "network-speed" => "Show current network upload and download speed.",
        "media" => "Show playback information and media controls.",
        "wallpaper" => "Choose and apply a desktop wallpaper.",
        "taskbar" => "Show open windows as clickable app icons without labels.",
        "system-monitor" => "Show live CPU and memory usage.",
        "task-view" => "Open Windows Task View.",
        "show-desktop" => "Minimize windows and show the desktop.",
        "recycle-bin" => "Open the Windows Recycle Bin.",
        _ => string.Empty
    };

    public static List<MonobarModuleConfig> CreateDefaults() => new()
    {
        new() { Id = "launcher", Enabled = true, Order = 0, Alignment = "Left" },
        new() { Id = "workspaces", Enabled = true, Order = 1, Alignment = "Left" },
        new() { Id = "clock", Enabled = true, Order = 2, Alignment = "Center" },
        new() { Id = "wifi", Enabled = true, Order = 3, Alignment = "Right" },
        new() { Id = "volume", Enabled = true, Order = 4, Alignment = "Right" },
        new() { Id = "battery", Enabled = true, Order = 5, Alignment = "Right" },
        new() { Id = "network-speed", Enabled = false, Order = 6, Alignment = "Right" },
        new() { Id = "media", Enabled = false, Order = 7, Alignment = "Left" },
        new() { Id = "wallpaper", Enabled = false, Order = 8, Alignment = "Right" },
        new() { Id = "taskbar", Enabled = false, Order = 9, Alignment = "Center" },
        new() { Id = "system-monitor", Enabled = false, Order = 10, Alignment = "Right" },
        new() { Id = "task-view", Enabled = false, Order = 11, Alignment = "Left" },
        new() { Id = "show-desktop", Enabled = false, Order = 12, Alignment = "Right" },
        new() { Id = "recycle-bin", Enabled = false, Order = 13, Alignment = "Right" }
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

