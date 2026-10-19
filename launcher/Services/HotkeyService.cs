using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Monolith.Services;

public sealed class HotkeyService : IDisposable
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312;

    private const int ID_LAUNCHER = 9999;
    private const int ID_DASHBOARD = 3001;
    private const int ID_TOGGLE_FLOATING = 3002;
    private const int ID_CLOSE_WINDOW = 3003;

    private const int ID_WS_SWITCH_BASE = 1000; // 1001..1009
    private const int ID_WS_MOVE_BASE = 2000;   // 2001..2009

    private const int ID_FOCUS_H = 4001;
    private const int ID_FOCUS_J = 4002;
    private const int ID_FOCUS_K = 4003;
    private const int ID_FOCUS_L = 4004;

    private const int ID_MOVE_H = 5001;
    private const int ID_MOVE_J = 5002;
    private const int ID_MOVE_K = 5003;
    private const int ID_MOVE_L = 5004;

    private const uint VK_SPACE = 0x20;
    private const uint VK_D = 0x44;
    private const uint VK_F = 0x46;
    private const uint VK_Q = 0x51;
    private const uint VK_H = 0x48;
    private const uint VK_J = 0x4A;
    private const uint VK_K = 0x4B;
    private const uint VK_L = 0x4C;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _hwnd;
    private HwndSource? _source;
    private readonly HashSet<int> _registeredIds = new();

    public event Action? LauncherHotkeyPressed;
    public event Action? DashboardHotkeyPressed;
    public event Action? ToggleFloatingHotkeyPressed;
    public event Action? CloseWindowHotkeyPressed;
    public event Action<int>? WorkspaceSwitchHotkeyPressed;
    public event Action<int>? MoveWindowWorkspaceHotkeyPressed;
    public event Action<string>? FocusDirectionHotkeyPressed;
    public event Action<string>? MoveDirectionHotkeyPressed;

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(HwndHook);
    }

    public void EnableLauncherHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;
        Register(ID_LAUNCHER, MOD_ALT | MOD_NOREPEAT, VK_SPACE);
    }

    public void DisableLauncherHotkey()
    {
        Unregister(ID_LAUNCHER);
    }

    public void EnableTilerHotkeys()
    {
        if (_hwnd == IntPtr.Zero) return;

        // Dashboard toggle: Alt + Shift + D
        Register(ID_DASHBOARD, MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, VK_D);

        // Toggle Floating: Alt + F
        Register(ID_TOGGLE_FLOATING, MOD_ALT | MOD_NOREPEAT, VK_F);

        // Close Window: Alt + Shift + Q
        Register(ID_CLOSE_WINDOW, MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, VK_Q);

        // Workspace Focus: Alt + 1..9
        for (int i = 1; i <= 9; i++)
        {
            uint vk = (uint)(0x30 + i);
            Register(ID_WS_SWITCH_BASE + i, MOD_ALT | MOD_NOREPEAT, vk);
        }

        // Workspace Move: Alt + Shift + 1..9
        for (int i = 1; i <= 9; i++)
        {
            uint vk = (uint)(0x30 + i);
            Register(ID_WS_MOVE_BASE + i, MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, vk);
        }

        // Focus Navigation: Alt + H / J / K / L
        Register(ID_FOCUS_H, MOD_ALT | MOD_NOREPEAT, VK_H);
        Register(ID_FOCUS_J, MOD_ALT | MOD_NOREPEAT, VK_J);
        Register(ID_FOCUS_K, MOD_ALT | MOD_NOREPEAT, VK_K);
        Register(ID_FOCUS_L, MOD_ALT | MOD_NOREPEAT, VK_L);

        // Move Navigation: Alt + Shift + H / J / K / L
        Register(ID_MOVE_H, MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, VK_H);
        Register(ID_MOVE_J, MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, VK_J);
        Register(ID_MOVE_K, MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, VK_K);
        Register(ID_MOVE_L, MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, VK_L);
    }

    public void DisableTilerHotkeys()
    {
        Unregister(ID_DASHBOARD);
        Unregister(ID_TOGGLE_FLOATING);
        Unregister(ID_CLOSE_WINDOW);

        for (int i = 1; i <= 9; i++)
        {
            Unregister(ID_WS_SWITCH_BASE + i);
            Unregister(ID_WS_MOVE_BASE + i);
        }

        Unregister(ID_FOCUS_H);
        Unregister(ID_FOCUS_J);
        Unregister(ID_FOCUS_K);
        Unregister(ID_FOCUS_L);

        Unregister(ID_MOVE_H);
        Unregister(ID_MOVE_J);
        Unregister(ID_MOVE_K);
        Unregister(ID_MOVE_L);
    }

    private void Register(int id, uint modifiers, uint vk)
    {
        if (_hwnd != IntPtr.Zero && !_registeredIds.Contains(id))
        {
            if (RegisterHotKey(_hwnd, id, modifiers, vk))
            {
                _registeredIds.Add(id);
            }
        }
    }

    private void Unregister(int id)
    {
        if (_hwnd != IntPtr.Zero && _registeredIds.Contains(id))
        {
            UnregisterHotKey(_hwnd, id);
            _registeredIds.Remove(id);
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();

            if (id == ID_LAUNCHER)
            {
                LauncherHotkeyPressed?.Invoke();
                handled = true;
            }
            else if (id == ID_DASHBOARD)
            {
                DashboardHotkeyPressed?.Invoke();
                handled = true;
            }
            else if (id == ID_TOGGLE_FLOATING)
            {
                ToggleFloatingHotkeyPressed?.Invoke();
                handled = true;
            }
            else if (id == ID_CLOSE_WINDOW)
            {
                CloseWindowHotkeyPressed?.Invoke();
                handled = true;
            }
            else if (id > ID_WS_SWITCH_BASE && id <= ID_WS_SWITCH_BASE + 9)
            {
                int ws = id - ID_WS_SWITCH_BASE;
                WorkspaceSwitchHotkeyPressed?.Invoke(ws);
                handled = true;
            }
            else if (id > ID_WS_MOVE_BASE && id <= ID_WS_MOVE_BASE + 9)
            {
                int ws = id - ID_WS_MOVE_BASE;
                MoveWindowWorkspaceHotkeyPressed?.Invoke(ws);
                handled = true;
            }
            else if (id is >= ID_FOCUS_H and <= ID_FOCUS_L)
            {
                string dir = id switch
                {
                    ID_FOCUS_H => "left",
                    ID_FOCUS_J => "down",
                    ID_FOCUS_K => "up",
                    _ => "right"
                };
                FocusDirectionHotkeyPressed?.Invoke(dir);
                handled = true;
            }
            else if (id is >= ID_MOVE_H and <= ID_MOVE_L)
            {
                string dir = id switch
                {
                    ID_MOVE_H => "left",
                    ID_MOVE_J => "down",
                    ID_MOVE_K => "up",
                    _ => "right"
                };
                MoveDirectionHotkeyPressed?.Invoke(dir);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        DisableLauncherHotkey();
        DisableTilerHotkeys();
        if (_source != null)
        {
            _source.RemoveHook(HwndHook);
            _source = null;
        }
    }
}
