using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monolith.Studio.ViewModels;

namespace Monolith.Studio.Views;

public sealed partial class AppearancePage : Page
{
    public MainViewModel ViewModel => App.MainViewModel;
    private bool _pageReady;
    private readonly DispatcherTimer _sliderSaveTimer;

    public AppearancePage()
    {
        InitializeComponent();
        _sliderSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _sliderSaveTimer.Tick += (_, _) =>
        {
            _sliderSaveTimer.Stop();
            _ = ViewModel.SaveConfigAsync();
        };
        Loaded += (_, _) =>
        {
            _pageReady = true;
            LauncherOpacityValue.Text = ViewModel.Config.LauncherOpacity.ToString();
            UpdateBackdropControls();
        };
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
        if (!_pageReady) return;
        UpdateBackdropControls();
        _ = ViewModel.SaveConfigAsync();
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
        if (!_pageReady) return;
        if (ReferenceEquals(sender, LauncherOpacitySlider))
            LauncherOpacityValue.Text = ((int)Math.Round(e.NewValue)).ToString();
        _sliderSaveTimer.Stop();
        _sliderSaveTimer.Start();
    }

    private void UpdateBackdropControls()
    {
        bool oled = string.Equals(ViewModel.Config.BackdropMaterial, "Pure OLED Dark", StringComparison.OrdinalIgnoreCase);
        LauncherOpacitySlider.IsEnabled = !oled;
        LauncherOpacityValue.Opacity = oled ? 0.45 : 1.0;
    }
}

