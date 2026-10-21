using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monolith.Studio.Models;

namespace Monolith.Studio.Views;

public sealed partial class MonobarPage : Page
{
    private bool _ready;
    public MonobarPage()
    {
        InitializeComponent();
        var vm = App.MainViewModel;
        EnabledToggle.IsOn = vm.Config.MonobarEnabled;
        HeightSlider.Value = vm.Config.MonobarHeight;
        OpacitySlider.Value = vm.Config.MonobarOpacity;
        MaterialCombo.SelectedItem = vm.Config.MonobarMaterial;
        ModulesList.ItemsSource = vm.MonobarModules;
        HeightValue.Text = $"{vm.Config.MonobarHeight} px";
        OpacityValue.Text = $"{vm.Config.MonobarOpacity}%";
        _ready = true;

        EnabledToggle.Toggled += (_, _) => Save();
        HeightSlider.ValueChanged += (_, _) => { HeightValue.Text = $"{(int)HeightSlider.Value} px"; Save(); };
        OpacitySlider.ValueChanged += (_, _) => { OpacityValue.Text = $"{(int)OpacitySlider.Value}%"; Save(); };
        MaterialCombo.SelectionChanged += (_, _) => Save();
    }

    private void ModuleToggled(object sender, RoutedEventArgs e) => Save();
    private void AlignmentChanged(object sender, SelectionChangedEventArgs e) { if (_ready) Save(); }

    private void MoveUpClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) App.MainViewModel.MoveMonobarModule(id, -1);
    }

    private void MoveDownClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) App.MainViewModel.MoveMonobarModule(id, 1);
    }

    private void Save()
    {
        if (!_ready) return;
        var vm = App.MainViewModel;
        vm.Config.MonobarEnabled = EnabledToggle.IsOn;
        vm.Config.MonobarHeight = (int)HeightSlider.Value;
        vm.Config.MonobarOpacity = (int)OpacitySlider.Value;
        if (MaterialCombo.SelectedItem is string material) vm.Config.MonobarMaterial = material;
        _ = vm.SaveConfigAsync();
    }
}
