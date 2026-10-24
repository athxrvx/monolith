using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Monolith.Studio.Services;

internal static class AccentColorService
{
    private static readonly UISettings Settings = new();
    private static bool _isListening;

    internal static event Action<Color>? AccentChanged;

    internal static Color GetAccentColor()
    {
        var config = App.MainViewModel.Config;
        if (string.Equals(config.AccentMode, "Manual", StringComparison.OrdinalIgnoreCase)
            && TryParseColor(config.AccentColor, out var manualColor))
            return manualColor;

        try { return Settings.GetColorValue(UIColorType.Accent); }
        catch { return Color.FromArgb(255, 96, 205, 255); }
    }

    internal static bool TryParseColor(string? value, out Color color)
    {
        color = Color.FromArgb(255, 96, 205, 255);
        if (string.IsNullOrWhiteSpace(value)) return false;
        string hex = value.Trim().TrimStart('#');
        if (hex.Length == 6
            && byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out byte red)
            && byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte green)
            && byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte blue))
        {
            color = Color.FromArgb(255, red, green, blue);
            return true;
        }
        return false;
    }

    internal static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    internal static void RefreshAccent()
    {
        if (!_isListening)
        {
            Settings.ColorValuesChanged += (_, _) =>
            {
                if (!string.Equals(App.MainViewModel.Config.AccentMode, "Windows", StringComparison.OrdinalIgnoreCase))
                    return;
                DispatchRefresh();
            };
            _isListening = true;
        }

        DispatchRefresh();
    }

    private static void DispatchRefresh()
    {
        void Apply()
        {
            Color color = GetAccentColor();
            var resources = App.Current.Resources;
            resources["AccentBlueColor"] = color;
            if (resources.TryGetValue("AccentBlueBrush", out object brushResource)
                && brushResource is SolidColorBrush accentBrush)
                accentBrush.Color = color;
            AccentChanged?.Invoke(color);
        }

        DispatcherQueue? dispatcher = App.MainWindowInstance?.DispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
            dispatcher.TryEnqueue(Apply);
        else
            Apply();
    }
}
