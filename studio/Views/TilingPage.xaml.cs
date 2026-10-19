using Microsoft.UI.Xaml.Controls;
using Monolith.Studio.ViewModels;

namespace Monolith.Studio.Views;

public sealed partial class TilingPage : Page
{
    public MainViewModel ViewModel => App.MainViewModel;
    private bool _pageReady;

    public TilingPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _pageReady = true;
    }

    private void OnSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pageReady) _ = ViewModel.SaveConfigAsync();
    }

    private void OnNumberBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_pageReady) _ = ViewModel.SaveConfigAsync();
    }
}
