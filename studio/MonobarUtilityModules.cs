using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Monolith.Studio;

internal sealed class TaskViewMonobarModule : MonobarModuleBase
{
    public TaskViewMonobarModule(MainWindow owner) : base(owner) => Element = IconButton("\uE7C4", "Task view", (_, _) => SendWindowsShortcut(0x09));
    public override string Id => "task-view";
    public override UIElement Element { get; }
    private static void SendWindowsShortcut(ushort key) => NativeShortcut.Send(key);
}

internal sealed class QuickSettingsMonobarModule : MonobarModuleBase
{
    public QuickSettingsMonobarModule(MainWindow owner) : base(owner) =>
        Element = IconButton("\uE713", "Open Windows Quick Settings", (_, _) => NativeShortcut.Send(0x41));
    public override string Id => "quick-settings";
    public override UIElement Element { get; }
}

internal sealed class ShowDesktopMonobarModule : MonobarModuleBase
{
    public ShowDesktopMonobarModule(MainWindow owner) : base(owner) => Element = IconButton("\uE80F", "Show desktop", (_, _) => NativeShortcut.Send(0x44));
    public override string Id => "show-desktop";
    public override UIElement Element { get; }
}

internal sealed class RecycleBinMonobarModule : MonobarModuleBase
{
    public RecycleBinMonobarModule(MainWindow owner) : base(owner) => Element = IconButton("\uE74D", "Open Recycle Bin", (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true }));
    public override string Id => "recycle-bin";
    public override UIElement Element { get; }
}

internal sealed class SystemMonitorMonobarModule : MonobarModuleBase
{
    private readonly TextBlock _readout = new() { FontSize = 13 };
    private readonly TextBlock _barReadout = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly MonobarPopupWindow _popup;
    private long _previousIdle, _previousKernel, _previousUser;
    private bool _hasCpuSample;

    public SystemMonitorMonobarModule(MainWindow owner) : base(owner)
    {
        var content = new StackPanel { Width = 270, Spacing = 12 };
        content.Children.Add(MonobarPopupContent.Header("\uE9D9", "System monitor", "Current usage"));
        var card = MonobarPopupContent.Card();
        _readout.FontSize = 13;
        card.Child = _readout;
        content.Children.Add(card);
        _popup = new MonobarPopupWindow(MonobarPopupContent.PopupSurface(content, 310, 0), 310, 0);
        var button = new Button
        {
            Height = ModuleHeight, MinHeight = 0, MaxHeight = ModuleHeight,
            Padding = new Thickness(8, 0, 8, 0), Background = Brush(22), BorderBrush = Brush(20), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7), Content = _barReadout, VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, "CPU and memory usage");
        button.Click += (_, _) => ShowPopup();
        Element = button;
        Refresh();
    }

    public override string Id => "system-monitor";
    public override UIElement Element { get; }
    public override void Refresh()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        string ram = "RAM --";
        if (GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0)
        {
            ulong used = memory.TotalPhysical - memory.AvailablePhysical;
            ram = $"RAM {used / 1073741824d:0.0}/{memory.TotalPhysical / 1073741824d:0.0} GB";
        }
        string cpu = GetCpuPercent() is int value ? $"CPU {value}%" : "CPU --";
        _readout.Text = $"{cpu}\n{ram}";
        _barReadout.Text = $"{cpu} · {ram}";
    }
    public override void Dispose() { _popup.Close(); }
    private void ShowPopup()
    {
        _popup.ShowBelow(Element);
    }

    private int? GetCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        long idleNow = FileTimeValue(idle), kernelNow = FileTimeValue(kernel), userNow = FileTimeValue(user);
        long idleDelta = idleNow - _previousIdle;
        long totalDelta = (kernelNow - _previousKernel) + (userNow - _previousUser);
        _previousIdle = idleNow; _previousKernel = kernelNow; _previousUser = userNow;
        if (!_hasCpuSample || totalDelta <= 0) { _hasCpuSample = true; return null; }
        return Math.Clamp((int)Math.Round(100d * (totalDelta - idleDelta) / totalDelta), 0, 100);
    }

    private static long FileTimeValue(System.Runtime.InteropServices.ComTypes.FILETIME value) =>
        ((long)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME idle, out System.Runtime.InteropServices.ComTypes.FILETIME kernel, out System.Runtime.InteropServices.ComTypes.FILETIME user);
}

internal static class NativeShortcut
{
    private const uint KeyEvent = 0x0001, KeyUp = 0x0002;
    private const ushort VkLwin = 0x5B;
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort VirtualKey, ScanCode; public uint Flags, Time; public IntPtr ExtraInfo; }

    public static void Send(ushort key)
    {
        var inputs = new[]
        {
            Key(VkLwin, 0), Key(key, 0), Key(key, KeyUp), Key(VkLwin, KeyUp)
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }
    private static Input Key(ushort key, uint flags) => new() { Type = KeyEvent, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = flags } } };
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
