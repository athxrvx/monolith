using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monolith.Studio.ViewModels;

namespace Monolith.Studio.Views;

public sealed partial class AppearancePage : Page
{
    public MainViewModel ViewModel => App.MainViewModel;
    private bool _pageReady;

    public AppearancePage()
    {
        InitializeComponent();
        Loaded += (_, _) => _pageReady = true;
    }

    private void OnSettingToggled(object sender, RoutedEventArgs e)
    {
        if (_pageReady) _ = ViewModel.SaveConfigAsync();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_pageReady) _ = ViewModel.SaveConfigAsync();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pageReady) _ = ViewModel.SaveConfigAsync();
    }

    private void OnAnimationModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_pageReady) return;
        ViewModel.Config.EnableAnimations = !string.Equals(
            ViewModel.Config.AnimationMode, "None", System.StringComparison.OrdinalIgnoreCase);
        _ = ViewModel.SaveConfigAsync();
    }

    private void OnSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_pageReady) _ = ViewModel.SaveConfigAsync();
    }
}

