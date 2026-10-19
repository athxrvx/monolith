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

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        string process = InputProcessName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(process)) return;

        var rule = new WindowRule
        {
            ProcessName = process,
            TitleRegex = string.IsNullOrEmpty(InputTitleRegex.Text) ? ".*" : InputTitleRegex.Text.Trim(),
            Action = ComboAction.SelectedValue?.ToString() ?? "Force Tile",
            ManageBorderVisibility = ToggleManageBorder.IsOn
        };

        ViewModel.AddRule(rule);
        InputProcessName.Text = string.Empty;
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

