using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Monolith.Studio.Services;
using Monolith.Studio.ViewModels;

namespace Monolith.Studio.Views;

public sealed partial class AppearancePage : Page
{
    public MainViewModel ViewModel => App.MainViewModel;
    private bool _pageReady;
    private bool _updatingAccentMode;
    private readonly DispatcherTimer _sliderSaveTimer;


    public AppearancePage()
    {
        InitializeComponent();
        _sliderSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _sliderSaveTimer.Tick += (_, _) => { _sliderSaveTimer.Stop(); _ = ViewModel.SaveConfigAsync(); };
        Loaded += (_, _) =>
        {
            AccentModeCombo.SelectedIndex = string.Equals(ViewModel.Config.AccentMode, "Manual", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _pageReady = true;
            UpdateAccentControls();
            UpdateAccentPreview(AccentColorService.GetAccentColor());
            AccentColorService.AccentChanged += OnAccentColorChanged;
        };
        Unloaded += (_, _) => AccentColorService.AccentChanged -= OnAccentColorChanged;
    }

    private void OnSettingToggled(object sender, RoutedEventArgs e)
    {
        if (_pageReady) _ = ViewModel.SaveConfigAsync();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
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
        if (!_pageReady) return;
        _sliderSaveTimer.Stop();
        _sliderSaveTimer.Start();
    }

    private async void OnAccentModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_pageReady || _updatingAccentMode) return;
        ViewModel.Config.AccentMode = AccentModeCombo.SelectedIndex == 1 ? "Manual" : "Windows";
        UpdateAccentControls();
        AccentColorService.RefreshAccent();
        await ViewModel.SaveConfigAsync();
    }

    private async void ChooseAccentColor_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPicker
        {
            Color = AccentColorService.GetAccentColor(),
            IsColorPreviewVisible = true,
            IsMoreButtonVisible = true,
            IsAlphaEnabled = false,
            MinWidth = 320
        };
        var dialog = new ContentDialog
        {
            Title = "Choose accent color",
            Content = picker,
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ViewModel.Config.AccentMode = "Manual";
        ViewModel.Config.AccentColor = AccentColorService.ToHex(picker.Color);
        _updatingAccentMode = true;
        AccentModeCombo.SelectedIndex = 1;
        _updatingAccentMode = false;
        UpdateAccentControls();
        AccentColorService.RefreshAccent();
        await ViewModel.SaveConfigAsync();
    }

    private void UpdateAccentControls()
    {
        bool manual = string.Equals(ViewModel.Config.AccentMode, "Manual", StringComparison.OrdinalIgnoreCase);
        ChooseAccentButton.IsEnabled = manual;
        AccentHex.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        AccentPreview.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAccentColorChanged(Windows.UI.Color color) => UpdateAccentPreview(color);

    private void UpdateAccentPreview(Windows.UI.Color color)
    {
        AccentPreview.Background = new SolidColorBrush(color);
        AccentHex.Text = AccentColorService.ToHex(color);
    }

}







