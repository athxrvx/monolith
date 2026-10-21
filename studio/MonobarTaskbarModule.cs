using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Monolith.Studio.Services;

namespace Monolith.Studio;

/// <summary>An icon-only strip of open top-level application windows.</summary>
internal sealed class TaskbarMonobarModule : MonobarModuleBase
{
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer _scroll;
    private readonly int _ownPid = Environment.ProcessId;
    private IntPtr[] _lastWindows = Array.Empty<IntPtr>();
    private DateTime _lastRefresh = DateTime.MinValue;

    public TaskbarMonobarModule(MainWindow owner) : base(owner)
    {
        _scroll = new ScrollViewer
        {
            Content = _buttons,
            Width = 420,
            Height = ModuleHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            ZoomMode = ZoomMode.Disabled
        };
        Element = new Border
        {
            Height = ModuleHeight,
            MaxWidth = 430,
            Padding = new Thickness(3, 0, 3, 0),
            Background = Brush(18),
            CornerRadius = new CornerRadius(8),
            Child = _scroll,
            VerticalAlignment = VerticalAlignment.Center
        };
        Refresh();
    }

    public override string Id => "taskbar";
    public override UIElement Element { get; }

    public override void Refresh()
    {
        if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromMilliseconds(900)) return;
        _lastRefresh = DateTime.UtcNow;
        var windows = EnumerateApplicationWindows();
        var handles = new IntPtr[windows.Count];
        for (int i = 0; i < windows.Count; i++) handles[i] = windows[i].Handle;
        if (handles.AsSpan().SequenceEqual(_lastWindows))
        {
            foreach (var item in windows)
                if (_buttons.Children[item.Index] is Button button) ToolTipService.SetToolTip(button, item.Title);
            return;
        }

        _lastWindows = handles;
        _buttons.Children.Clear();
        foreach (var item in windows)
        {
            IntPtr windowHandle = item.Handle;
            var button = new Button
            {
                Width = 30,
                Height = ModuleHeight,
                MinWidth = 30,
                MinHeight = 0,
                Padding = new Thickness(0),
                Background = Brush(22),
                BorderBrush = Brush(18),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Content = CreateIcon(item.Executable)
            };
            ToolTipService.SetToolTip(button, item.Title);
            button.Click += (_, _) => Activate(windowHandle);
            _buttons.Children.Add(button);
        }
    }

    private static UIElement CreateIcon(string executable)
    {
        try
        {
            var path = ShellIconCache.GetIconPath(executable);
            if (!string.IsNullOrEmpty(path))
                return new Image { Source = new BitmapImage(new Uri(path)), Width = 18, Height = 18, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
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
            int pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == _ownPid) return true;
            var title = new StringBuilder(length + 1);
            GetWindowText(hwnd, title, title.Capacity);
            if (string.IsNullOrWhiteSpace(title.ToString())) return true;

            string executable = string.Empty;
            try
            {
                using var process = Process.GetProcessById(pid);
                executable = process.MainModule?.FileName ?? process.ProcessName + ".exe";
            }
            catch { }
            result.Add(new AppWindowInfo(hwnd, title.ToString(), executable, result.Count));
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
    private readonly record struct AppWindowInfo(IntPtr Handle, string Title, string Executable, int Index);

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
