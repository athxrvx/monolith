using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Monolith.Studio.Services;

namespace Monolith.Studio;

/// <summary>Shows one stable icon per running application, grouping its windows together.</summary>
internal sealed class TaskbarMonobarModule : MonobarModuleBase
{
    private const int MaxVisibleApps = 7;
    private const double IconButtonWidth = 30;
    private const double IconSpacing = 3;
    private static readonly HashSet<string> NonTaskbarProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "TextInputHost",
        "WindowsInputExperience",
        "InputApp",
        "TabTip"
    };
    private readonly StackPanel _buttons = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = IconSpacing,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly ScrollViewer _scroll;
    private readonly Border _container;
    private readonly int _ownPid = Environment.ProcessId;
    private readonly Dictionary<string, AppGroupState> _groups = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastRefresh = DateTime.MinValue;

    public TaskbarMonobarModule(MainWindow owner) : base(owner)
    {
        _scroll = new ScrollViewer
        {
            Content = _buttons,
            Width = 0,
            Height = ModuleHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            ZoomMode = ZoomMode.Disabled
        };
        _container = new Border
        {
            Height = ModuleHeight,
            Width = 0,
            Padding = new Thickness(0),
            Background = Brush(18),
            CornerRadius = new CornerRadius(8),
            Child = _scroll,
            VerticalAlignment = VerticalAlignment.Center
        };
        Element = _container;
        Refresh();
    }

    public override string Id => "taskbar";
    public override UIElement Element { get; }

    public override void Refresh()
    {
        if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromMilliseconds(900)) return;
        _lastRefresh = DateTime.UtcNow;
        var groups = EnumerateApplicationWindows()
            .GroupBy(item => GroupKey(item.Executable), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => new AppWindowGroup(group.First().Executable, group.ToList()),
                StringComparer.OrdinalIgnoreCase);

        foreach (string removedKey in _groups.Keys.Where(key => !groups.ContainsKey(key)).ToArray())
        {
            _buttons.Children.Remove(_groups[removedKey].Button);
            _groups.Remove(removedKey);
        }

        foreach (var (key, group) in groups)
        {
            if (!_groups.TryGetValue(key, out var state))
            {
                state = new AppGroupState
                {
                    Button = new Button
                    {
                        Width = 30, Height = ModuleHeight, MinWidth = 30, MinHeight = 0,
                        Padding = new Thickness(0), Background = Brush(22), BorderBrush = Brush(18),
                        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
                        Content = CreateIcon(group.Executable)
                    },
                    Windows = group.Windows
                };
                _groups.Add(key, state);
                _buttons.Children.Add(state.Button);
                string capturedKey = key;
                state.Button.Click += (_, _) =>
                {
                    if (_groups.TryGetValue(capturedKey, out var current) && current.Windows.Count > 0)
                        Activate(current.Windows[0].Handle);
                };
            }
            else
            {
                state.Windows = group.Windows;
            }

            string appName = Path.GetFileNameWithoutExtension(group.Executable);
            string titles = string.Join("\n", group.Windows.Select(window => window.Title).Where(title => !string.IsNullOrWhiteSpace(title)).Distinct());
            ToolTipService.SetToolTip(state.Button, group.Windows.Count > 1
                ? $"{appName} ({group.Windows.Count} windows)\n{titles}"
                : $"{appName}\n{titles}");
        }

        int visibleCount = Math.Min(groups.Count, MaxVisibleApps);
        double contentWidth = visibleCount == 0
            ? 0
            : visibleCount * IconButtonWidth + (visibleCount - 1) * IconSpacing;
        _scroll.Width = contentWidth;
        _scroll.HorizontalScrollBarVisibility = groups.Count > MaxVisibleApps
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
        _container.Width = contentWidth == 0 ? 0 : contentWidth + 6;
        _container.Padding = contentWidth == 0 ? new Thickness(0) : new Thickness(3, 0, 3, 0);
    }

    private static string GroupKey(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return "unknown";
        try { return Path.GetFullPath(executable).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant(); }
        catch { return executable.ToUpperInvariant(); }
    }

    private static UIElement CreateIcon(string executable)
    {
        try
        {
            string? path = ShellIconCache.GetIconPath(executable);
            if (!string.IsNullOrEmpty(path))
                return new Image
                {
                    Source = new BitmapImage(new Uri(path)),
                    Width = 18,
                    Height = 18,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                };
        }
        catch { }
        return new FontIcon { Glyph = "\uE7F4", FontSize = 15 };
    }

    private List<AppWindowInfo> EnumerateApplicationWindows()
    {
        var result = new List<AppWindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GwOwner) != IntPtr.Zero) return true;
            long exStyle = GetWindowLongPtr(hwnd, GwlpExStyle).ToInt64();
            if ((exStyle & WsExToolWindow) != 0) return true;
            int length = GetWindowTextLength(hwnd);
            if (length <= 0) return true;
            GetWindowThreadProcessId(hwnd, out int pid);
            if (pid == _ownPid) return true;

            var title = new StringBuilder(length + 1);
            GetWindowText(hwnd, title, title.Capacity);
            string windowTitle = title.ToString();
            if (string.IsNullOrWhiteSpace(windowTitle)
                || windowTitle.Equals("Windows Input Experience", StringComparison.OrdinalIgnoreCase)) return true;

            string executable = string.Empty;
            string processName = string.Empty;
            try
            {
                using var process = Process.GetProcessById(pid);
                processName = process.ProcessName;
                executable = process.MainModule?.FileName ?? process.ProcessName + ".exe";
            }
            catch
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    processName = process.ProcessName;
                    executable = process.ProcessName + ".exe";
                }
                catch { }
            }
            if (NonTaskbarProcessNames.Contains(processName)) return true;
            result.Add(new AppWindowInfo(hwnd, windowTitle, executable));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static void Activate(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return;
        ShowWindow(hwnd, SwRestore);
        SetForegroundWindow(hwnd);
    }

    private const int GwOwner = 4;
    private const int GwlpExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const int SwRestore = 9;
    private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr lParam);
    private sealed class AppGroupState
    {
        public required Button Button { get; init; }
        public List<AppWindowInfo> Windows { get; set; } = new();
    }
    private sealed record AppWindowGroup(string Executable, List<AppWindowInfo> Windows);
    private readonly record struct AppWindowInfo(IntPtr Handle, string Title, string Executable);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
