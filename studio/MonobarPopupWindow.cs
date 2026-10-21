using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace Monolith.Studio;

internal sealed class MonobarPopupWindow : Window
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsExToolWindow = 0x80;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly int _widthDip;
    private readonly int _heightDip;

    public MonobarPopupWindow(UIElement content, int width, int height)
    {
        _widthDip = width;
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        _appWindow = AppWindow.GetFromWindowId(id);
        _appWindow.IsShownInSwitchers = false;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Content = content;
        if (height <= 0)
        {
            var bounds = DisplayArea.Primary?.OuterBounds ?? new RectInt32(0, 0, 1920, 1080);
            double scale = GetDpiForWindow(_hwnd) / 96d;
            double maxHeightDip = Math.Max(240, (bounds.Height - ToPixels(App.MainViewModel.Config.MonobarHeight) - 24) / scale);
            content.Measure(new Size(width, maxHeightDip));
            // Controls can report fractional desired sizes before their templates render.
            // Leave a small layout allowance so the last row is not clipped by the HWND.
            _heightDip = Math.Max(1, (int)Math.Ceiling(Math.Min(content.DesiredSize.Height + 6, maxHeightDip)));
        }
        else _heightDip = height;
        _appWindow.Resize(new SizeInt32 { Width = ToPixels(width), Height = ToPixels(_heightDip) });
        var style = GetWindowLongPtr(_hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlStyle, new IntPtr((style & ~0x00C00000L & ~0x00040000L & ~0x00020000L) | WsPopup));
        var exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle | WsExToolWindow));
        int roundedCorners = DwmwcpRound;
        DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref roundedCorners, sizeof(int));
        int noSystemBorder = DwmColorNone;
        DwmSetWindowAttribute(_hwnd, DwmwaBorderColor, ref noSystemBorder, sizeof(int));
        // Keep the popup surface owned by WinUI. Applying the legacy accent
        // policy to a WinUI HWND makes its content render over a black backing.
        SystemBackdrop = new DesktopAcrylicBackdrop();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                _appWindow.Hide();
        };
    }

    public void ShowAt(int x, int y)
    {
        var bounds = DisplayArea.Primary?.OuterBounds ?? new RectInt32(0, 0, 1920, 1080);
        int width = ToPixels(_widthDip), height = ToPixels(_heightDip);
        x = Math.Clamp(x, bounds.X + 6, Math.Max(bounds.X + 6, bounds.X + bounds.Width - width - 6));
        y = Math.Clamp(y, bounds.Y + 6, Math.Max(bounds.Y + 6, bounds.Y + bounds.Height - height - 6));
        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        // Set z-order and final frame while hidden, then activate so acrylic is
        // present on the first visible frame instead of appearing after a click.
        SetWindowPos(_hwnd, HwndTopmost, x, y, width, height, SwpFrameChanged | SwpNoActivate);
        Activate();
    }

    public void ShowBelow(UIElement anchor, bool centerOnAnchor = false)
    {
        var bounds = DisplayArea.Primary?.OuterBounds ?? new RectInt32(0, 0, 1920, 1080);
        int width = ToPixels(_widthDip);
        int x = bounds.X + bounds.Width - width - 8;
        int y = bounds.Y + ToPixels(App.MainViewModel.Config.MonobarHeight) + 4;
        if (MonobarWindow.BarHandle != IntPtr.Zero && GetWindowRect(MonobarWindow.BarHandle, out var barRect))
        {
            double scale = GetDpiForWindow(MonobarWindow.BarHandle) / 96d;
            Point origin = anchor.TransformToVisual(null).TransformPoint(new Point(0, 0));
            int anchorLeft = barRect.Left + (int)Math.Round(origin.X * scale);
            double anchorWidthDip = anchor is FrameworkElement element ? element.ActualWidth : 0;
            int anchorWidth = (int)Math.Round(anchorWidthDip * scale);
            x = centerOnAnchor
                ? anchorLeft + anchorWidth / 2 - width / 2
                : anchorLeft + anchorWidth - width;
            y = barRect.Bottom + 4;
        }
        ShowAt(x, y);
    }

    private int ToPixels(int dip) => (int)Math.Round(dip * GetDpiForWindow(_hwnd) / 96d);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
}
