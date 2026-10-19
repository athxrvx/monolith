using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monolith.Models;
using Monolith.Services;

namespace Monolith.ViewModels;

public partial class MonolithViewModel : ObservableObject
{
    private readonly IpcClient _ipcClient = new();
    private readonly EngineSupervisor _engineSupervisor = EngineSupervisor.Instance;

    [ObservableProperty]
    private int _currentWorkspace = 1;

    [ObservableProperty]
    private MonolithConfig _config = new();

    [ObservableProperty]
    private bool _isTilerEnabled = true;

    [ObservableProperty]
    private bool _isLauncherEnabled = true;

    [ObservableProperty]
    private string _tilerStatusText = "Active & Tiling";

    [ObservableProperty]
    private string _tilerStatusColor = "#00E676"; // Green

    [ObservableProperty]
    private string _tilerTelemetry = "PID: - | RAM: 0.0 MB";

    [ObservableProperty]
    private string _launcherStatusText = "Hotkey Armed (Alt+Space)";

    [ObservableProperty]
    private string _launcherStatusColor = "#00E5FF"; // Cyan

    [ObservableProperty]
    private string _ipcStatusText = "IPC Connected";

    [ObservableProperty]
    private string _ipcStatusColor = "#00E676";

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    [ObservableProperty]
    private ObservableCollection<WindowRule> _windowRules = new();

    [ObservableProperty]
    private ObservableCollection<HotkeyBinding> _hotkeyBindings = new();

    public event Action? RequestShowLauncher;

    public MonolithViewModel()
    {
        _engineSupervisor.StateChanged += OnEngineSupervisorStateChanged;
        _ipcClient.ConnectionChanged += OnIpcConnectionChanged;
    }

    public async Task InitializeAsync()
    {
        Config = await ConfigService.LoadConfigAsync();
        IsTilerEnabled = Config.IsTilerEnabled;
        IsLauncherEnabled = Config.IsLauncherEnabled;

        WindowRules = new ObservableCollection<WindowRule>(Config.WindowRules);
        HotkeyBindings = new ObservableCollection<HotkeyBinding>(Config.Hotkeys);

        // Start Tiler if enabled
        if (IsTilerEnabled)
        {
            await _engineSupervisor.StartAsync();
        }

        await _ipcClient.StartAsync();
        UpdateTilerTelemetry();
    }

    partial void OnIsTilerEnabledChanged(bool value)
    {
        Config.IsTilerEnabled = value;
        _ = SaveConfigAsync();

        if (value)
        {
            _ = _engineSupervisor.StartAsync();
            TilerStatusText = "Active & Tiling";
            TilerStatusColor = "#00E676";
        }
        else
        {
            _engineSupervisor.Stop();
            TilerStatusText = "Disabled";
            TilerStatusColor = "#888888";
            TilerTelemetry = "Engine Stopped";
        }
    }

    partial void OnIsLauncherEnabledChanged(bool value)
    {
        Config.IsLauncherEnabled = value;
        _ = SaveConfigAsync();

        if (value)
        {
            LauncherStatusText = "Hotkey Armed (Alt+Space)";
            LauncherStatusColor = "#00E5FF";
        }
        else
        {
            LauncherStatusText = "Disabled";
            LauncherStatusColor = "#888888";
        }
    }

    private void OnEngineSupervisorStateChanged(bool running)
    {
        if (IsTilerEnabled)
        {
            TilerStatusText = running ? "Active & Tiling" : "Syncing / Restarting";
            TilerStatusColor = running ? "#00E676" : "#FFB300";
            UpdateTilerTelemetry();
        }
    }

    private void OnIpcConnectionChanged(bool connected)
    {
        IpcStatusText = connected ? "IPC Synced" : "IPC Offline";
        IpcStatusColor = connected ? "#00E676" : "#FF5252";
    }

    public void UpdateTilerTelemetry()
    {
        if (_engineSupervisor.IsRunning)
        {
            TilerTelemetry = $"PID: {_engineSupervisor.ProcessId} | RAM: {_engineSupervisor.MemoryUsage}";
        }
        else if (!IsTilerEnabled)
        {
            TilerTelemetry = "Disabled";
        }
        else
        {
            TilerTelemetry = "Starting...";
        }
    }

    [RelayCommand]
    public async Task RestartTilerAsync()
    {
        if (IsTilerEnabled)
        {
            await _engineSupervisor.RestartAsync();
            UpdateTilerTelemetry();
        }
    }

    [RelayCommand]
    public void OpenLauncherNow()
    {
        RequestShowLauncher?.Invoke();
    }

    [RelayCommand]
    public async Task SaveConfigAsync()
    {
        Config.WindowRules = new System.Collections.Generic.List<WindowRule>(WindowRules);
        Config.Hotkeys = new System.Collections.Generic.List<HotkeyBinding>(HotkeyBindings);
        await ConfigService.SaveConfigAsync(Config);

        // Notify engine
        await _ipcClient.SendCommandAsync("SET_STATE", Config);
    }

    [RelayCommand]
    public async Task ReloadConfigAsync()
    {
        Config = await ConfigService.LoadConfigAsync();
        IsTilerEnabled = Config.IsTilerEnabled;
        IsLauncherEnabled = Config.IsLauncherEnabled;
        WindowRules = new ObservableCollection<WindowRule>(Config.WindowRules);
        HotkeyBindings = new ObservableCollection<HotkeyBinding>(Config.Hotkeys);
        await _ipcClient.SendCommandAsync("RELOAD_CONFIG");
    }

    [RelayCommand]
    public void OpenLogFile()
    {
        string logPath = ConfigService.GetLogPath();
        if (!File.Exists(logPath))
        {
            File.WriteAllText(logPath, $"[INFO] Monolith Desktop Environment initialized at {DateTime.Now}\n");
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = logPath, UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    public void ResetHotkeys()
    {
        var defaultConfig = new MonolithConfig();
        HotkeyBindings.Clear();
        foreach (var hk in defaultConfig.Hotkeys)
        {
            HotkeyBindings.Add(hk);
        }
        _ = SaveConfigAsync();
    }

    public void AddRule(WindowRule rule)
    {
        WindowRules.Add(rule);
        _ = SaveConfigAsync();
    }

    public void RemoveRule(WindowRule rule)
    {
        WindowRules.Remove(rule);
        _ = SaveConfigAsync();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;

    public async Task SwitchWorkspaceAsync(int target)
    {
        if (target < 1 || target > 10) return;
        int previous = CurrentWorkspace;
        CurrentWorkspace = target;

        // 1. Notify engine to adjust managed window visibility & tiling
        await _ipcClient.SendCommandAsync("FOCUS_WORKSPACE", new { workspace = target });

        // 2. Transition native Windows 11 virtual desktops
        VirtualDesktopNav.NavigateToWorkspace(previous, target);
    }

    public async Task MoveActiveWindowToWorkspaceAsync(int target)
    {
        if (target < 1 || target > 10) return;
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        await _ipcClient.SendCommandAsync("MOVE_WINDOW", new { hwnd = hwnd.ToInt64(), workspace = target });
    }

    public async Task ToggleFloatingActiveWindowAsync()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        await _ipcClient.SendCommandAsync("TOGGLE_FLOATING", new { hwnd = hwnd.ToInt64() });
    }

    public void CloseActiveWindow()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }
}

