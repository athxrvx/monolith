using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Monolith.Studio.Models;
using Monolith.Studio.Services;
using Monolith.Services;

namespace Monolith.Studio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly EngineIpcClient _ipcClient = new();
    private readonly EngineSupervisor _engineSupervisor = EngineSupervisor.Instance;
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread()!;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    [ObservableProperty]
    private MonolithConfig _config = new();

    [ObservableProperty]
    private string _engineStatusText = "Stopped";

    [ObservableProperty]
    private string _engineStatusColor = "#FF5252"; // Red

    [ObservableProperty]
    private int _enginePid = 0;

    [ObservableProperty]
    private string _engineRamUsage = "0.0 MB";

    [ObservableProperty]
    private bool _isEngineConnected = false;

    [ObservableProperty]
    private ObservableCollection<WindowRule> _windowRules = new();

    [ObservableProperty]
    private ObservableCollection<HotkeyBinding> _hotkeyBindings = new();

    [ObservableProperty]
    private ObservableCollection<MonobarModuleConfig> _monobarModules = new();

    [ObservableProperty]
    private int _activeWorkspace = 1;

    public MainViewModel()
    {
        _ipcClient.ConnectionChanged += OnConnectionChanged;
        _ipcClient.StateReceived += OnStateReceived;
    }

    public async Task InitializeAsync()
    {
        Config = await ConfigService.LoadConfigAsync();

        WindowRules = new ObservableCollection<WindowRule>(Config.WindowRules);
        HotkeyBindings = new ObservableCollection<HotkeyBinding>(Config.Hotkeys);
        MonobarModules = new ObservableCollection<MonobarModuleConfig>(Config.MonobarModules ?? MonobarModuleConfig.CreateDefaults());

        await _ipcClient.StartAsync();
        if (Config.MonolithEnabled && Config.EngineStartup && !_engineSupervisor.IsRunning)
        {
            await _engineSupervisor.StartAsync();
        }
        UpdateEngineProcessInfo();
    }

    private void OnConnectionChanged(bool connected)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            IsEngineConnected = connected;
            if (connected)
            {
                EngineStatusText = "Running";
                EngineStatusColor = "#00E676";
                _ = _ipcClient.SendCommandAsync("GET_STATE");
            }
            else
            {
                EngineStatusText = "Stopped";
                EngineStatusColor = "#FF5252";
                EnginePid = 0;
                EngineRamUsage = "0.0 MB";
            }
            App.MainWindowInstance?.RefreshMonobarState();
        });
    }

    private void OnStateReceived(string json)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("active_workspace", out var workspace)
                    && workspace.TryGetInt32(out int activeWorkspace))
                    ActiveWorkspace = Math.Clamp(activeWorkspace, 1, 9);
            }
            catch (JsonException) { }
            UpdateEngineProcessInfo();
            App.MainWindowInstance?.RefreshMonobarState();
        });
    }

    public void UpdateEngineProcessInfo()
    {
        EnginePid = _engineSupervisor.ProcessId;
        EngineRamUsage = _engineSupervisor.MemoryUsage;
        if (_engineSupervisor.IsRunning)
        {
            EngineStatusText = "Running";
            EngineStatusColor = "#00E676";
        }
    }

    [RelayCommand]
    public async Task SaveConfigAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            Config.WindowRules = new System.Collections.Generic.List<WindowRule>(WindowRules);
            Config.Hotkeys = new System.Collections.Generic.List<HotkeyBinding>(HotkeyBindings);
            Config.MonobarModules = new System.Collections.Generic.List<MonobarModuleConfig>(MonobarModules.OrderBy(module => module.Order));
            await ConfigService.SaveConfigAsync(Config);
            StartupRegistrationService.Apply(Config.EngineStartup);
            AccentColorService.RefreshAccent();

            // Push SET_STATE to engine
            await _ipcClient.SendCommandAsync("SET_STATE", Config);
            _dispatcherQueue.TryEnqueue(() =>
            {
                App.RefreshHotkeys?.Invoke();
                App.MainWindowInstance?.ApplyLauncherAppearance();
                App.MainWindowInstance?.ApplyMonobarConfiguration();
                App.MainWindowInstance?.ApplyMonoDockConfiguration();
            });
        }
        finally
        {
            _saveGate.Release();
        }
    }

    [RelayCommand]
    public async Task ReloadConfigAsync()
    {
        Config = await ConfigService.LoadConfigAsync();
        WindowRules = new ObservableCollection<WindowRule>(Config.WindowRules);
        HotkeyBindings = new ObservableCollection<HotkeyBinding>(Config.Hotkeys);
        MonobarModules = new ObservableCollection<MonobarModuleConfig>(Config.MonobarModules ?? MonobarModuleConfig.CreateDefaults());
        await _ipcClient.SendCommandAsync("RELOAD_CONFIG");
        App.MainWindowInstance?.ApplyMonobarConfiguration();
        App.MainWindowInstance?.ApplyMonoDockConfiguration();
    }

    [RelayCommand]
    public async Task RestartEngineAsync()
    {
        await _engineSupervisor.RestartAsync();
        UpdateEngineProcessInfo();
    }

    [RelayCommand]
    public void StopEngine()
    {
        _engineSupervisor.Stop();

        EngineStatusText = "Stopped";
        EngineStatusColor = "#FF5252";
        EnginePid = 0;
        EngineRamUsage = "0.0 MB";
    }

    public async Task SetTilerEnabledAsync(bool enabled)
    {
        if (enabled)
        {
            await _engineSupervisor.StartAsync();
            UpdateEngineProcessInfo();
        }
        else
        {
            StopEngine();
        }
    }

    public async Task SetMonolithEnabledAsync(bool enabled)
    {
        Config.MonolithEnabled = enabled;
        OnPropertyChanged(nameof(Config));

        if (enabled)
        {
            App.SetHotkeysEnabled?.Invoke(true);
            if (Config.LauncherEnabled)
                App.MainWindowInstance?.SetLauncherAvailable(true);
            await _engineSupervisor.StartAsync();
            UpdateEngineProcessInfo();
        }
        else
        {
            App.SetHotkeysEnabled?.Invoke(false);
            App.MainWindowInstance?.SetLauncherAvailable(false);
            StopEngine();
        }

        await SaveConfigAsync();
    }

    public async Task SetLauncherEnabledAsync(bool enabled)
    {
        Config.LauncherEnabled = enabled;
        OnPropertyChanged(nameof(Config));
        await SaveConfigAsync();
    }

    public async Task SwitchWorkspaceAsync(int workspace)
    {
        bool sent = await _ipcClient.SendCommandAsync("FOCUS_WORKSPACE", new { workspace });
        App.LogStartup($"Workspace switch command to {workspace}: {(sent ? "sent" : "IPC unavailable")}");
    }

    public void MoveMonobarModule(string id, int offset)
    {
        int index = -1;
        for (int i = 0; i < MonobarModules.Count; i++)
        {
            if (string.Equals(MonobarModules[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        int target = index + Math.Sign(offset);
        if (index < 0 || target < 0 || target >= MonobarModules.Count) return;
        MonobarModules.Move(index, target);
        for (int i = 0; i < MonobarModules.Count; i++)
            MonobarModules[i].Order = i;
        Config.MonobarModules = MonobarModules.OrderBy(module => module.Order).ToList();
    }

    public async Task NavigateWorkspaceAsync(int delta)
    {
        await _ipcClient.SendCommandAsync("FOCUS_WORKSPACE_DELTA", new { delta });
    }

    public async Task FocusDirectionAsync(string direction)
    {
        await _ipcClient.SendCommandAsync("FOCUS_DIRECTION", new { direction });
    }

    public async Task MoveWindowDirectionAsync(string direction)
    {
        await _ipcClient.SendCommandAsync("MOVE_WINDOW_DIRECTION", new { direction });
    }

    public async Task MoveActiveWindowWorkspaceAsync(int delta)
    {
        await _ipcClient.SendCommandAsync("MOVE_WINDOW_WORKSPACE_DELTA", new { delta });
    }

    public async Task MoveActiveWindowToWorkspaceAsync(int workspace, long? hwnd = null)
    {
        bool sent = await _ipcClient.SendCommandAsync("MOVE_WINDOW", new { workspace, hwnd });
        App.LogStartup($"Move focused window to workspace {workspace}: {(sent ? "sent" : "IPC unavailable")}");
    }

    public async Task ToggleFloatingAsync(long? hwnd = null)
    {
        await _ipcClient.SendCommandAsync("TOGGLE_FLOATING", new { hwnd });
    }

    public async Task ToggleVerticalTileAsync(long? hwnd = null)
    {
        await _ipcClient.SendCommandAsync("TOGGLE_VERTICAL_TILE", new { hwnd });
    }

    public async Task ToggleMonocleAsync(long? hwnd = null)
    {
        await _ipcClient.SendCommandAsync("TOGGLE_MONOCLE", new { hwnd });
    }

    public async Task ResizeActiveWindowAsync(string axis, int direction)
    {
        await _ipcClient.SendCommandAsync("RESIZE_WINDOW", new { axis, direction });
    }

    [RelayCommand]
    public void OpenLogFile()
    {
        string logPath = ConfigService.GetLogPath();
        if (!File.Exists(logPath))
        {
            File.WriteAllText(logPath, $"[INFO] Monolith Desktop Environment Log initialized at {DateTime.Now}\n");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true
            });
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

    public void AddHotkey(string action, string binding, string argument = "")
    {
        string category = action.Contains("Workspace", StringComparison.OrdinalIgnoreCase) || action.Contains("Desktop", StringComparison.OrdinalIgnoreCase)
            ? "Workspaces"
            : action.Contains("Launch", StringComparison.OrdinalIgnoreCase) || action.Contains("Command", StringComparison.OrdinalIgnoreCase)
                ? "Applications & Commands"
                : "Window Management";
        HotkeyBindings.Add(new HotkeyBinding { Category = category, Action = action, Binding = binding, Argument = argument });
        _ = SaveConfigAsync();
    }

    public void RemoveHotkey(HotkeyBinding binding)
    {
        HotkeyBindings.Remove(binding);
        _ = SaveConfigAsync();
    }

}

