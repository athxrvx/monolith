using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Monolith.Studio.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Monolith.Studio.Views;

public sealed partial class MonobarPage
{
    public ObservableCollection<MonobarModuleConfig> LeftModuleItems { get; } = new();
    public ObservableCollection<MonobarModuleConfig> CenterModuleItems { get; } = new();
    public ObservableCollection<MonobarModuleConfig> RightModuleItems { get; } = new();

    private void InitializeModuleLanes()
    {
        LeftModulesList.ItemsSource = LeftModuleItems;
        CenterModulesList.ItemsSource = CenterModuleItems;
        RightModulesList.ItemsSource = RightModuleItems;
        RefreshModuleLanes();
    }

    private void RefreshModuleLanes()
    {
        ReplaceLaneItems(LeftModuleItems, "Left");
        ReplaceLaneItems(CenterModuleItems, "Center");
        ReplaceLaneItems(RightModuleItems, "Right");
    }

    private static void ReplaceLaneItems(ObservableCollection<MonobarModuleConfig> lane, string alignment)
    {
        lane.Clear();
        foreach (var module in App.MainViewModel.MonobarModules
            .Where(module => module.Enabled && string.Equals(module.Alignment, alignment, StringComparison.OrdinalIgnoreCase))
            .OrderBy(module => module.Order))
            lane.Add(module);
    }

    private void ModuleDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var module = e.Items.OfType<MonobarModuleConfig>().FirstOrDefault();
        if (module is null) return;
        e.Data.SetText(module.Id);
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void ModuleDragOver(object sender, DragEventArgs e)
    {
        if (sender is not ListView target || !e.DataView.Contains(StandardDataFormats.Text)) return;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;

        _ = GetInsertionIndex(target, e, out double markerY);
        var indicator = DropIndicatorFor(target);
        indicator.Margin = new Thickness(8, markerY, 8, 0);
        indicator.Visibility = Visibility.Visible;
    }

    private void ModuleDragLeave(object sender, DragEventArgs e)
    {
        if (sender is ListView target) DropIndicatorFor(target).Visibility = Visibility.Collapsed;
    }

    private Border DropIndicatorFor(ListView target) => target.Name switch
    {
        "LeftModulesList" => LeftDropIndicator,
        "CenterModulesList" => CenterDropIndicator,
        _ => RightDropIndicator
    };

    private int GetInsertionIndex(ListView target, DragEventArgs e, out double markerY)
    {
        double pointerY = e.GetPosition(target).Y;
        for (int index = 0; index < target.Items.Count; index++)
        {
            if (target.ContainerFromIndex(index) is not ListViewItem item) continue;
            double top = item.TransformToVisual(target).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            if (pointerY < top + item.ActualHeight / 2)
            {
                markerY = Math.Max(0, top);
                return index;
            }
        }

        int lastIndex = target.Items.Count - 1;
        if (lastIndex >= 0 && target.ContainerFromIndex(lastIndex) is ListViewItem lastItem)
            markerY = lastItem.TransformToVisual(target).TransformPoint(new Windows.Foundation.Point(0, 0)).Y + lastItem.ActualHeight;
        else
            markerY = 4;
        return target.Items.Count;
    }

    private void LeftLaneDrop(object sender, DragEventArgs e) => HandleLaneDrop(sender, e, "Left");
    private void CenterLaneDrop(object sender, DragEventArgs e) => HandleLaneDrop(sender, e, "Center");
    private void RightLaneDrop(object sender, DragEventArgs e) => HandleLaneDrop(sender, e, "Right");

    private async void HandleLaneDrop(object sender, DragEventArgs e, string alignment)
    {
        if (sender is not ListView target || !e.DataView.Contains(StandardDataFormats.Text)) return;
        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.Move;
        int insertIndex = GetInsertionIndex(target, e, out _);
        LeftDropIndicator.Visibility = Visibility.Collapsed;
        CenterDropIndicator.Visibility = Visibility.Collapsed;
        RightDropIndicator.Visibility = Visibility.Collapsed;

        string id;
        try { id = await e.DataView.GetTextAsync(); }
        catch { return; }

        var module = App.MainViewModel.MonobarModules.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase) && item.Enabled);
        if (module is null) return;

        var sourceLane = LaneFor(module.Alignment);
        var targetLane = LaneFor(alignment);
        int sourceIndex = sourceLane.IndexOf(module);
        if (ReferenceEquals(sourceLane, targetLane) && sourceIndex >= 0 && sourceIndex < insertIndex)
            insertIndex--;
        sourceLane.Remove(module);
        module.Alignment = alignment;
        insertIndex = Math.Clamp(insertIndex, 0, targetLane.Count);
        targetLane.Insert(insertIndex, module);

        PersistLaneOrder();
        QueueUpdate();
    }
    private ObservableCollection<MonobarModuleConfig> LaneFor(string alignment) => alignment switch
    {
        "Center" => CenterModuleItems,
        "Right" => RightModuleItems,
        _ => LeftModuleItems
    };

    private void PersistLaneOrder()
    {
        var vm = App.MainViewModel;
        var active = LeftModuleItems.Concat(CenterModuleItems).Concat(RightModuleItems).ToList();
        var knownActive = new HashSet<string>(active.Select(module => module.Id), StringComparer.OrdinalIgnoreCase);
        active.AddRange(vm.MonobarModules.Where(module => module.Enabled && !knownActive.Contains(module.Id))
            .OrderBy(module => module.Order));
        active.AddRange(vm.MonobarModules.Where(module => !module.Enabled).OrderBy(module => module.Order));

        vm.MonobarModules.Clear();
        for (int index = 0; index < active.Count; index++)
        {
            active[index].Order = index;
            vm.MonobarModules.Add(active[index]);
        }
        vm.Config.MonobarModules = vm.MonobarModules.ToList();
    }

    private void RemoveAppletClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MonobarModuleConfig module }) return;
        LaneFor(module.Alignment).Remove(module);
        App.MainViewModel.MonobarModules.Remove(module);
        PersistLaneOrder();
        QueueUpdate();
    }

    private void ModuleSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MonobarModuleConfig module }) _ = ShowModuleSettingsAsync(module);
    }

    private async System.Threading.Tasks.Task ShowModuleSettingsAsync(MonobarModuleConfig module)
    {
        var content = new StackPanel { Spacing = 12, MinWidth = 420 };
        content.Children.Add(new TextBlock { Text = module.Description, TextWrapping = TextWrapping.Wrap, Opacity = 0.72 });

        var condition = new ComboBox
        {
            Header = "Show this applet",
            ItemsSource = module.AvailableConditions,
            DisplayMemberPath = "Name",
            SelectedValuePath = "Id",
            SelectedValue = module.VisibilityCondition,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        content.Children.Add(condition);

        TextBox? wallpaperFolder = null;
        ToggleSwitch? use24Hour = null;
        if (module.Id == "wallpaper")
        {
            wallpaperFolder = new TextBox { Header = "Wallpaper folder", Text = module.WallpaperFolder, PlaceholderText = "Choose a folder of images" };
            var folderRow = new Grid { ColumnSpacing = 8 };
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            folderRow.Children.Add(wallpaperFolder);
            var browse = new Button { Content = "Browse…", VerticalAlignment = VerticalAlignment.Bottom };
            browse.Click += async (_, _) =>
            {
                string? selectedPath = await PickWallpaperFolderAsync();
                if (selectedPath is not null) wallpaperFolder.Text = selectedPath;
            };
            Grid.SetColumn(browse, 1);
            folderRow.Children.Add(browse);
            content.Children.Add(folderRow);
        }
        else if (module.Id == "clock")
        {
            use24Hour = new ToggleSwitch { Header = "Use 24-hour time", IsOn = module.ClockUse24Hour };
            content.Children.Add(use24Hour);
        }

        var dialog = new ContentDialog
        {
            Title = $"{module.DisplayName} settings",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        module.VisibilityCondition = condition.SelectedValue as string ?? "always";
        if (wallpaperFolder is not null) module.WallpaperFolder = wallpaperFolder.Text.Trim();
        if (use24Hour is not null) module.ClockUse24Hour = use24Hour.IsOn;
        QueueUpdate();
    }

    private async System.Threading.Tasks.Task<string?> PickWallpaperFolderAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception ex)
        {
            App.LogStartup("Failed to select the Monobar wallpaper folder", ex);
            return null;
        }
    }

    private async void AddAppletClicked(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 500 };
        var section = new ComboBox { Header = "Add to section", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        section.Items.Add(new ComboBoxItem { Content = "Left" });
        section.Items.Add(new ComboBoxItem { Content = "Center" });
        section.Items.Add(new ComboBoxItem { Content = "Right" });
        var search = new TextBox { PlaceholderText = "Search applets…", HorizontalAlignment = HorizontalAlignment.Stretch };
        var rows = new StackPanel { Spacing = 6 };
        var scroll = new ScrollViewer { Content = rows, Height = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(section);
        panel.Children.Add(search);
        panel.Children.Add(scroll);

        void BuildAppletRows()
        {
            rows.Children.Clear();
            var vm = App.MainViewModel;
            var activeIds = new HashSet<string>(vm.MonobarModules.Where(module => module.Enabled).Select(module => module.Id), StringComparer.OrdinalIgnoreCase);
            string query = search.Text.Trim();
            var available = MonobarModuleConfig.CreateDefaults()
                .Where(module => !activeIds.Contains(module.Id))
                .Where(module => query.Length == 0
                    || module.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || module.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var applet in available)
            {
                var row = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 2)
                };
                var grid = new Grid { ColumnSpacing = 10 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.Children.Add(new FontIcon { Glyph = applet.IconGlyph, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
                var description = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
                description.Children.Add(new TextBlock { Text = applet.DisplayName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                description.Children.Add(new TextBlock { Text = applet.Description, FontSize = 12, Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(description, 1);
                grid.Children.Add(description);
                var add = new Button { Content = "Add", VerticalAlignment = VerticalAlignment.Center };
                add.Click += (_, _) =>
                {
                    AddApplet(applet.Id, SelectedSection(section));
                    BuildAppletRows();
                    QueueUpdate();
                };
                Grid.SetColumn(add, 2);
                grid.Children.Add(add);
                row.Child = grid;
                rows.Children.Add(row);
            }

            if (available.Count == 0)
                rows.Children.Add(new TextBlock { Text = "No applets match your search.", Opacity = 0.68, Margin = new Thickness(4, 12, 4, 4) });
        }

        search.TextChanged += (_, _) => BuildAppletRows();
        BuildAppletRows();
        var dialog = new ContentDialog
        {
            Title = "Add applet",
            Content = panel,
            CloseButtonText = "Done",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private static string SelectedSection(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Content as string ?? "Left";

    private void AddApplet(string id, string alignment)
    {
        var vm = App.MainViewModel;
        var module = vm.MonobarModules.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (module is null)
        {
            var defaults = MonobarModuleConfig.CreateDefaults().FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (defaults is null) return;
            module = new MonobarModuleConfig
            {
                Id = defaults.Id,
                Enabled = true,
                Order = vm.MonobarModules.Count,
                Alignment = alignment,
                WallpaperFolder = defaults.WallpaperFolder,
                ClockUse24Hour = defaults.ClockUse24Hour,
                VisibilityCondition = defaults.VisibilityCondition
            };
            vm.MonobarModules.Add(module);
        }
        else
        {
            module.Enabled = true;
            module.Alignment = alignment;
        }

        LaneFor(alignment).Add(module);
        PersistLaneOrder();
    }
}




