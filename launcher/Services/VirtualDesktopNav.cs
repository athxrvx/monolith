using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Monolith.Services;

/// <summary>
/// Provides hardware-level native navigation for Windows 11 Virtual Desktops
/// using synthesized input events (Win + Ctrl + Left / Right).
/// </summary>
public static class VirtualDesktopNav
{
    private const byte VK_MENU = 0x12;     // Alt key
    private const byte VK_LWIN = 0x5B;     // Left Windows key
    private const byte VK_CONTROL = 0x11;  // Ctrl key
    private const byte VK_LEFT = 0x25;     // Left arrow
    private const byte VK_RIGHT = 0x27;    // Right arrow
    private const byte VK_D = 0x44;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public static void CreateVirtualDesktop()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_D, 0, 0, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        });
    }

    public static void MoveOneDesktop(bool next)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            byte arrowKey = next ? VK_RIGHT : VK_LEFT;
            keybd_event(arrowKey, 0, 0, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(arrowKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        });
    }

    public static void NavigateToWorkspace(int current, int target)
    {
        int steps = target - current;
        if (steps == 0) return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            for (int i = 0; i < Math.Abs(steps); i++)
            {
                keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                byte arrowKey = steps > 0 ? VK_RIGHT : VK_LEFT;
                keybd_event(arrowKey, 0, 0, UIntPtr.Zero);
                Thread.Sleep(15);
                keybd_event(arrowKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(80);
            }
        });
    }

    public static void MoveActiveWindowOneDesktop(bool next)
    {
        byte arrowKey = next ? VK_RIGHT : VK_LEFT;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(0x10, 0, 0, UIntPtr.Zero);
            keybd_event(arrowKey, 0, 0, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(arrowKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(0x10, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        });
    }
}
