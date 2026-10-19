using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Monolith.Services;
using Monolith.ViewModels;
using Monolith.Views;

namespace Monolith;

public partial class App : System.Windows.Application
{
    private MonolithViewModel? _viewModel;
    private MonolithWindow? _monolithWindow;
    private LauncherWindow? _launcherWindow;
    private HotkeyService? _hotkeyService;
    private TrayIconService? _trayIconService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _viewModel = new MonolithViewModel();
        await _viewModel.InitializeAsync();

        _monolithWindow = new MonolithWindow(_viewModel);
        _launcherWindow = new LauncherWindow();

        // Wire up bidirectional events
        _launcherWindow.RequestOpenStudio = () =>
        {
            _monolithWindow.Show();
            _monolithWindow.WindowState = WindowState.Normal;
            _monolithWindow.Activate();
        };

        _viewModel.RequestShowLauncher += () =>
        {
            if (_viewModel.IsLauncherEnabled)
            {
                _launcherWindow.ShowPalette();
            }
        };

        // Initialize Global Hotkeys
        _hotkeyService = new HotkeyService();
        var helper = new WindowInteropHelper(_monolithWindow);
        helper.EnsureHandle();
        _hotkeyService.Initialize(helper.Handle);

        _hotkeyService.LauncherHotkeyPressed += () =>
        {
            if (_viewModel.IsLauncherEnabled)
            {
                _launcherWindow.TogglePalette();
            }
        };

        _hotkeyService.DashboardHotkeyPressed += () =>
        {
            if (_monolithWindow.IsVisible && _monolithWindow.WindowState != WindowState.Minimized)
            {
                _monolithWindow.Hide();
            }
            else
            {
                _monolithWindow.Show();
                _monolithWindow.WindowState = WindowState.Normal;
                _monolithWindow.Activate();
            }
        };

        _hotkeyService.WorkspaceSwitchHotkeyPressed += ws =>
        {
            if (_viewModel.IsTilerEnabled)
            {
                _ = _viewModel.SwitchWorkspaceAsync(ws);
            }
        };

        _hotkeyService.MoveWindowWorkspaceHotkeyPressed += ws =>
        {
            if (_viewModel.IsTilerEnabled)
            {
                _ = _viewModel.MoveActiveWindowToWorkspaceAsync(ws);
            }
        };

        _hotkeyService.ToggleFloatingHotkeyPressed += () =>
        {
            if (_viewModel.IsTilerEnabled)
            {
                _ = _viewModel.ToggleFloatingActiveWindowAsync();
            }
        };

        _hotkeyService.CloseWindowHotkeyPressed += () =>
        {
            _viewModel.CloseActiveWindow();
        };

        if (_viewModel.IsLauncherEnabled)
        {
            _hotkeyService.EnableLauncherHotkey();
        }

        if (_viewModel.IsTilerEnabled)
        {
            _hotkeyService.EnableTilerHotkeys();
        }

        // React to ViewModel toggle changes
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MonolithViewModel.IsLauncherEnabled))
            {
                if (_viewModel.IsLauncherEnabled)
                    _hotkeyService.EnableLauncherHotkey();
                else
                    _hotkeyService.DisableLauncherHotkey();
            }
            else if (args.PropertyName == nameof(MonolithViewModel.IsTilerEnabled))
            {
                if (_viewModel.IsTilerEnabled)
                    _hotkeyService.EnableTilerHotkeys();
                else
                    _hotkeyService.DisableTilerHotkeys();
            }
        };

        // Initialize System Tray Icon
        _trayIconService = new TrayIconService(
            _viewModel,
            showMainWindow: () =>
            {
                _monolithWindow.Show();
                _monolithWindow.WindowState = WindowState.Normal;
                _monolithWindow.Activate();
            },
            showLauncher: () =>
            {
                if (_viewModel.IsLauncherEnabled)
                {
                    _launcherWindow.ShowPalette();
                }
            },
            exitApp: () =>
            {
                Shutdown();
            }
        );

        // Check command-line arguments
        bool startHidden = e.Args.Any(a => a.Equals("--hidden", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        bool startLauncher = e.Args.Any(a => a.Equals("--launcher", StringComparison.OrdinalIgnoreCase) || a.Equals("-l", StringComparison.OrdinalIgnoreCase));

        if (startLauncher)
        {
            _launcherWindow.ShowPalette();
        }
        else if (!startHidden)
        {
            _monolithWindow.Show();
            _monolithWindow.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        _hotkeyService?.Dispose();
        EngineSupervisor.Instance.Stop();
        base.OnExit(e);
    }
}
