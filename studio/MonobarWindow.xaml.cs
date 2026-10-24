using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Monolith.Studio.Services;
using Windows.Graphics;

namespace Monolith.Studio;

public sealed partial class MonobarWindow : Window
{
    private const int GwlExStyle = -20;
    private const int GwlStyle = -16;
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint AbmNew = 0x00000000;
    private const uint AbmRemove = 0x00000001;
    private const uint AbmQueryPos = 0x00000002;
    private const uint AbmSetPos = 0x00000003;
    private const uint AbeTop = 1;
    private const uint WmUser = 0x0400;
    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("shell32.dll", EntryPoint = "SHAppBarMessage")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rect;
        public IntPtr Param;
    }

    private readonly List<IMonobarModule> _modules = new();
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly IntPtr _hwnd;
    internal static IntPtr BarHandle { get; private set; }
    private bool _registeredAsAppBar;
    private bool? _barVisible;
    private int _appliedHeight = -1;
    private int _appliedOpacity = -1;
    private string? _appliedMaterial;
    private string? _appliedModuleConfiguration;
    private AcrylicWindowBackdrop? _acrylicBackdrop;

    public MonobarWindow()
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        BarHandle = _hwnd;
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32 { Width = 1200, Height = 32 });
        appWindow.IsShownInSwitchers = false;

        var style = GetWindowLongPtr(_hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlStyle, new IntPtr((style & ~0x00C00000L & ~0x00040000L & ~0x00020000L) | WsPopup));
        var exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle | WsExToolWindow | WsExNoActivate));

        RegisterAppBar();
        _refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(1000);
        _refreshTimer.IsRepeating = true;
        _refreshTimer.Tick += (_, _) => RefreshModules();
        _refreshTimer.Start();
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _acrylicBackdrop?.Dispose();
            _acrylicBackdrop = null;
            foreach (var module in _modules)
            {
                try { module.Dispose(); }
                catch (Exception ex) { App.LogStartup($"Failed to close Monobar module '{module.Id}'", ex); }
            }
            _modules.Clear();
            if (_registeredAsAppBar)
            {
                var data = CreateAppBarData();
                SHAppBarMessage(AbmRemove, ref data);
                _registeredAsAppBar = false;
            }
        };
        ApplyConfiguration();
    }

    public void ApplyConfiguration()
    {
        var config = App.MainViewModel.Config;
        var work = DisplayArea.Primary?.OuterBounds ?? new RectInt32(0, 0, 1920, 1080);
        int height = Math.Clamp(config.MonobarHeight, 28, 64);
        bool materialChanged = !string.Equals(_appliedMaterial, config.MonobarMaterial, StringComparison.Ordinal);
        bool opacityChanged = _appliedOpacity != config.MonobarOpacity;
        bool heightChanged = _appliedHeight != height;
        if (materialChanged) ApplyBackdrop(config.MonobarMaterial, config.MonobarOpacity);
        byte tintAlpha = (byte)Math.Clamp(10 + config.MonobarOpacity * 0.55, 20, 70);
        if (materialChanged || opacityChanged)
        {
            BarSurface.Background = config.MonobarMaterial switch
            {
                "Acrylic (Frosted Glass)" => new SolidColorBrush(Windows.UI.Color.FromArgb(0, 22, 22, 22)),
                "Pure OLED Dark" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0)),
                "Mica" or "Mica Alt" => new SolidColorBrush(Windows.UI.Color.FromArgb(38, 18, 18, 18)),
                _ => new SolidColorBrush(Windows.UI.Color.FromArgb(tintAlpha, 22, 22, 22))
            };
            if (config.MonobarMaterial == "Acrylic (Frosted Glass)")
                _acrylicBackdrop?.SetTintOpacity(config.MonobarOpacity / 250d);
            _appliedMaterial = config.MonobarMaterial;
            _appliedOpacity = config.MonobarOpacity;
        }

        string moduleConfiguration = string.Join("|", (config.MonobarModules ?? Models.MonobarModuleConfig.CreateDefaults())
            .Where(item => item.Enabled)
            .OrderBy(item => item.Order)
            .Select(item => $"{item.Id}:{item.Order}:{item.Alignment}:{item.WallpaperFolder}:{item.ClockUse24Hour}:{item.VisibilityCondition}"));
        if (!string.Equals(_appliedModuleConfiguration, moduleConfiguration, StringComparison.Ordinal))
        {
            foreach (var module in _modules)
            {
                try { module.Dispose(); }
                catch (Exception ex) { App.LogStartup($"Failed to close Monobar module '{module.Id}'", ex); }
            }
            _modules.Clear();
            LeftModules.Children.Clear();
            CenterModules.Children.Clear();
            RightModules.Children.Clear();
            foreach (var item in (config.MonobarModules ?? Models.MonobarModuleConfig.CreateDefaults()).Where(item => item.Enabled).OrderBy(item => item.Order))
            {
                try
                {
                    var module = MonobarModuleFactory.Create(item, App.MainWindowInstance!);
                    if (module is null) continue;
                    _modules.Add(module);
                    var host = item.Alignment switch
                    {
                        "Center" => CenterModules,
                        "Right" => RightModules,
                        _ => LeftModules
                    };
                    host.Children.Add(module.Element);
                }
                catch (Exception ex)
                {
                    App.LogStartup($"Failed to create Monobar module '{item.Id}'", ex);
                }
            }
            _appliedModuleConfiguration = moduleConfiguration;
            RefreshModules();
        }

        bool shouldShow = config.MonolithEnabled && config.MonobarEnabled;
        if (shouldShow && (_barVisible != true || heightChanged)) ShowBar();
        else if (!shouldShow && _barVisible != false) HideBar();
        _appliedHeight = height;
    }

    private void ApplyBackdrop(string material, int opacity)
    {
        _acrylicBackdrop?.Dispose();
        _acrylicBackdrop = null;
        if (material == "Acrylic (Frosted Glass)")
        {
            SystemBackdrop = null;
            _acrylicBackdrop = AcrylicWindowBackdrop.Attach(this, opacity / 250d);
        }
        else
        {
            if (material == "Pure OLED Dark") SystemBackdrop = null;
            else if (material == "Mica") SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            else if (material == "Mica Alt") SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            else SystemBackdrop = new DesktopAcrylicBackdrop();
        }
    }

    public void RefreshModules()
    {
        MonobarConditionService.RefreshMediaState();
        foreach (var module in _modules)
        {
            try
            {
                module.Refresh();
                var config = App.MainViewModel.Config.MonobarModules?.FirstOrDefault(item => string.Equals(item.Id, module.Id, StringComparison.OrdinalIgnoreCase));
                string condition = config?.VisibilityCondition ?? "always";
                bool visible = MonobarConditionService.Evaluate(condition);
                module.Element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { App.LogStartup($"Failed to refresh Monobar module '{module.Id}'", ex); }
        }
    }

    public void ShowBar()
    {
        RegisterAppBar();
        var bounds = DisplayArea.Primary?.OuterBounds ?? new RectInt32(0, 0, 1920, 1080);
        int height = Math.Clamp(App.MainViewModel.Config.MonobarHeight, 28, 64);
        MoveToTop(bounds, height);
        SetWindowPos(_hwnd, HwndTopmost, bounds.X, bounds.Y, bounds.Width, height, SwpNoActivate | SwpFrameChanged | SwpShowWindow);
        _barVisible = true;
        _appliedHeight = height;
    }

    public void HideBar()
    {
        AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd)).Hide();
        _barVisible = false;
        if (_registeredAsAppBar)
        {
            var data = CreateAppBarData();
            SHAppBarMessage(AbmRemove, ref data);
            _registeredAsAppBar = false;
        }
    }

    private void MoveToTop(RectInt32 work, int height)
    {
        if (_registeredAsAppBar)
        {
            var data = CreateAppBarData();
            data.Edge = AbeTop;
            data.Rect = new NativeRect { Left = work.X, Top = work.Y, Right = work.X + work.Width, Bottom = work.Y + height };
            SHAppBarMessage(AbmQueryPos, ref data);
            data.Rect.Bottom = data.Rect.Top + height;
            SHAppBarMessage(AbmSetPos, ref data);
            SetWindowPos(_hwnd, HwndTopmost, data.Rect.Left, data.Rect.Top, data.Rect.Right - data.Rect.Left, data.Rect.Bottom - data.Rect.Top, SwpNoActivate | SwpFrameChanged);
            return;
        }
        AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd)).MoveAndResize(new RectInt32(work.X, work.Y, work.Width, height));
    }

    private AppBarData CreateAppBarData() => new()
    {
        Size = (uint)Marshal.SizeOf<AppBarData>(),
        Window = _hwnd
    };

    private void RegisterAppBar()
    {
        if (_registeredAsAppBar) return;
        var data = CreateAppBarData();
        data.CallbackMessage = WmUser + 0x110;
        _registeredAsAppBar = SHAppBarMessage(AbmNew, ref data) != UIntPtr.Zero;
    }
}
