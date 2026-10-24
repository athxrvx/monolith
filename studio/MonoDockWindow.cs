using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Monolith.Studio.Services;
using Windows.Foundation;
using Windows.Graphics;
using Path = System.IO.Path;

namespace Monolith.Studio;

/// <summary>A centered, bottom-aligned application dock.</summary>
internal sealed class MonoDockWindow : Window
{
    private const int GwlStyle = -16;
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsExToolWindow = 0x80;
    private const int GwOwner = 4;
    private const int GwlExStyle = -20;
    private const long WsExToolWindowStyle = 0x80;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const uint WmClose = 0x0010;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtrEx(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] private static extern bool SetForegroundWindowNative(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly Grid _root = new();
    private readonly StackPanel _icons = new() { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _surface;
    private readonly DispatcherQueueTimer _runningTimer;
    private readonly List<DockItem> _items = new();
    private readonly List<UIElement> _dynamicItems = new();
    private readonly Button _addButton;
    private static List<InstalledShortcut>? _installedShortcuts;
    private string? _windowSnapshot;
    private AcrylicWindowBackdrop? _acrylic;
    private int _appliedIconSize = -1;
    private string? _appliedTargets;
    private bool? _isShown;

    public MonoDockWindow()
    {
        var itemsHost = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _addButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE710", FontSize = 22 },
            Width = 42, Height = 48, MinWidth = 42, MinHeight = 0,
            Padding = new Thickness(0), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 255, 255, 255)),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(_addButton, "Pin an app");
        _addButton.Flyout = CreatePinMenu();
        _icons.Children.Add(_addButton);
        itemsHost.Children.Add(_icons);
        _surface = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(184, 35, 35, 35)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(95, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(28),
            Padding = new Thickness(14, 9, 14, 9),
            Child = itemsHost,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _root.Children.Add(_surface);
        Content = _root;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));
        _appWindow.IsShownInSwitchers = false;
        _appWindow.Resize(new SizeInt32 { Width = 500, Height = 100 });
        var style = GetWindowLongPtr(_hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlStyle, new IntPtr((style & ~0x00C00000L & ~0x00040000L & ~0x00020000L) | WsPopup));
        var exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle | WsExToolWindow));
        _acrylic = AcrylicWindowBackdrop.Attach(this, 0.28);
        _runningTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _runningTimer.Interval = TimeSpan.FromMilliseconds(900);
        _runningTimer.IsRepeating = true;
        _runningTimer.Tick += (_, _) => RefreshDockWindows();
        _runningTimer.Start();
        Closed += (_, _) => { _runningTimer.Stop(); _acrylic?.Dispose(); _acrylic = null; };
        ApplyConfiguration();
    }

    public void ApplyConfiguration()
    {
        var config = App.MainViewModel.Config;
        var targets = config.MonodockPinnedApps ?? new List<string>();
        string targetSnapshot = string.Join("|", targets);
        int iconSize = Math.Clamp(config.MonodockIconSize, 40, 88);
        if (_appliedIconSize != iconSize || !string.Equals(_appliedTargets, targetSnapshot, StringComparison.Ordinal))
        {
            _appliedIconSize = iconSize;
            _appliedTargets = targetSnapshot;
            BuildItems(targets, iconSize);
        }

        bool shouldShow = config.MonolithEnabled && config.MonodockEnabled;
        if (shouldShow && _isShown != true)
        {
            PositionDock();
            _appWindow.Show();
            _isShown = true;
        }
        else if (!shouldShow && _isShown != false)
        {
            _appWindow.Hide();
            _isShown = false;
        }
        if (shouldShow) RefreshDockWindows();
    }

    private void BuildItems(IReadOnlyList<string> targets, int iconSize)
    {
        _icons.Children.Clear();
        _items.Clear();
        _dynamicItems.Clear();
        _windowSnapshot = null;
        foreach (string target in targets.Where(value => !string.IsNullOrWhiteSpace(value)).Take(20))
        {
            UIElement icon = CreateFluentAppIcon(target, iconSize);
            var indicator = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = Visibility.Collapsed
            };
            var buttonContent = new Grid { Width = iconSize, Height = iconSize + 10 };
            buttonContent.Children.Add(icon);
            buttonContent.Children.Add(indicator);
            var button = new Button
            {
                Content = buttonContent,
                Width = iconSize + 8,
                Height = iconSize + 10,
                MinWidth = iconSize + 8,
                MinHeight = 0,
                Padding = new Thickness(4, 0, 4, 0),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                VerticalAlignment = VerticalAlignment.Center
            };
            string label = GetLabel(target);
            ToolTipService.SetToolTip(button, label);
            button.Click += (_, _) => ActivatePinnedApp(target);
            button.ContextFlyout = CreatePinnedContextMenu(target);
            _icons.Children.Add(button);
            _items.Add(new DockItem(target, button, indicator));
        }
        _icons.Children.Add(_addButton);
        _surface.Width = double.NaN;
        _surface.Height = iconSize + 30;
        PositionDock();
    }

    private void PositionDock()
    {
        var bounds = DisplayArea.Primary?.WorkArea ?? new RectInt32(0, 0, 1920, 1080);
        double scale = GetDpiForWindow(_hwnd) / 96d;
        double contentWidth = _icons.Children.Sum(element =>
        {
            if (element is not FrameworkElement frameworkElement) return 0;
            double elementWidth = double.IsNaN(frameworkElement.Width) ? frameworkElement.DesiredSize.Width : frameworkElement.Width;
            return elementWidth + frameworkElement.Margin.Left + frameworkElement.Margin.Right;
        });
        contentWidth += Math.Max(0, _icons.Children.Count - 1) * _icons.Spacing;
        contentWidth += _surface.Padding.Left + _surface.Padding.Right + _surface.BorderThickness.Left + _surface.BorderThickness.Right;
        int width = Math.Max(150, (int)Math.Ceiling(contentWidth * scale));
        int height = (int)Math.Ceiling((_appliedIconSize + 30) * scale);
        _appWindow.Resize(new SizeInt32 { Width = width, Height = height });
        int x = bounds.X + Math.Max(0, (bounds.Width - width) / 2);
        int y = bounds.Y + bounds.Height - height - (int)Math.Round(12 * scale);
        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        SetWindowPos(_hwnd, HwndTopmost, x, y, width, height, SwpNoActivate);
    }

    private void RefreshDockWindows()
    {
        var windows = EnumerateWindows();
        foreach (var item in _items)
        {
            string processName = PinnedProcessName(item.Target);
            item.Indicator.Visibility = windows.Any(window => string.Equals(window.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                ? Visibility.Visible : Visibility.Collapsed;
        }

        string snapshot = string.Join("|", windows.Select(window => $"{window.Handle}:{window.Title}:{window.Minimized}"));
        if (string.Equals(snapshot, _windowSnapshot, StringComparison.Ordinal)) return;
        _windowSnapshot = snapshot;
        foreach (var element in _dynamicItems) _icons.Children.Remove(element);
        _dynamicItems.Clear();

        var pinnedNames = _items.Select(item => PinnedProcessName(item.Target)).Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unpinnedApps = windows.Where(window => !window.Minimized && !pinnedNames.Contains(window.ProcessName))
            .GroupBy(window => window.Executable, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).Take(8).ToArray();
        var minimizedWindows = windows.Where(window => window.Minimized).Take(8).ToArray();
        if (unpinnedApps.Length + minimizedWindows.Length == 0)
        {
            PositionDock();
            return;
        }

        var separator = new Border { Width = 1, Height = Math.Max(28, _appliedIconSize * 0.55), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 255, 255, 255)), Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
        AddDynamicItem(separator);
        foreach (var window in unpinnedApps)
            AddDynamicItem(CreateWindowButton(window, _appliedIconSize, isMinimizedTile: false));
        if (minimizedWindows.Length > 0)
        {
            var minimizedSeparator = new Border { Width = 1, Height = Math.Max(28, _appliedIconSize * 0.55), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 255, 255, 255)), Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
            AddDynamicItem(minimizedSeparator);
            foreach (var window in minimizedWindows)
                AddDynamicItem(CreateWindowButton(window, Math.Max(40, _appliedIconSize * 0.72), isMinimizedTile: true));
        }
        PositionDock();
    }

    private List<WindowInfo> EnumerateWindows()
    {
        var result = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == _hwnd || !IsWindowVisible(hwnd) || GetWindow(hwnd, GwOwner) != IntPtr.Zero) return true;
            if ((GetWindowLongPtrEx(hwnd, GwlExStyle).ToInt64() & WsExToolWindowStyle) != 0) return true;
            int titleLength = Math.Clamp(GetWindowTextLength(hwnd), 0, 1024);
            if (titleLength == 0) return true;
            var titleBuffer = new System.Text.StringBuilder(titleLength + 1);
            GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);
            string title = titleBuffer.ToString().Trim();
            if (title.Length == 0) return true;
            GetWindowThreadProcessId(hwnd, out int pid);
            if (pid == Environment.ProcessId) return true;
            string processName = string.Empty, executable = string.Empty;
            try
            {
                using var process = Process.GetProcessById(pid);
                processName = process.ProcessName;
                executable = process.MainModule?.FileName ?? processName + ".exe";
            }
            catch { return true; }
            result.Add(new WindowInfo(hwnd, title, processName, executable, IsIconic(hwnd), hwnd == GetForegroundWindow()));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private void AddDynamicItem(UIElement element)
    {
        _dynamicItems.Add(element);
        int index = Math.Max(0, _icons.Children.IndexOf(_addButton));
        _icons.Children.Insert(index, element);
    }

    private Button CreateWindowButton(WindowInfo window, double size, bool isMinimizedTile)
    {
        UIElement icon = CreateFluentAppIcon(window.Executable, size);
        if (isMinimizedTile)
            icon = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(10), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 255, 255, 255)), BorderThickness = new Thickness(1), Child = icon, Clip = null };
        var button = new Button
        {
            Content = icon, Width = size + 8, Height = size + 10, MinWidth = size + 8, MinHeight = 0,
            Padding = new Thickness(4, 0, 4, 0), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, window.Title);
        button.Click += (_, _) =>
        {
            if (isMinimizedTile) ActivateWindow(window.Handle);
            else if (window.IsForeground) ShowWindow(window.Handle, SwMinimize);
            else ActivateWindow(window.Handle);
        };
        button.ContextFlyout = CreateWindowContextMenu(window);
        return button;
    }

    private MenuFlyout CreatePinMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Pin an app…", Icon = new FontIcon { Glyph = "\uE718" }, IsEnabled = false });
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var app in GetInstalledShortcuts().Take(60))
        {
            var item = new MenuFlyoutItem { Text = app.Name, Icon = new FontIcon { Glyph = FluentGlyph(app.Name) }, Tag = app.Path };
            item.Click += (_, _) => PinApp(app.Path);
            menu.Items.Add(item);
        }
        return menu;
    }

    private MenuFlyout CreatePinnedContextMenu(string target)
    {
        var menu = new MenuFlyout();
        var unpin = new MenuFlyoutItem { Text = "Unpin from dock", Icon = new FontIcon { Glyph = "\uE77A" } };
        unpin.Click += (_, _) => UnpinApp(target);
        menu.Items.Add(unpin);
        var launch = new MenuFlyoutItem { Text = "Open", Icon = new FontIcon { Glyph = "\uE768" } };
        launch.Click += (_, _) => Launch(target);
        menu.Items.Add(launch);
        return menu;
    }

    private MenuFlyout CreateWindowContextMenu(WindowInfo window)
    {
        var menu = new MenuFlyout();
        if (!_items.Any(item => string.Equals(PinnedProcessName(item.Target), window.ProcessName, StringComparison.OrdinalIgnoreCase)))
        {
            var pin = new MenuFlyoutItem { Text = "Pin app to dock", Icon = new FontIcon { Glyph = "\uE718" } };
            pin.Click += (_, _) => PinApp(window.Executable);
            menu.Items.Add(pin);
        }
        var minimize = new MenuFlyoutItem { Text = window.Minimized ? "Restore window" : "Minimize to dock", Icon = new FontIcon { Glyph = window.Minimized ? "\uE740" : "\uE921" } };
        minimize.Click += (_, _) => { if (window.Minimized) ActivateWindow(window.Handle); else ShowWindow(window.Handle, SwMinimize); };
        menu.Items.Add(minimize);
        menu.Items.Add(new MenuFlyoutSeparator());
        var close = new MenuFlyoutItem { Text = "Close window", Icon = new FontIcon { Glyph = "\uE8BB" } };
        close.Click += (_, _) => PostWindowMessage(window.Handle, WmClose, IntPtr.Zero, IntPtr.Zero);
        menu.Items.Add(close);
        return menu;
    }

    private void PinApp(string target)
    {
        var pins = App.MainViewModel.Config.MonodockPinnedApps ??= new List<string>();
        if (pins.Any(pin => string.Equals(pin, target, StringComparison.OrdinalIgnoreCase))) return;
        pins.Add(target);
        _ = App.MainViewModel.SaveConfigAsync();
        App.MainWindowInstance?.ApplyMonoDockConfiguration();
    }

    private void UnpinApp(string target)
    {
        App.MainViewModel.Config.MonodockPinnedApps?.RemoveAll(pin => string.Equals(pin, target, StringComparison.OrdinalIgnoreCase));
        _ = App.MainViewModel.SaveConfigAsync();
        App.MainWindowInstance?.ApplyMonoDockConfiguration();
    }

    private void ActivatePinnedApp(string target)
    {
        string processName = PinnedProcessName(target);
        var appWindows = EnumerateWindows().Where(window => string.Equals(window.ProcessName, processName, StringComparison.OrdinalIgnoreCase)).ToArray();
        var minimized = appWindows.FirstOrDefault(window => window.Minimized);
        if (minimized is not null) { ActivateWindow(minimized.Handle); return; }
        var active = appWindows.FirstOrDefault(window => window.IsForeground);
        if (active is not null) { ShowWindow(active.Handle, SwMinimize); return; }
        if (appWindows.Length > 0) { ActivateWindow(appWindows[0].Handle); return; }
        Launch(target);
    }

    private static void ActivateWindow(IntPtr hwnd)
    {
        ShowWindow(hwnd, SwRestore);
        SetForegroundWindowNative(hwnd);
    }

    private string PinnedProcessName(string target)
    {
        if (Path.GetExtension(target).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var app = GetInstalledShortcuts().FirstOrDefault(item => string.Equals(item.Path, target, StringComparison.OrdinalIgnoreCase));
            if (app is not null) return Path.GetFileNameWithoutExtension(app.Executable);
        }
        return Path.GetFileNameWithoutExtension(target);
    }

    private static List<InstalledShortcut> GetInstalledShortcuts()
    {
        if (_installedShortcuts is not null) return _installedShortcuts;
        var result = new List<InstalledShortcut>();
        object? shellObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return _installedShortcuts = result;
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null) return _installedShortcuts = result;
            dynamic shell = shellObject;
            var roots = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs") };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string link in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).Take(2000))
                {
                    object? shortcutObject = null;
                    try
                    {
                        shortcutObject = shell.CreateShortcut(link);
                        dynamic shortcut = shortcutObject;
                        string executable = Environment.ExpandEnvironmentVariables(Convert.ToString(shortcut.TargetPath) ?? string.Empty).Trim().Trim('"');
                        if (executable.Length == 0) continue;
                        string name = Path.GetFileNameWithoutExtension(link);
                        if (name.StartsWith("Uninstall ", StringComparison.OrdinalIgnoreCase)) continue;
                        result.Add(new InstalledShortcut(name, link, executable));
                    }
                    catch { }
                    finally { if (shortcutObject is not null && Marshal.IsComObject(shortcutObject)) Marshal.FinalReleaseComObject(shortcutObject); }
                }
            }
        }
        catch { }
        finally { if (shellObject is not null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject); }
        return _installedShortcuts = result.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).DistinctBy(app => app.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static UIElement CreateFluentAppIcon(string target, double size)
    {
        string iconFile = FluentIconFile(target);
        return new Image
        {
            Width = size,
            Height = size,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            Source = new SvgImageSource(new Uri($"ms-appx:///Assets/MonoDockFluentIcons/{iconFile}"))
        };
    }

    private static string FluentIconFile(string target)
    {
        string key = Path.GetFileNameWithoutExtension(target).ToLowerInvariant();
        if (key.Contains("explorer") || key.Contains("folder") || key.Contains("file-manager")) return "file-manager.svg";
        if (key.Contains("edge")) return "microsoft-edge.svg";
        if (key.Contains("chrome")) return "google-chrome.svg";
        if (key.Contains("chromium")) return "chromium.svg";
        if (key.Contains("firefox")) return "firefox.svg";
        if (key.Contains("notepad") || key.Contains("editor")) return "text-editor.svg";
        if (key.Contains("terminal") || key == "wt" || key.Contains("powershell") || key == "cmd") return "terminal.svg";
        if (key.Contains("calculator") || key == "calc") return "org.kde.plasma.calculator.svg";
        if (key.Contains("outlook")) return "ms-outlook.svg";
        if (key.Contains("mail")) return "internet-mail.svg";
        if (key.Contains("photo") || key.Contains("image")) return "accessories-image-viewer.svg";
        if (key.Contains("calendar")) return "ms-calendar.svg";
        if (key.Contains("music")) return "gnome-music.svg";
        if (key.Contains("setting")) return "systemsettings.svg";
        if (key.Contains("code") || key.Contains("visualstudio")) return "visualstudiocode.svg";
        return "application-default-icon.svg";
    }

    private static string FluentGlyph(string target)
    {
        string key = Path.GetFileNameWithoutExtension(target).ToLowerInvariant();
        if (key.Contains("explorer") || key.Contains("folder")) return "\uE8B7";
        if (key.Contains("edge") || key.Contains("chrome") || key.Contains("firefox") || key.Contains("browser")) return "\uE774";
        if (key.Contains("notepad") || key.Contains("editor")) return "\uE70F";
        if (key.Contains("terminal") || key == "wt" || key.Contains("powershell") || key == "cmd") return "\uE756";
        if (key.Contains("calculator")) return "\uE8EF";
        if (key.Contains("mail") || key.Contains("outlook")) return "\uE715";
        if (key.Contains("photo")) return "\uEB9F";
        if (key.Contains("music")) return "\uE8D6";
        if (key.Contains("calendar")) return "\uE787";
        if (key.Contains("setting")) return "\uE713";
        return "\uE7F4";
    }

    private static void Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) { App.LogStartup($"Could not launch MonoDock app '{target}'", ex); }
    }

    private static string GetLabel(string target)
    {
        if (target.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            return target["shell:AppsFolder\\".Length..];
        return Path.GetFileNameWithoutExtension(target);
    }

    private sealed record DockItem(string Target, Button Button, Ellipse Indicator);
    private sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName, string Executable, bool Minimized, bool IsForeground);
    private sealed record InstalledShortcut(string Name, string Path, string Executable);
    private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr parameter);
}
