using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monolith.Studio.Models;
using Monolith.Studio.ViewModels;

namespace Monolith.Studio.Views;

public sealed partial class HotkeysPage : Page
{
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    public MainViewModel ViewModel => App.MainViewModel;

    public HotkeysPage()
    {
        InitializeComponent();
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetHotkeys();
    }

    private async void AddHotkey_Click(object sender, RoutedEventArgs e)
    {
        string[] actions =
        {
            "Toggle Launcher", "Toggle Float", "Tile Vertically", "Toggle Monocle",
            "Close Active Window", "Minimize Active Window", "Maximize/Restore Active Window",
            "Launch Terminal (wt.exe)", "Open File Explorer", "Launch Application", "Run Shell Command",
            "Widen Window", "Narrow Window", "Increase Window Height", "Decrease Window Height",
            "Focus Left", "Focus Right", "Focus Up", "Focus Down",
            "Previous Desktop", "Next Desktop", "Move Window to Previous Desktop", "Move Window to Next Desktop"
        };
        var actionBox = new ComboBox { ItemsSource = actions, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var bindingBox = CreateShortcutRecorder();
        var argumentBox = new TextBox { PlaceholderText = "Application path or script/command" };
        var argumentLabel = new TextBlock { Text = "Application path or script/command" };
        var argumentPanel = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        argumentPanel.Children.Add(argumentLabel);
        argumentPanel.Children.Add(argumentBox);
        actionBox.SelectionChanged += (_, _) =>
        {
            string selectedAction = actionBox.SelectedItem?.ToString() ?? string.Empty;
            argumentPanel.Visibility = selectedAction is "Launch Application" or "Run Shell Command"
                ? Visibility.Visible
                : Visibility.Collapsed;
        };
        var dialog = new ContentDialog
        {
            Title = "Add key binding",
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Action" }, actionBox,
                    new TextBlock { Text = "Shortcut: click the field and press the key combination" }, bindingBox,
                    argumentPanel
                }
            },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };
        App.SetHotkeysEnabled?.Invoke(false);
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { App.SetHotkeysEnabled?.Invoke(true); }
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(bindingBox.Text))
        {
            string action = actionBox.SelectedItem?.ToString() ?? actions[0];
            ViewModel.AddHotkey(action, bindingBox.Text.Trim(), argumentPanel.Visibility == Visibility.Visible ? argumentBox.Text.Trim() : string.Empty);
        }
    }

    private void RemoveHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is HotkeyBinding binding)
            ViewModel.RemoveHotkey(binding);
    }

    private async void BindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HotkeyBinding binding)
        {
            var textBox = CreateShortcutRecorder();
            textBox.Text = binding.Binding;

            var dialog = new ContentDialog
            {
                Title = $"Edit Shortcut: {binding.Action}",
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "Enter your desired key combination:", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)) },
                        textBox
                    }
                },
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            };

            App.SetHotkeysEnabled?.Invoke(false);
            ContentDialogResult result;
            try { result = await dialog.ShowAsync(); }
            finally { App.SetHotkeysEnabled?.Invoke(true); }
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                binding.Binding = textBox.Text.Trim();
                _ = ViewModel.SaveConfigAsync();
                // Refresh list display
                HotkeysListView.ItemsSource = null;
                HotkeysListView.ItemsSource = ViewModel.HotkeyBindings;
            }
        }
    }

    private static TextBox CreateShortcutRecorder()
    {
        var box = new TextBox { IsReadOnly = true, PlaceholderText = "Click here, then press a shortcut" };
        box.KeyDown += (_, e) =>
        {
            int key = (int)e.Key;
            if (key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C) { e.Handled = true; return; }
            string? keyName = GetKeyName(key);
            if (keyName is null) { e.Handled = true; return; }

            var parts = new System.Collections.Generic.List<string>();
            if (IsPressed(0x11)) parts.Add("Ctrl");
            if (IsPressed(0x12)) parts.Add("Alt");
            if (IsPressed(0x10)) parts.Add("Shift");
            if (IsPressed(0x5B) || IsPressed(0x5C)) parts.Add("Win");
            if (parts.Count == 0) { e.Handled = true; return; }
            parts.Add(keyName);
            box.Text = string.Join(" + ", parts);
            e.Handled = true;
        };
        return box;
    }

    private static bool IsPressed(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

    private static string? GetKeyName(int key)
    {
        if (key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)key).ToString();
        if (key is >= 0x70 and <= 0x87) return $"F{key - 0x6F}";
        return key switch
        {
            0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x1B => "Escape", 0x20 => "Space",
            0x21 => "PageUp", 0x22 => "PageDown", 0x23 => "End", 0x24 => "Home",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            0x2D => "Insert", 0x2E => "Delete", _ => null
        };
    }
}

