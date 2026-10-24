using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monolith.Studio.Models;
using Windows.Storage.Pickers;

namespace Monolith.Studio.Views;

public sealed partial class MonobarPage : Page
{
    private bool _ready;
    private bool _hasPendingChanges;
    private string _lastSavedSnapshot = string.Empty;
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _saveTimer;

    public MonobarPage()
    {
        InitializeComponent();
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            if (!_ready) return;
            SyncConfigurationFromControls();
            App.MainWindowInstance?.ApplyMonobarConfiguration();
        };
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => SavePendingChanges();
        Unloaded += (_, _) => SavePendingChanges();
        Loaded += (_, _) =>
        {
            _ready = true;
            _lastSavedSnapshot = CaptureSnapshot();
        };

        var vm = App.MainViewModel;
        EnabledToggle.IsOn = vm.Config.MonobarEnabled;
        HeightSlider.Value = vm.Config.MonobarHeight;
        MaterialCombo.SelectedItem = vm.Config.MonobarMaterial is "Acrylic (Frosted Glass)" or "Pure OLED Dark"
            ? vm.Config.MonobarMaterial
            : "Acrylic (Frosted Glass)";
        InitializeModuleLanes();
        HeightValue.Text = $"{vm.Config.MonobarHeight} px";

        EnabledToggle.Toggled += (_, _) => QueueUpdate();
        HeightSlider.ValueChanged += (_, _) => { HeightValue.Text = $"{(int)HeightSlider.Value} px"; QueueUpdate(); };
        MaterialCombo.SelectionChanged += (_, _) => QueueUpdate();
    }

    private void ModuleToggled(object sender, RoutedEventArgs e) => QueueUpdate();
    private void AlignmentChanged(object sender, SelectionChangedEventArgs e) => QueueUpdate();
    private void ModuleOptionToggled(object sender, RoutedEventArgs e) => QueueUpdate();
    private void ModuleOptionLostFocus(object sender, RoutedEventArgs e) => QueueUpdate();
    private void ModuleConditionChanged(object sender, SelectionChangedEventArgs e) => QueueUpdate();

    private async void BrowseWallpaperFolderClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MonobarModuleConfig module }) return;
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;
            module.WallpaperFolder = folder.Path;
            QueueUpdate();
        }
        catch (Exception ex)
        {
            App.LogStartup("Failed to select the Monobar wallpaper folder", ex);
        }
    }

    private void MoveUpClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            App.MainViewModel.MoveMonobarModule(id, -1);
            QueueUpdate();
        }
    }

    private void MoveDownClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            App.MainViewModel.MoveMonobarModule(id, 1);
            QueueUpdate();
        }
    }

    private void QueueUpdate()
    {
        if (!_ready) return;
        SyncConfigurationFromControls();
        if (CaptureSnapshot() == _lastSavedSnapshot) return;
        _hasPendingChanges = true;
        _previewTimer.Stop();
        _previewTimer.Start();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SyncConfigurationFromControls()
    {
        var vm = App.MainViewModel;
        vm.Config.MonobarEnabled = EnabledToggle.IsOn;
        vm.Config.MonobarHeight = (int)HeightSlider.Value;
        if (MaterialCombo.SelectedItem is string material) vm.Config.MonobarMaterial = material;
        vm.Config.MonobarModules = new List<MonobarModuleConfig>(vm.MonobarModules);
    }

    private void SavePendingChanges()
    {
        _saveTimer.Stop();
        _previewTimer.Stop();
        if (!_hasPendingChanges) return;
        SyncConfigurationFromControls();
        _hasPendingChanges = false;
        _lastSavedSnapshot = CaptureSnapshot();
        _ = App.MainViewModel.SaveConfigAsync();
    }

    private static string CaptureSnapshot()
    {
        var vm = App.MainViewModel;
        string modules = string.Join("|", vm.MonobarModules.OrderBy(module => module.Order)
            .Select(module => $"{module.Id}:{module.Enabled}:{module.Order}:{module.Alignment}:{module.WallpaperFolder}:{module.ClockUse24Hour}:{module.VisibilityCondition}"));
        return $"{vm.Config.MonobarEnabled}:{vm.Config.MonobarHeight}:{vm.Config.MonobarMaterial}|{modules}";
    }
}



