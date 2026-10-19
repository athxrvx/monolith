using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Monolith.Studio.Models;
using Monolith.Studio.Services;
using Monolith.Studio.Views;
using Monolith.Services;

namespace Monolith.Studio;

public sealed partial class MainWindow : Window
{
    private const uint WmClose = 0x0010;
    private const int SwMinimize = 6;
    private const int SwMaximize = 3;
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostWindowMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    private readonly WorkspaceHotkeyService _workspaceHotkeys;
    private readonly LauncherWindow _launcherWindow;
    private readonly TrayIconService? _trayIconService;
    private bool _allowWindowClose;

    public MainWindow()
    {
        App.LogStartup("MainWindow constructor entered");
        InitializeComponent();
        App.LogStartup("MainWindow XAML loaded");

        // Match the spacious layout of Windows Settings and leave room for the page content.
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1320, Height = 860 });
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "monolith_icon.ico");
        if (File.Exists(iconPath))
            appWindow.SetIcon(iconPath);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _launcherWindow = new LauncherWindow();
        App.MainWindowInstance = this;
        _workspaceHotkeys = new WorkspaceHotkeyService(windowHandle, App.MainViewModel.HotkeyBindings, OnHotkeyPressed);
        App.RefreshHotkeys = () => _workspaceHotkeys.ReloadBindings(App.MainViewModel.HotkeyBindings);
        App.SetHotkeysEnabled = _workspaceHotkeys.SetEnabled;
        try
        {
            _trayIconService = new TrayIconService(
                showSettings: ShowSettingsWindow,
                showLauncher: ShowLauncher,
                isMonolithEnabled: () => App.MainViewModel.Config.MonolithEnabled,
                setMonolithEnabled: enabled => _ = App.MainViewModel.SetMonolithEnabledAsync(enabled),
                isTilerEnabled: () => EngineSupervisor.Instance.IsRunning,
                setTilerEnabled: enabled => _ = App.MainViewModel.SetTilerEnabledAsync(enabled),
                isLauncherEnabled: () => App.MainViewModel.Config.LauncherEnabled,
                setLauncherEnabled: enabled => _ = App.MainViewModel.SetLauncherEnabledAsync(enabled),
                isEdgeNavigationEnabled: () => App.MainViewModel.Config.EdgeNavigationEnabled,
                setEdgeNavigationEnabled: enabled => _ = App.MainViewModel.SetEdgeNavigationEnabledAsync(enabled),
                restartTiler: () => _ = App.MainViewModel.RestartEngineAsync(),
                exitApp: ExitCompletely);
            App.LogStartup("Tray icon initialized");
        }
        catch (Exception ex)
        {
            App.LogStartup("Tray icon initialization failed", ex);
        }
        appWindow.Closing += AppWindow_Closing;
        Closed += (_, _) =>
        {
            App.RefreshHotkeys = null;
            App.SetHotkeysEnabled = null;
            App.MainWindowInstance = null;
            _workspaceHotkeys.Dispose();
            _launcherWindow.Close();
            _trayIconService?.Dispose();
        };

        // Use the WinUI Acrylic backdrop supported by the app's current Windows App SDK runtime.
        SystemBackdrop = new DesktopAcrylicBackdrop();
        ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;

        // Default to General page
        NavView.SelectedItem = NavView.MenuItems[0];
        NavigateToPage("General");
    }

    private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_allowWindowClose || _trayIconService is null)
            return;

        args.Cancel = true;
        sender.Hide();
    }

    private void ShowSettingsWindow()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
        Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id).Show();
        Activate();
    }

    private void ShowLauncher()
    {
        if (App.MainViewModel.Config.MonolithEnabled && App.MainViewModel.Config.LauncherEnabled)
            _launcherWindow.TogglePalette();
    }

    public void SetLauncherAvailable(bool enabled)
    {
        if (!enabled)
            _launcherWindow.HidePalette();
    }

    private void ExitCompletely()
    {
        _allowWindowClose = true;
        App.MainViewModel.StopEngine();
        Close();
        App.Current.Exit();
    }

    private void OnHotkeyPressed(Monolith.Studio.Models.HotkeyBinding binding)
    {
        if (!App.MainViewModel.Config.MonolithEnabled)
            return;
        string action = binding.Action.Trim();
        if (action.Equals("Toggle Launcher", StringComparison.OrdinalIgnoreCase))
        {
            ShowLauncher();
        }
        else if (action.StartsWith("Focus ", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.FocusDirectionAsync(action[6..].ToLowerInvariant());
        }
        else if (action.Equals("Next Desktop", StringComparison.OrdinalIgnoreCase))
        {
            App.MainViewModel.SwitchNativeDesktop(true);
        }
        else if (action.Equals("Previous Desktop", StringComparison.OrdinalIgnoreCase))
        {
            App.MainViewModel.SwitchNativeDesktop(false);
        }
        else if (action.Equals("Move Window to Next Desktop", StringComparison.OrdinalIgnoreCase))
        {
            App.MainViewModel.MoveActiveWindowToDesktop(true);
        }
        else if (action.Equals("Move Window to Previous Desktop", StringComparison.OrdinalIgnoreCase))
        {
            App.MainViewModel.MoveActiveWindowToDesktop(false);
        }
        else if (action.Equals("Toggle Float", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.ToggleFloatingAsync();
        }
        else if (action.Equals("Tile Vertically", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.ToggleVerticalTileAsync();
        }
        else if (action.Equals("Toggle Monocle", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.ToggleMonocleAsync();
        }
        else if (action.Equals("Close Active Window", StringComparison.OrdinalIgnoreCase))
        {
            PostWindowMessage(GetForegroundWindow(), WmClose, UIntPtr.Zero, IntPtr.Zero);
        }
        else if (action.Equals("Minimize Active Window", StringComparison.OrdinalIgnoreCase))
        {
            ShowWindow(GetForegroundWindow(), SwMinimize);
        }
        else if (action.Equals("Maximize/Restore Active Window", StringComparison.OrdinalIgnoreCase))
        {
            var foreground = GetForegroundWindow();
            ShowWindow(foreground, IsZoomed(foreground) ? SwRestore : SwMaximize);
        }
        else if (action.Equals("Launch Terminal", StringComparison.OrdinalIgnoreCase) || action.Equals("Launch Terminal (wt.exe)", StringComparison.OrdinalIgnoreCase))
        {
            LaunchProgram(string.IsNullOrWhiteSpace(binding.Argument) ? "wt.exe" : binding.Argument);
        }
        else if (action.Equals("Open File Explorer", StringComparison.OrdinalIgnoreCase))
        {
            LaunchProgram("explorer.exe");
        }
        else if (action.Equals("Launch Application", StringComparison.OrdinalIgnoreCase))
        {
            LaunchProgram(binding.Argument);
        }
        else if (action.Equals("Run Shell Command", StringComparison.OrdinalIgnoreCase))
        {
            RunShellCommand(binding.Argument);
        }
        else if (action.Equals("Widen Window", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.ResizeActiveWindowAsync("width", 1);
        }
        else if (action.Equals("Narrow Window", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.ResizeActiveWindowAsync("width", -1);
        }
        else if (action.Equals("Increase Window Height", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.ResizeActiveWindowAsync("height", 1);
        }
        else if (action.Equals("Decrease Window Height", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.ResizeActiveWindowAsync("height", -1);
        }
    }

    private static void LaunchProgram(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { App.LogStartup($"Could not launch '{path}'", ex); }
    }

    private static void RunShellCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c " + command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex) { App.LogStartup("Could not run configured shell command", ex); }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateToPage(tag);
        }
    }

    internal async System.Threading.Tasks.Task ShowOnboardingIfNeededAsync()
    {
        if (App.MainViewModel.Config.OnboardingCompleted)
            return;

        // Startup configuration can finish before WinUI has attached the page
        // tree to a XamlRoot. ContentDialog.ShowAsync requires that root.
        var xamlRoot = await WaitForContentXamlRootAsync();

        var startupToggle = new ToggleSwitch
        {
            Header = "Start Monolith with Windows",
            OnContent = "On",
            OffContent = "Off",
            IsOn = App.MainViewModel.Config.EngineStartup
        };
        var launcherToggle = new ToggleSwitch
        {
            Header = "Enable the launcher",
            OnContent = "On",
            OffContent = "Off",
            IsOn = App.MainViewModel.Config.LauncherEnabled
        };
        var content = new StackPanel { Spacing = 14, Width = 460 };
        content.Children.Add(new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/monolith.png")), Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Left });
        content.Children.Add(new TextBlock { Text = "A calmer way to manage your windows", FontSize = 23, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "Monolith arranges windows in a smooth scrolling workspace. Use Alt + H/J/K/L or Alt + arrow keys to move focus, and Alt + Space to open the launcher.", TextWrapping = TextWrapping.Wrap, Opacity = 0.78 });
        content.Children.Add(new Border { Height = 1, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(35, 255, 255, 255)), Margin = new Thickness(0, 2, 0, 2) });
        content.Children.Add(startupToggle);
        content.Children.Add(launcherToggle);

        var dialog = new ContentDialog
        {
            Title = "Welcome to Monolith",
            Content = content,
            PrimaryButtonText = "Finish setup",
            CloseButtonText = "Skip",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };
        await dialog.ShowAsync();

        App.MainViewModel.Config.EngineStartup = startupToggle.IsOn;
        App.MainViewModel.Config.LauncherEnabled = launcherToggle.IsOn;
        App.MainViewModel.Config.OnboardingCompleted = true;
        await App.MainViewModel.SaveConfigAsync();
        if (!startupToggle.IsOn)
            App.MainViewModel.StopEngine();
        else if (!EngineSupervisor.Instance.IsRunning)
            await App.MainViewModel.SetTilerEnabledAsync(true);

    }

    private Task<XamlRoot> WaitForContentXamlRootAsync()
    {
        if (ContentFrame.XamlRoot is { } root)
            return Task.FromResult(root);

        var completion = new TaskCompletionSource<XamlRoot>(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            if (ContentFrame.XamlRoot is not { } loadedRoot)
                return;
            ContentFrame.Loaded -= loaded;
            completion.TrySetResult(loadedRoot);
        };
        ContentFrame.Loaded += loaded;

        // Close the race where the root is attached between the initial check
        // and subscribing to Loaded.
        if (ContentFrame.XamlRoot is { } attachedRoot)
        {
            ContentFrame.Loaded -= loaded;
            completion.TrySetResult(attachedRoot);
        }

        return completion.Task;
    }

    private void NavigateToPage(string tag)
    {
        Type pageType = tag switch
        {
            "Tiling" => typeof(TilingPage),
            "Rules" => typeof(RulesPage),
            "Appearance" => typeof(AppearancePage),
            "Hotkeys" => typeof(HotkeysPage),
            _ => typeof(GeneralPage)
        };

        if (ContentFrame.CurrentSourcePageType == pageType)
            return;

        BreadcrumbTitle.Text = tag switch
        {
            "Tiling" => "Scrolling Window Manager",
            "Rules" => "Window Routing Rules",
            "Appearance" => "Appearance & Themes",
            "Hotkeys" => "Keyboard Shortcuts & Hotkeys",
            _ => "General Settings"
        };

        ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
    }

    private void ContentFrame_NavigationFailed(object sender, Microsoft.UI.Xaml.Navigation.NavigationFailedEventArgs e)
    {
        e.Handled = true;
        BreadcrumbTitle.Text = "Page could not be opened";
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Monolith", "monolith.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] Navigation failed for {e.SourcePageType?.FullName}: {e.Exception}\n");
        }
        catch { }
    }
}

