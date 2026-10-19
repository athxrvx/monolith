using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Monolith.Models;
using Monolith.ViewModels;

namespace Monolith.Views;

public partial class MonolithWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public MonolithViewModel ViewModel { get; }

    public MonolithWindow(MonolithViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += MonolithWindow_Loaded;
    }

    private void MonolithWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            int cornerPref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            int backdrop = DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Hide to background / tray instead of terminating everything
        Hide();
    }

    private void TabRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out int index))
        {
            if (PageGeneral == null) return;

            PageGeneral.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageWorkspaces.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            PageTiling.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            PageRules.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
            PageAppearance.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
            PageHotkeys.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void LayoutRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string layout)
        {
            ViewModel.Config.LayoutMode = layout;
            _ = ViewModel.SaveConfigAsync();
        }
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.SaveConfigAsync();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _ = ViewModel.SaveConfigAsync();
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _ = ViewModel.SaveConfigAsync();
    }

    private void RestartTiler_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.RestartTilerAsync();
    }

    private void OpenPalette_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenLauncherNow();
    }

    private void ViewLogFile_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenLogFile();
    }

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.SaveConfigAsync();
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetHotkeys();
    }

    private void EditHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HotkeyBinding binding)
        {
            // Simple prompt
            string current = binding.Binding;
            var w = new Window
            {
                Title = $"Edit Shortcut: {binding.Action}",
                Width = 360,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 18, 23)),
                WindowStyle = WindowStyle.ToolWindow
            };

            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "Enter key combination (e.g. Alt + Shift + D):", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 8) });
            var tb = new TextBox { Text = current, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 36)), Foreground = System.Windows.Media.Brushes.White, Padding = new Thickness(6, 4, 6, 4) };
            sp.Children.Add(tb);

            var okBtn = new Button { Content = "Save", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Width = 70, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 229, 255)), Foreground = System.Windows.Media.Brushes.Black, FontWeight = FontWeights.SemiBold };
            okBtn.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(tb.Text))
                {
                    binding.Binding = tb.Text.Trim();
                    _ = ViewModel.SaveConfigAsync();
                }
                w.Close();
            };
            sp.Children.Add(okBtn);

            w.Content = sp;
            w.ShowDialog();
        }
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        string proc = InputRuleProc.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(proc)) return;

        string action = (InputRuleAction.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Force Tile";
        int.TryParse(InputRuleWs.Text, out int ws);
        if (ws < 1) ws = 1;

        var rule = new WindowRule
        {
            ProcessName = proc,
            TitleRegex = ".*",
            Action = action,
            TargetWorkspace = ws,
            ManageBorderVisibility = true
        };

        ViewModel.AddRule(rule);
        InputRuleProc.Text = string.Empty;
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WindowRule rule)
        {
            ViewModel.RemoveRule(rule);
        }
    }
}

