using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Monolith.Studio.Services;

/// <summary>Creates the notification area icon and its native Windows context menu.</summary>
public sealed class TrayIconService : IDisposable
{
    private const int CallbackMessage = 0x8000 + 42;
    private const int WmDestroy = 0x0002;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint MfString = 0;
    private const uint MfSeparator = 0x00000800;
    private const uint MfChecked = 0x00000008;
    private const uint MfGrayed = 0x00000001;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const uint LrDefaultSize = 0x0040;

    private static readonly WndProc WindowProcedure = WindowProc;
    private static readonly string WindowClassName = $"Monolith.Tray.{Guid.NewGuid():N}";
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread()!;
    private readonly Action _showSettings;
    private readonly Action _showLauncher;
    private readonly Func<bool> _isMonolithEnabled;
    private readonly Action<bool> _setMonolithEnabled;
    private readonly Func<bool> _isTilerEnabled;
    private readonly Action<bool> _setTilerEnabled;
    private readonly Func<bool> _isLauncherEnabled;
    private readonly Action<bool> _setLauncherEnabled;
    private readonly Action _restartTiler;
    private readonly Action _exitApp;
    private IntPtr _window;
    private IntPtr _icon;
    private IntPtr _instance;
    private bool _classRegistered;
    private bool _iconAdded;
    private bool _disposed;

    public TrayIconService(
        Action showSettings,
        Action showLauncher,
        Func<bool> isMonolithEnabled,
        Action<bool> setMonolithEnabled,
        Func<bool> isTilerEnabled,
        Action<bool> setTilerEnabled,
        Func<bool> isLauncherEnabled,
        Action<bool> setLauncherEnabled,
        Action restartTiler,
        Action exitApp)
    {
        _showSettings = showSettings;
        _showLauncher = showLauncher;
        _isMonolithEnabled = isMonolithEnabled;
        _setMonolithEnabled = setMonolithEnabled;
        _isTilerEnabled = isTilerEnabled;
        _setTilerEnabled = setTilerEnabled;
        _isLauncherEnabled = isLauncherEnabled;
        _setLauncherEnabled = setLauncherEnabled;
        _restartTiler = restartTiler;
        _exitApp = exitApp;
        try
        {
            _instance = GetModuleHandle(null);
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Instance = _instance,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedure),
                ClassName = WindowClassName
            };
            if (RegisterClassEx(ref windowClass) == 0)
                throw new InvalidOperationException("Could not register the Monolith tray window.");
            _classRegistered = true;

            _window = CreateWindowEx(WsExToolWindow, WindowClassName, "Monolith Tray", WsPopup,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
            if (_window == IntPtr.Zero)
                throw new InvalidOperationException("Could not create the Monolith tray window.");

            string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "monolith_icon.ico");
            _icon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
            if (_icon == IntPtr.Zero)
                throw new InvalidOperationException($"Could not load the Monolith tray icon from '{iconPath}'.");
            var data = new NotifyIconData
            {
                Size = (uint)Marshal.SizeOf<NotifyIconData>(),
                Window = _window,
                Id = 1,
                Flags = NifMessage | NifIcon | NifTip,
                CallbackMessage = CallbackMessage,
                Icon = _icon,
                Tip = "Monolith Desktop Environment",
                Info = string.Empty,
                InfoTitle = string.Empty
            };
            if (!ShellNotifyIcon(NimAdd, ref data))
                throw new InvalidOperationException("Windows could not add the Monolith notification icon.");
            _iconAdded = true;
            _activeService = this;
        }
        catch
        {
            ReleaseNativeResources();
            throw;
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            int mouseMessage = unchecked((int)lParam);
            if (mouseMessage == WmLButtonUp)
                Dispatch(actionId: 1);
            else if (mouseMessage == WmRButtonUp)
                Dispatch(actionId: 0);
            return IntPtr.Zero;
        }
        if (message == WmDestroy)
            return IntPtr.Zero;
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void Dispatch(uint actionId)
    {
        // The tray window belongs to the UI thread; its current instance is held by the active service.
        _activeService?.HandleAction(actionId);
    }

    private static TrayIconService? _activeService;

    private void HandleAction(uint actionId)
    {
        _dispatcher.TryEnqueue(() =>
        {
            switch (actionId)
            {
                case 0: ShowContextMenu(); break;
                case 1: _showSettings(); break;
                case 2: _showLauncher(); break;
                case 8: _setMonolithEnabled(!_isMonolithEnabled()); break;
                case 3 when _isMonolithEnabled(): _setTilerEnabled(!_isTilerEnabled()); break;
                case 4 when _isMonolithEnabled(): _setLauncherEnabled(!_isLauncherEnabled()); break;
                case 5 when _isMonolithEnabled(): _restartTiler(); break;
                case 6: _exitApp(); break;
            }
        });
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            AppendMenu(menu, MfString, 1, "Open Monolith Settings");
            AppendMenu(menu, MfString, 2, "Open Launcher");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString | (_isMonolithEnabled() ? MfChecked : 0), 8, "Monolith enabled");
            AppendMenu(menu, MfSeparator, 0, null);
            uint disabled = _isMonolithEnabled() ? 0 : MfGrayed;
            AppendMenu(menu, MfString | disabled | (_isTilerEnabled() ? MfChecked : 0), 3, "Tiler enabled");
            AppendMenu(menu, MfString | disabled | (_isLauncherEnabled() ? MfChecked : 0), 4, "Launcher enabled");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString | disabled, 5, "Restart Tiler");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, 6, "Quit Monolith");
            GetCursorPos(out Point cursor);
            SetForegroundWindow(_window);
            uint command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand,
                cursor.X, cursor.Y, 0, _window, IntPtr.Zero);
            if (command != 0)
                HandleAction(command);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ReleaseNativeResources();
        if (ReferenceEquals(_activeService, this))
            _activeService = null;
    }

    private void ReleaseNativeResources()
    {
        if (_window != IntPtr.Zero)
        {
            if (_iconAdded)
            {
                var data = new NotifyIconData
                {
                    Size = (uint)Marshal.SizeOf<NotifyIconData>(),
                    Window = _window,
                    Id = 1,
                    Tip = string.Empty,
                    Info = string.Empty,
                    InfoTitle = string.Empty
                };
                ShellNotifyIcon(NimDelete, ref data);
                _iconAdded = false;
            }
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
        if (_classRegistered)
        {
            UnregisterClass(WindowClassName, _instance);
            _classRegistered = false;
        }
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UnregisterClass(string className, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, uint id, string? text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
