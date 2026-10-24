using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Monolith.Studio.Views;

public sealed partial class MonoLauncherPage : Page
{
    private bool _ready;
    private readonly DispatcherTimer _saveTimer;

    public MonoLauncherPage()
    {
        InitializeComponent();
        var config = App.MainViewModel.Config;
        MaterialCombo.SelectedItem = config.BackdropMaterial is "Acrylic (Frosted Glass)" or "Pure OLED Dark"
            ? config.BackdropMaterial
            : "Acrylic (Frosted Glass)";
        WidthSlider.Value = config.LauncherWidthPercent;
        HeightSlider.Value = config.LauncherHeightPercent;
        UpdateLabels();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _ = App.MainViewModel.SaveConfigAsync();
            App.MainWindowInstance?.ApplyLauncherAppearance();
        };
        Loaded += (_, _) => _ready = true;
        Unloaded += (_, _) => Save();
        MaterialCombo.SelectionChanged += (_, _) => Save();
        WidthSlider.ValueChanged += (_, _) => { UpdateLabels(); Save(); };
        HeightSlider.ValueChanged += (_, _) => { UpdateLabels(); Save(); };
    }

    private void UpdateLabels()
    {
        WidthValue.Text = $"{(int)Math.Round(WidthSlider.Value)}%";
        HeightValue.Text = $"{(int)Math.Round(HeightSlider.Value)}%";
    }

    private void Save()
    {
        if (!_ready) return;
        var config = App.MainViewModel.Config;
        if (MaterialCombo.SelectedItem is string material) config.BackdropMaterial = material;
        config.LauncherWidthPercent = (int)Math.Round(WidthSlider.Value);
        config.LauncherHeightPercent = (int)Math.Round(HeightSlider.Value);
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
