using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Monolith.Studio.Services;
using Monolith.Studio.ViewModels;

namespace Monolith.Studio.Views;

public sealed partial class GeneralPage : Page
{
    public MainViewModel ViewModel => App.MainViewModel;
    private bool _pageReady;

    public GeneralPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _pageReady = true;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_pageReady) _ = ViewModel.SaveConfigAsync();
    }

    private void OnSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pageReady) _ = ViewModel.SaveConfigAsync();
    }

    private void TaskbarVisibility_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_pageReady) return;
        if (sender is not ToggleSwitch toggle) return;
        ViewModel.Config.HideTaskbar = toggle.IsOn;
        TaskbarVisibilityService.Apply(toggle.IsOn);
        _ = ViewModel.SaveConfigAsync();
    }

    private void RestartEngine_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.RestartEngineAsync();
    }

    private void StopEngine_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StopEngine();
    }

    private void ReloadConfig_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.ReloadConfigAsync();
    }

    private void ViewLogFile_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenLogFile();
    }
}

