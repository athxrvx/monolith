using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monolith.Studio.Models;
using Monolith.Studio.ViewModels;

namespace Monolith.Studio.Views;

public sealed partial class RulesPage : Page
{
    public MainViewModel ViewModel => App.MainViewModel;

    public RulesPage()
    {
        InitializeComponent();
    }

    private async void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var processName = new TextBox
        {
            Header = "Process executable name",
            PlaceholderText = "e.g. spotify.exe, code.exe",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var titleRegex = new TextBox
        {
            Header = "Window title regex",
            Text = ".*",
            PlaceholderText = "e.g. .* - Visual Studio",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var action = new ComboBox
        {
            Header = "Window action",
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        action.Items.Add("Force Tile");
        action.Items.Add("Force Float");
        action.Items.Add("Ignore Tiling");

        var form = new StackPanel { Spacing = 12, MinWidth = 420 };
        form.Children.Add(processName);
        form.Children.Add(titleRegex);
        form.Children.Add(action);
        var dialog = new ContentDialog
        {
            Title = "Add window rule",
            Content = form,
            PrimaryButtonText = "Add rule",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        string process = processName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(process)) return;

        var rule = new WindowRule
        {
            ProcessName = process,
            TitleRegex = string.IsNullOrEmpty(titleRegex.Text) ? ".*" : titleRegex.Text.Trim(),
            Action = action.SelectedItem?.ToString() ?? "Force Tile"
        };

        ViewModel.AddRule(rule);
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WindowRule rule)
        {
            ViewModel.RemoveRule(rule);
        }
    }

    private async void ExportRules_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = JsonSerializer.Serialize(ViewModel.WindowRules, new JsonSerializerOptions { WriteIndented = true });
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Monolith", "rules_export.json");
            await File.WriteAllTextAsync(appData, json);

            var dialog = new ContentDialog
            {
                Title = "Rules Exported",
                Content = $"Window rules successfully exported to:\n{appData}",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch { }
    }
}

