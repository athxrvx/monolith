using System.Runtime.InteropServices;
using System.Text;

namespace Monolith.Studio.Services;

/// <summary>Hides the Windows taskbar windows while Monolith is active.</summary>
internal static class TaskbarVisibilityService
{
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const uint AbmGetState = 0x00000004;
    private const uint AbmSetState = 0x0000000A;
    private const uint AbsAutoHide = 0x00000001;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly object Gate = new();
    private static readonly HashSet<IntPtr> HiddenTaskbars = [];
    private static readonly EnumWindowsProc EnumerateCallback = EnumerateTaskbars;
    private static Timer? _watcher;
    private static bool _hideEnabled;
    private static uint? _originalAppBarState;

    public static void Apply(bool hidden)
    {
        lock (Gate)
        {
            if (hidden)
            {
                _originalAppBarState ??= GetAppBarState();
                _hideEnabled = true;
                HideTaskbars();
                _watcher ??= new Timer(_ =>
                {
                    lock (Gate)
                    {
                        if (_hideEnabled) HideTaskbars();
                    }
                }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
            else
            {
                RestoreLocked(resetAutoHideWhenIdle: true);
            }
        }
    }

    public static void Restore()
    {
        lock (Gate) RestoreLocked(resetAutoHideWhenIdle: false);
    }

    private static void RestoreLocked(bool resetAutoHideWhenIdle)
    {
        if (!_hideEnabled && HiddenTaskbars.Count == 0)
        {
            if (resetAutoHideWhenIdle) SetAppBarState(GetAppBarState() & ~AbsAutoHide);
            return;
        }
        _hideEnabled = false;
        _watcher?.Dispose();
        _watcher = null;

        foreach (IntPtr hwnd in HiddenTaskbars)
        {
            if (IsTaskbarWindow(hwnd)) ShowTaskbar(hwnd);
        }
        HiddenTaskbars.Clear();
        EnumWindows(ShowTaskbarsCallback, IntPtr.Zero);
        uint state = _originalAppBarState ?? (resetAutoHideWhenIdle ? GetAppBarState() & ~AbsAutoHide : GetAppBarState());
        SetAppBarState(state);
        _originalAppBarState = null;
        App.LogStartup("Restored Windows taskbar visibility");
    }

    private static void HideTaskbars()
    {
        EnumWindows(EnumerateCallback, IntPtr.Zero);
    }

    private static bool EnumerateTaskbars(IntPtr hwnd, IntPtr lParam)
    {
        var className = new StringBuilder(128);
        _ = GetClassName(hwnd, className, className.Capacity);
        string name = className.ToString();
        if (IsTaskbarClass(name) && IsWindow(hwnd))
        {
            HiddenTaskbars.Add(hwnd);
            ShowWindow(hwnd, SwHide);
        }
        return true;
    }

    private static bool ShowTaskbarsCallback(IntPtr hwnd, IntPtr lParam)
    {
        if (IsTaskbarWindow(hwnd)) ShowTaskbar(hwnd);
        return true;
    }

    private static void ShowTaskbar(IntPtr hwnd)
    {
        ShowWindow(hwnd, SwShowNoActivate);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpShowWindow);
    }

    private static bool IsTaskbarWindow(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return false;
        var className = new StringBuilder(128);
        _ = GetClassName(hwnd, className, className.Capacity);
        return IsTaskbarClass(className.ToString());
    }

    private static bool IsTaskbarClass(string name) =>
        name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    private static uint GetAppBarState()
    {
        var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        return (uint)SHAppBarMessage(AbmGetState, ref data).ToUInt64();
    }

    private static void SetAppBarState(uint state)
    {
        var data = new AppBarData
        {
            Size = (uint)Marshal.SizeOf<AppBarData>(),
            LParam = new IntPtr(unchecked((int)state))
        };
        SHAppBarMessage(AbmSetState, ref data);
    }

    [DllImport("user32.dll", EntryPoint = "EnumWindows")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("shell32.dll", EntryPoint = "SHAppBarMessage")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rect;
        public IntPtr LParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
