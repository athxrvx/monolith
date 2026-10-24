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
    private readonly MonobarWindow _monobarWindow;
    private readonly MonoDockWindow _monoDockWindow;
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
        var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        int windowWidth = Math.Min(1320, workArea.Width);
        int windowHeight = Math.Min(860, workArea.Height);
        appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = windowWidth, Height = windowHeight });
        appWindow.Move(new Windows.Graphics.PointInt32
        {
            X = workArea.X + Math.Max(0, (workArea.Width - windowWidth) / 2),
            Y = workArea.Y + Math.Max(0, (workArea.Height - windowHeight) / 2)
        });
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "monolith_icon.ico");
        if (File.Exists(iconPath))
            appWindow.SetIcon(iconPath);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _launcherWindow = new LauncherWindow();
        _launcherWindow.RequestOpenSettings = OpenSettingsPage;
        App.MainWindowInstance = this;
        _monobarWindow = new MonobarWindow();
        _monoDockWindow = new MonoDockWindow();
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
            TaskbarVisibilityService.Restore();
            App.RefreshHotkeys = null;
            App.SetHotkeysEnabled = null;
            App.MainWindowInstance = null;
            try { _workspaceHotkeys.Dispose(); }
            catch (Exception ex) { App.LogStartup("Failed to dispose workspace hotkeys", ex); }
            try { _launcherWindow.Close(); }
            catch (Exception ex) { App.LogStartup("Failed to close launcher window", ex); }
            try { _monobarWindow.Close(); }
            catch (Exception ex) { App.LogStartup("Failed to close Monobar", ex); }
            try { _monoDockWindow.Close(); }
            catch (Exception ex) { App.LogStartup("Failed to close MonoDock", ex); }
            try { _trayIconService?.Dispose(); }
            catch (Exception ex) { App.LogStartup("Failed to remove the tray icon", ex); }
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

    private void OpenSettingsPage(string page)
    {
        foreach (var candidate in NavView.MenuItems)
        {
            if (candidate is NavigationViewItem item
                && string.Equals(item.Tag?.ToString(), page, StringComparison.OrdinalIgnoreCase))
            {
                NavView.SelectedItem = item;
                break;
            }
        }
        NavigateToPage(page);
        ShowSettingsWindow();
    }

    public void ApplyLauncherAppearance() => _launcherWindow.ApplyAppearance();

    public void ApplyMonobarConfiguration() => _monobarWindow.ApplyConfiguration();

    public void ApplyMonoDockConfiguration() => _monoDockWindow.ApplyConfiguration();

    public void RefreshMonobarState() => _monobarWindow.RefreshModules();

    public void ToggleLauncher() => ShowLauncher();

    public void ShowMonolithSettings(string page) => OpenSettingsPage(page);

    private void ShowLauncher()
    {
        if (App.MainViewModel.Config.MonolithEnabled && App.MainViewModel.Config.LauncherEnabled)
            _launcherWindow.TogglePalette();
    }

    public void SetLauncherAvailable(bool enabled)
    {
        ApplyLauncherAppearance();
        if (!enabled)
            _launcherWindow.HidePalette();
    }

    private void ExitCompletely()
    {
        if (_allowWindowClose) return;
        _allowWindowClose = true;
        TaskbarVisibilityService.Restore();
        try { App.MainViewModel.StopEngine(); }
        catch (Exception ex) { App.LogStartup("Failed to stop engine during complete exit", ex); }
        try { Close(); }
        catch (Exception ex) { App.LogStartup("Failed to close settings window during complete exit", ex); }
        finally { App.Current.Exit(); }
    }

    private void OnHotkeyPressed(Monolith.Studio.Models.HotkeyBinding binding)
    {
        if (!App.MainViewModel.Config.MonolithEnabled)
        {
            App.LogStartup($"Ignored hotkey '{binding.Binding}' because Monolith is disabled");
            return;
        }
        string action = binding.Action.Trim();
        App.LogStartup($"Routing hotkey action: {action}");
        if (action.Equals("Toggle Launcher", StringComparison.OrdinalIgnoreCase))
        {
            ShowLauncher();
        }
        else if (action.StartsWith("Focus ", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.FocusDirectionAsync(action[6..].ToLowerInvariant());
        }
        else if (action.StartsWith("Move Window ", StringComparison.OrdinalIgnoreCase)
            && action is "Move Window Left" or "Move Window Right" or "Move Window Up" or "Move Window Down")
        {
            _ = App.MainViewModel.MoveWindowDirectionAsync(action[12..].ToLowerInvariant());
        }
        else if (action.Equals("Next Workspace", StringComparison.OrdinalIgnoreCase)
            || action.Equals("Next Desktop", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.NavigateWorkspaceAsync(1);
        }
        else if (action.Equals("Previous Workspace", StringComparison.OrdinalIgnoreCase)
            || action.Equals("Previous Desktop", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.NavigateWorkspaceAsync(-1);
        }
        else if (action.Equals("Move Window to Next Workspace", StringComparison.OrdinalIgnoreCase)
            || action.Equals("Move Window to Next Desktop", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.MoveActiveWindowWorkspaceAsync(1);
        }
        else if (action.Equals("Move Window to Previous Workspace", StringComparison.OrdinalIgnoreCase)
            || action.Equals("Move Window to Previous Desktop", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainViewModel.MoveActiveWindowWorkspaceAsync(-1);
        }
        else if (action.StartsWith("Move Window to Workspace ", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(action[25..], out int targetWorkspace))
        {
            _ = App.MainViewModel.MoveActiveWindowToWorkspaceAsync(targetWorkspace);
        }
        else if (action.StartsWith("Switch to Workspace ", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(action[20..], out int workspace))
        {
            _ = App.MainViewModel.SwitchWorkspaceAsync(workspace);
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
            Header = "Run Monolith when I sign in",
            OnContent = "On",
            OffContent = "Off",
            IsOn = App.MainViewModel.Config.EngineStartup
        };
        var launcherToggle = new ToggleSwitch
        {
            Header = "Enable the command launcher",
            OnContent = "On",
            OffContent = "Off",
            IsOn = App.MainViewModel.Config.LauncherEnabled
        };
        var monobarToggle = new ToggleSwitch
        {
            Header = "Show the Monobar",
            OnContent = "On",
            OffContent = "Off",
            IsOn = App.MainViewModel.Config.MonobarEnabled
        };
        int step = 0;
        var dialog = new ContentDialog
        {
            Title = "Get started",
            PrimaryButtonText = "Continue",
            SecondaryButtonText = "Back",
            CloseButtonText = "Skip setup",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            MinWidth = 560,
            MaxWidth = 620
        };
        void ShowStep()
        {
            dialog.Content = new ScrollViewer
            {
                Content = CreateOnboardingStep(step, startupToggle, launcherToggle, monobarToggle),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 560
            };
            dialog.PrimaryButtonText = step == 2 ? "Finish setup" : "Continue";
            dialog.IsSecondaryButtonEnabled = step > 0;
        }
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (step < 2)
            {
                args.Cancel = true;
                step++;
                ShowStep();
            }
        };
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            step = Math.Max(0, step - 1);
            ShowStep();
        };
        ShowStep();
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            App.MainViewModel.Config.EngineStartup = startupToggle.IsOn;
            App.MainViewModel.Config.LauncherEnabled = launcherToggle.IsOn;
            App.MainViewModel.Config.MonobarEnabled = monobarToggle.IsOn;
            App.MainViewModel.Config.OnboardingCompleted = true;
            await App.MainViewModel.SaveConfigAsync();
            SetLauncherAvailable(App.MainViewModel.Config.MonolithEnabled && launcherToggle.IsOn);
            ApplyMonobarConfiguration();
        }
        else if (result == ContentDialogResult.None)
        {
            App.MainViewModel.Config.OnboardingCompleted = true;
            await App.MainViewModel.SaveConfigAsync();
        }

    }

    private static UIElement CreateOnboardingStep(int step, ToggleSwitch startupToggle, ToggleSwitch launcherToggle, ToggleSwitch monobarToggle)
    {
        var content = new StackPanel { Spacing = 16, Width = 500, Margin = new Thickness(4, 6, 4, 2) };
        var stepLabels = new[] { "THE IDEA", "THE CONTROLS", "YOUR SETUP" };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(12),
            Background = OnboardingAccentBrush(34),
            Child = new TextBlock
            {
                Text = "M",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = OnboardingAccentBrush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var brandText = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock { Text = "MONOLITH", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold, CharacterSpacing = 90 });
        brandText.Children.Add(new TextBlock { Text = "SCROLLING WINDOW MANAGER", FontSize = 9, Opacity = 0.58, CharacterSpacing = 45 });
        brand.Children.Add(brandText);
        content.Children.Add(brand);
        var heading = new TextBlock
        {
            Text = step switch
            {
                0 => "A workspace that keeps moving.",
                1 => "Move around without reaching for the mouse.",
                _ => "Make Monolith feel like yours."
            },
            FontSize = 25,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        content.Children.Add(new TextBlock
        {
            Text = $"GETTING STARTED     {step + 1:00} / 03     ·     {stepLabels[step]}",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 90,
            Foreground = OnboardingAccentBrush()
        });
        content.Children.Add(heading);

        if (step == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Monolith is a scrolling window manager. Each workspace is a row of windows: move left or right to travel along it, and stack related windows vertically when you need more room.",
                FontSize = 14,
                LineHeight = 21,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78
            });
            var workspace = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(34, 255, 255, 255)),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14)
            };
            var columns = new Grid { ColumnSpacing = 8, Height = 112 };
            for (int i = 0; i < 3; i++) columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.Children.Add(CreateWorkspacePreviewCard("ACTIVE WINDOW", true, 0));
            columns.Children.Add(CreateWorkspacePreviewCard("NEXT WINDOW", false, 1));
            columns.Children.Add(CreateWorkspacePreviewCard("MORE WINDOWS", false, 2));
            workspace.Child = columns;
            content.Children.Add(workspace);
            content.Children.Add(new TextBlock
            {
                Text = "Think of it as one wide desk. Monolith keeps the focused window in view while the rest of the workspace stays a shortcut away.",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72
            });
        }
        else if (step == 1)
        {
            content.Children.Add(new TextBlock
            {
                Text = "These are the default shortcuts. You can change every binding later in Settings → Hotkeys.",
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78
            });
            var shortcuts = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
            shortcuts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shortcuts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shortcuts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shortcuts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shortcuts.Children.Add(CreateShortcutCard("FOCUS WINDOWS", "Alt + H / L", "Move left or right", 0, 0));
            shortcuts.Children.Add(CreateShortcutCard("STACK WINDOWS", "Alt + K / J", "Move up or down", 1, 0));
            shortcuts.Children.Add(CreateShortcutCard("SWITCH WORKSPACES", "Alt + 1 … 9", "Page Up / Page Down to step", 0, 1));
            shortcuts.Children.Add(CreateShortcutCard("OPEN THE LAUNCHER", "Alt + Space", "Find and open an app", 1, 1));
            content.Children.Add(shortcuts);
            var moveHint = new Border
            {
                Background = OnboardingAccentBrush(24),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Child = new TextBlock
                {
                    Text = "Tip: hold Alt + Shift while using H / J / K / L to move the focused window.",
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            content.Children.Add(moveHint);
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = "Choose what should be ready when you sign in. These options are always available later in Settings.",
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78
            });
            content.Children.Add(CreateOnboardingSettingCard("STARTUP", "Keep your workspace ready", "Launch the engine automatically when Monolith starts.", startupToggle));
            content.Children.Add(CreateOnboardingSettingCard("LAUNCHER", "Find apps quickly", "Use Alt + Space to open the command launcher.", launcherToggle));
            content.Children.Add(CreateOnboardingSettingCard("MONOBAR", "Keep workspace status nearby", "Show the configurable bar across the top of your screen.", monobarToggle));
            content.Children.Add(new TextBlock
            {
                Text = "You can change these choices later in General and Monobar settings.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.62,
                Margin = new Thickness(2, 0, 0, 0)
            });
        }

        var progress = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 3, 0, 0) };
        for (int i = 0; i < 3; i++)
        {
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var segment = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(2),
                Background = OnboardingAccentBrush(i <= step ? (byte)255 : (byte)46)
            };
            Grid.SetColumn(segment, i);
            progress.Children.Add(segment);
        }
        content.Children.Add(progress);
        return content;
    }

    private static UIElement CreateWorkspacePreviewCard(string label, bool active, int column)
    {
        var card = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(active
                ? WithAccentOpacity(56)
                : Windows.UI.Color.FromArgb(28, 255, 255, 255)),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(active
                ? WithAccentOpacity(120)
                : Windows.UI.Color.FromArgb(32, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new Border
                    {
                        Height = 45,
                        CornerRadius = new CornerRadius(6),
                        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(30, 255, 255, 255)),
                        BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(34, 255, 255, 255)),
                        BorderThickness = new Thickness(1)
                    },
                    new TextBlock
                    {
                        Text = label,
                        FontSize = 9,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        CharacterSpacing = 40,
                        Opacity = active ? 1 : 0.62,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            }
        };
        Grid.SetColumn(card, column);
        return card;
    }

    private static UIElement CreateShortcutCard(string title, string keys, string description, int column, int row)
    {
        var card = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(28, 255, 255, 255)),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(28, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 35, Opacity = 0.62 },
                    new TextBlock { Text = keys, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = OnboardingAccentBrush() },
                    new TextBlock { Text = description, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        Grid.SetColumn(card, column);
        Grid.SetRow(card, row);
        return card;
    }

    private static UIElement CreateOnboardingSettingCard(string eyebrow, string title, string description, ToggleSwitch toggle)
    {
        var text = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = eyebrow, FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 45, Foreground = OnboardingAccentBrush() },
                new TextBlock { Text = title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                new TextBlock { Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.68 }
            }
        };
        var row = new Grid { ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        row.Children.Add(toggle);
        Grid.SetColumn(toggle, 1);
        return new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(28, 255, 255, 255)),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(28, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(12, 10, 10, 10),
            Child = row
        };
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush OnboardingAccentBrush(byte opacity = 255) =>
        new(WithAccentOpacity(opacity));

    private static Windows.UI.Color WithAccentOpacity(byte opacity)
    {
        var accent = AccentColorService.GetAccentColor();
        return Windows.UI.Color.FromArgb(opacity, accent.R, accent.G, accent.B);
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
            "Monobar" => typeof(MonobarPage),
            "MonoLauncher" => typeof(MonoLauncherPage),
            "MonoDock" => typeof(MonoDockPage),
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
            "Monobar" => "Monobar",
            "MonoLauncher" => "MonoLauncher",
            "MonoDock" => "MonoDock",
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


