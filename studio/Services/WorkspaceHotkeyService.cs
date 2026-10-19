using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using Monolith.Studio.Models;

namespace Monolith.Studio.Services;

public sealed class WorkspaceHotkeyService : IDisposable
{
    private const uint ModAlt = 0x0001;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private const uint WmNcDestroy = 0x0082;
    private const uint SubclassId = 0x4D4F4E4F;
    private const int FirstHotkeyId = 7100;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr referenceData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr subclassId, UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);

    private readonly IntPtr _hwnd;
    private readonly Action<HotkeyBinding> _hotkeyPressed;
    private readonly SubclassProc _subclassProc;
    private readonly Dictionary<int, HotkeyBinding> _bindings = new();
    private readonly HashSet<int> _registeredIds = new();
    private List<HotkeyBinding> _currentBindings = new();
    private bool _subclassInstalled;
    private bool _enabled = true;

    public WorkspaceHotkeyService(IntPtr hwnd, IEnumerable<HotkeyBinding> bindings, Action<HotkeyBinding> hotkeyPressed)
    {
        _hwnd = hwnd;
        _hotkeyPressed = hotkeyPressed;
        _subclassProc = WindowProc;

        _subclassInstalled = SetWindowSubclass(_hwnd, _subclassProc, (UIntPtr)SubclassId, UIntPtr.Zero);
        ReloadBindings(bindings);
    }

    public void ReloadBindings(IEnumerable<HotkeyBinding> bindings)
    {
        _currentBindings = bindings.ToList();
        RegisterCurrentBindings();
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        RegisterCurrentBindings();
    }

    private void RegisterCurrentBindings()
    {
        foreach (int id in _registeredIds) UnregisterHotKey(_hwnd, id);
        _registeredIds.Clear();
        _bindings.Clear();
        if (!_enabled) return;

        int hotkeyId = FirstHotkeyId;
        foreach (var binding in _currentBindings)
        {
            if (!TryParseBinding(binding.Binding, out uint modifiers, out uint key)) continue;
            int currentId = hotkeyId++;
            if (RegisterHotKey(_hwnd, currentId, modifiers | ModNoRepeat, key))
            {
                _registeredIds.Add(currentId);
                _bindings[currentId] = binding;
            }
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr referenceData)
    {
        if (message == WmHotkey)
        {
            int hotkeyMessageId = unchecked((int)wParam.ToUInt64());
            if (_bindings.TryGetValue(hotkeyMessageId, out var binding))
            {
                _hotkeyPressed(binding);
                return IntPtr.Zero;
            }
        }

        if (message == WmNcDestroy)
        {
            Dispose();
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero) return;
        foreach (int id in _registeredIds) UnregisterHotKey(_hwnd, id);
        _registeredIds.Clear();
        _bindings.Clear();
        if (_subclassInstalled)
        {
            RemoveWindowSubclass(_hwnd, _subclassProc, (UIntPtr)SubclassId);
            _subclassInstalled = false;
        }
    }

    private static bool TryParseBinding(string text, out uint modifiers, out uint key)
    {
        const uint modAlt = 0x0001, modControl = 0x0002, modShift = 0x0004, modWin = 0x0008;
        modifiers = 0;
        key = 0;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (var part in parts.Take(parts.Length - 1))
        {
            modifiers |= part.ToLowerInvariant() switch
            {
                "alt" => modAlt,
                "ctrl" or "control" => modControl,
                "shift" => modShift,
                "win" or "windows" => modWin,
                _ => 0
            };
        }
        string name = parts[^1].Trim();
        if (name.Length == 1 && char.IsLetterOrDigit(name[0])) key = char.ToUpperInvariant(name[0]);
        else key = name.ToLowerInvariant() switch
        {
            "space" => 0x20, "left" => 0x25, "up" => 0x26, "right" => 0x27, "down" => 0x28,
            "enter" or "return" => 0x0D, "tab" => 0x09, "escape" or "esc" => 0x1B,
            "backspace" => 0x08, "delete" or "del" => 0x2E, "insert" => 0x2D,
            "home" => 0x24, "end" => 0x23, "pageup" => 0x21, "pagedown" => 0x22,
            _ => 0
        };
        if (key == 0 && name.Length >= 2 && (name[0] is 'F' or 'f') && int.TryParse(name[1..], out int functionKey) && functionKey is >= 1 and <= 24)
            key = (uint)(0x70 + functionKey - 1);
        return modifiers != 0 && key != 0;
    }
}
