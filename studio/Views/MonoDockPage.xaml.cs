using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Monolith.Studio.Views;

public sealed partial class MonoDockPage : Page
{
    private bool _ready;
    private readonly DispatcherTimer _saveTimer;

    public MonoDockPage()
    {
        InitializeComponent();
        var config = App.MainViewModel.Config;
        EnabledToggle.IsOn = config.MonodockEnabled;
        IconSizeSlider.Value = Math.Clamp(config.MonodockIconSize, 40, 88);
        PinnedAppsBox.Text = string.Join(Environment.NewLine, config.MonodockPinnedApps ?? new());
        UpdateLabels();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
        Loaded += (_, _) => _ready = true;
        Unloaded += (_, _) => Save();
        EnabledToggle.Toggled += (_, _) => QueueSave();
        IconSizeSlider.ValueChanged += (_, _) => { UpdateLabels(); QueueSave(); };
        PinnedAppsBox.LostFocus += (_, _) => QueueSave();
        PinnedAppsBox.TextChanged += (_, _) => QueueSave();
    }

    private void UpdateLabels() => IconSizeValue.Text = $"{(int)Math.Round(IconSizeSlider.Value)} px";

    private void QueueSave()
    {
        if (!_ready) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Save()
    {
        _saveTimer.Stop();
        if (!_ready) return;
        var config = App.MainViewModel.Config;
        config.MonodockEnabled = EnabledToggle.IsOn;
        config.MonodockIconSize = (int)Math.Round(IconSizeSlider.Value);
        config.MonodockPinnedApps = PinnedAppsBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim())
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
        _ = App.MainViewModel.SaveConfigAsync();
        App.MainWindowInstance?.ApplyMonoDockConfiguration();
    }
}
