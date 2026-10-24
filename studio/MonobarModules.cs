using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Monolith.Studio.Models;
using Monolith.Studio.Services;
using Windows.Storage;
using Windows.Devices.Radios;
using Windows.Media.Control;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;

namespace Monolith.Studio;

internal interface IMonobarModule
{
    string Id { get; }
    UIElement Element { get; }
    void Refresh();
    void Dispose();
}

internal abstract class MonobarModuleBase : IMonobarModule
{
    // Keep pills inset from the 32 px default bar so they have a visible 4 px breathing gap above and below.
    protected const double ModuleHeight = 24;
    protected readonly MainWindow Owner;
    protected MonobarModuleBase(MainWindow owner) => Owner = owner;
    public abstract string Id { get; }
    public abstract UIElement Element { get; }
    public virtual void Refresh() { }
    public virtual void Dispose() { }

    protected static SolidColorBrush Brush(byte alpha, byte red = 255, byte green = 255, byte blue = 255) => new(Windows.UI.Color.FromArgb(alpha, red, green, blue));

    internal static SolidColorBrush AccentBrush(byte? alpha = null) => MonobarAccent.Brush(alpha ?? 255);

    protected static void ApplyAccent(Slider slider)
    {
        var accent = AccentBrush();
        slider.Resources["SliderThumbBackground"] = accent;
        slider.Resources["SliderThumbBackgroundPointerOver"] = accent;
        slider.Resources["SliderThumbBackgroundPressed"] = accent;
        slider.Resources["SliderTrackValueFill"] = accent;
        slider.Resources["SliderTrackValueFillPointerOver"] = accent;
        slider.Resources["SliderTrackValueFillPressed"] = accent;
    }

    protected static Button IconButton(string glyph, string tooltip, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 15 },
            Width = 34, Height = ModuleHeight, MinHeight = 0, MaxHeight = ModuleHeight,
            Padding = new Thickness(0), Background = Brush(22), BorderBrush = Brush(20), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7), VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += click;
        return button;
    }
}

internal static class MonobarAccent
{
    private static readonly Dictionary<byte, SolidColorBrush> Brushes = new();

    static MonobarAccent()
    {
        AccentColorService.AccentChanged += UpdateBrushes;
    }

    public static SolidColorBrush Brush(byte alpha)
    {
        if (Brushes.TryGetValue(alpha, out var brush)) return brush;
        Windows.UI.Color color = AccentColorService.GetAccentColor();
        color.A = alpha;
        brush = new SolidColorBrush(color);
        Brushes[alpha] = brush;
        return brush;
    }

    private static void UpdateBrushes(Windows.UI.Color color)
    {
        foreach (var (alpha, brush) in Brushes)
        {
            var adjusted = color;
            adjusted.A = alpha;
            brush.Color = adjusted;
        }
    }
}

internal sealed class LauncherMonobarModule : MonobarModuleBase
{
    public LauncherMonobarModule(MainWindow owner) : base(owner) => Element = IconButton("\uE721", "Search apps and actions", (_, _) => Owner.ToggleLauncher());
    public override string Id => "launcher";
    public override UIElement Element { get; }
}

internal sealed class SystemTrayMonobarModule : MonobarModuleBase
{
    public SystemTrayMonobarModule(MainWindow owner) : base(owner)
    {
        Element = IconButton("\uE70D", "Open Windows notification area", (_, _) => _ = OpenNativeTrayAsync());
    }

    public override string Id => "tray-menu";
    public override UIElement Element { get; }

    private static async Task OpenNativeTrayAsync()
    {
        const byte keyUp = 0x02;
        const byte leftWindows = 0x5B;
        const byte bKey = 0x42;
        const byte enter = 0x0D;
        KeybdEvent(leftWindows, 0, 0, UIntPtr.Zero);
        KeybdEvent(bKey, 0, 0, UIntPtr.Zero);
        KeybdEvent(bKey, 0, keyUp, UIntPtr.Zero);
        KeybdEvent(leftWindows, 0, keyUp, UIntPtr.Zero);
        await Task.Delay(120);
        KeybdEvent(enter, 0, 0, UIntPtr.Zero);
        KeybdEvent(enter, 0, keyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll", EntryPoint = "keybd_event")] private static extern void KeybdEvent(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}

internal sealed class WorkspacesMonobarModule : MonobarModuleBase
{
    private readonly Button[] _buttons = new Button[5];
    public WorkspacesMonobarModule(MainWindow owner) : base(owner)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 1; i <= 5; i++)
        {
            int workspace = i;
            var button = new Button
            {
                Width = 18, Height = 24, MinWidth = 0, MinHeight = 0, MaxHeight = 24, Padding = new Thickness(0),
                Background = Brush(0), BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(button, $"Switch to workspace {workspace}");
            button.Click += async (_, _) => await App.MainViewModel.SwitchWorkspaceAsync(workspace);
            _buttons[i - 1] = button;
            panel.Children.Add(button);
        }
        Element = new Border { Height = ModuleHeight, Padding = new Thickness(6, 0, 6, 0), Background = Brush(18), CornerRadius = new CornerRadius(8), Child = panel, VerticalAlignment = VerticalAlignment.Center };
        Refresh();
    }

    public override string Id => "workspaces";
    public override UIElement Element { get; }
    public override void Refresh()
    {
        int active = App.MainViewModel.ActiveWorkspace;
        for (int i = 0; i < _buttons.Length; i++)
        {
            bool selected = i + 1 == active;
            _buttons[i].Width = 18;
            _buttons[i].Height = 18;
            _buttons[i].Background = Brush(0);
            _buttons[i].Content = new Border
            {
                Width = selected ? 18 : 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = selected ? AccentBrush() : Brush(115),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ToolTipService.SetToolTip(_buttons[i], selected ? $"Workspace {i + 1} (active)" : $"Workspace {i + 1}");
        }
    }
}

internal sealed class WindowTitleMonobarModule : MonobarModuleBase
{
    private readonly TextBlock _title = new()
    {
        FontSize = 12,
        FontWeight = Microsoft.UI.Text.FontWeights.Medium,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 340
    };
    private string _lastTitle = string.Empty;

    public WindowTitleMonobarModule(MainWindow owner) : base(owner)
    {
        Element = new Border
        {
            Height = ModuleHeight,
            MaxWidth = 360,
            Padding = new Thickness(10, 0, 10, 0),
            Background = Brush(22),
            BorderBrush = Brush(20),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = _title,
            VerticalAlignment = VerticalAlignment.Center
        };
        Refresh();
    }

    public override string Id => "window-title";
    public override UIElement Element { get; }

    public override void Refresh()
    {
        IntPtr foreground = GetForegroundWindow();
        int length = foreground == IntPtr.Zero ? 0 : Math.Clamp(GetWindowTextLength(foreground), 0, 2048);
        var buffer = new StringBuilder(length + 1);
        if (length > 0) GetWindowText(foreground, buffer, buffer.Capacity);
        string title = string.IsNullOrWhiteSpace(buffer.ToString()) ? "Desktop" : buffer.ToString().Trim();
        if (string.Equals(title, _lastTitle, StringComparison.Ordinal)) return;

        _lastTitle = title;
        _title.Text = title;
        ToolTipService.SetToolTip(Element, title);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
}

internal sealed class ClockMonobarModule : MonobarModuleBase
{
    private readonly MonobarModuleConfig _config;
    private readonly TextBlock _clock = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _weekday = new() { FontSize = 13, Opacity = 0.72 };
    private readonly TextBlock _selectedDate = new() { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock _monthLabel = new() { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _todaySummary = new() { FontSize = 13, Opacity = 0.75 };
    private readonly TextBlock _localClock = new() { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium };
    private readonly Grid _dayGrid = new() { ColumnSpacing = 4, RowSpacing = 4 };
    private readonly MonobarPopupWindow _popup;
    private DateTime _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selectedDay = DateTime.Today;
    private bool _calendarBuilt;

    public ClockMonobarModule(MainWindow owner, MonobarModuleConfig config) : base(owner)
    {
        _config = config;
        var button = new Button
        {
            Height = ModuleHeight, MinHeight = 0, MaxHeight = ModuleHeight,
            Padding = new Thickness(12, 0, 12, 0), Background = Brush(22), BorderBrush = Brush(20), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7), Content = _clock, VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, "Open calendar");
        var content = new StackPanel { Spacing = 8, Width = 370 };
        content.Children.Add(_weekday);
        content.Children.Add(_selectedDate);

        var monthHeader = new Grid { Height = 36 };
        monthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        monthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        monthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var previous = new Button { Content = new FontIcon { Glyph = "\uE76B", FontSize = 12 }, Width = 34, Height = 32, MinHeight = 0, Padding = new Thickness(0), Background = Brush(0), BorderThickness = new Thickness(0) };
        var next = new Button { Content = new FontIcon { Glyph = "\uE76C", FontSize = 12 }, Width = 34, Height = 32, MinHeight = 0, Padding = new Thickness(0), Background = Brush(0), BorderThickness = new Thickness(0) };
        previous.Click += (_, _) => { _displayMonth = _displayMonth.AddMonths(-1); BuildCalendar(); };
        next.Click += (_, _) => { _displayMonth = _displayMonth.AddMonths(1); BuildCalendar(); };
        monthHeader.Children.Add(previous);
        Grid.SetColumn(_monthLabel, 1); monthHeader.Children.Add(_monthLabel);
        Grid.SetColumn(next, 2); monthHeader.Children.Add(next);
        content.Children.Add(monthHeader);

        var weekdayRow = new Grid { ColumnSpacing = 4 };
        string[] weekdayNames = { "S", "M", "T", "W", "T", "F", "S" };
        for (int i = 0; i < 7; i++)
        {
            weekdayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock { Text = weekdayNames[i], HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11, Opacity = 0.62 };
            Grid.SetColumn(label, i); weekdayRow.Children.Add(label);
        }
        content.Children.Add(weekdayRow);
        for (int i = 0; i < 7; i++) _dayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 6; i++) _dayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        content.Children.Add(_dayGrid);
        content.Children.Add(CalendarInfoCard("Today", _todaySummary));
        var worldClock = new StackPanel { Spacing = 2 };
        worldClock.Children.Add(new TextBlock { Text = "Local time", FontSize = 12, Opacity = 0.65 });
        var locationRow = new Grid();
        locationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        locationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        locationRow.Children.Add(new TextBlock { Text = "Local", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_localClock, 1); locationRow.Children.Add(_localClock);
        worldClock.Children.Add(locationRow);
        content.Children.Add(CalendarInfoCard("World clocks", worldClock));
        _popup = new MonobarPopupWindow(MonobarPopupContent.PopupSurface(content, 410, 0), 410, 0);
        button.Click += (_, _) => ShowCalendar(button);
        Element = button;
        Refresh();
    }

    public override string Id => "clock";
    public override UIElement Element { get; }
    public override void Refresh()
    {
        var now = DateTime.Now;
        _clock.Text = now.ToString(_config.ClockUse24Hour ? "HH:mm  ddd, MMM d" : "h:mm tt  ddd, MMM d");
        _selectedDate.Text = now.ToString("MMMM d, yyyy");
        _weekday.Text = now.ToString("dddd");
        _todaySummary.Text = _selectedDay.Date == DateTime.Today ? "No events" : _selectedDay.ToString("dddd, MMMM d");
        _localClock.Text = now.ToString(_config.ClockUse24Hour ? "HH:mm" : "h:mm tt");
        if (!_calendarBuilt) BuildCalendar();
    }

    public override void Dispose() => _popup.Close();

    private void ShowCalendar(Button button)
    {
        Refresh();
        _popup.ShowBelow(button, centerOnAnchor: true);
    }

    private void BuildCalendar()
    {
        _calendarBuilt = true;
        _monthLabel.Text = _displayMonth.ToString("MMMM");
        _dayGrid.Children.Clear();
        var first = _displayMonth.AddDays(-(int)_displayMonth.DayOfWeek);
        for (int i = 0; i < 42; i++)
        {
            var date = first.AddDays(i);
            bool currentMonth = date.Month == _displayMonth.Month;
            bool selected = date.Date == _selectedDay.Date;
            var dayButton = new Button
            {
                Content = date.Day.ToString("00"),
                Background = selected ? AccentBrush() : Brush(0),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(currentMonth ? (byte)230 : (byte)75, 255, 255, 255)),
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(18),
                Width = 30, Height = 30, MinHeight = 0, Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var selectedDate = date;
            dayButton.Click += (_, _) =>
            {
                _selectedDay = selectedDate;
                _displayMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
                Refresh();
            };
            Grid.SetRow(dayButton, i / 7); Grid.SetColumn(dayButton, i % 7); _dayGrid.Children.Add(dayButton);
        }
    }

    private static Border CalendarInfoCard(string title, UIElement detail)
    {
        var card = new StackPanel { Spacing = 5 };
        card.Children.Add(new TextBlock { Text = title, FontSize = 12, Opacity = 0.66 });
        card.Children.Add(detail);
        return new Border { Background = Brush(18), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Child = card };
    }
}

internal abstract class PopupMonobarModule : MonobarModuleBase
{
    protected MonobarPopupWindow Popup = null!;
    protected PopupMonobarModule(MainWindow owner, string glyph, string title, bool grouped = false) : base(owner)
    {
        var button = grouped
            ? GroupedIconButton(glyph, title, (_, _) => ShowPopup())
            : IconButton(glyph, title, (_, _) => ShowPopup());
        Element = button;
    }

    public override UIElement Element { get; }
    public override void Dispose() => Popup.Close();
    protected void InitializePopup(UIElement content, int width, int height) =>
        Popup = new MonobarPopupWindow(MonobarPopupContent.PopupSurface(content, width, height), width, height);
    protected void ShowPopup()
    {
        Popup.ShowBelow(Element);
        Refresh();
    }

    private static Button GroupedIconButton(string glyph, string tooltip, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 15 },
            Width = 30,
            Height = ModuleHeight,
            MinHeight = 0,
            MaxHeight = ModuleHeight,
            Padding = new Thickness(0),
            Background = Brush(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += click;
        return button;
    }
}

internal sealed class WifiMonobarModule : PopupMonobarModule
{
    private readonly TextBlock _state = new() { FontSize = 13, Opacity = 0.72 };
    private readonly Button _toggle = new() { MinWidth = 92, Height = 32, MinHeight = 0, Padding = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private bool? _enabled;
    public WifiMonobarModule(MainWindow owner, bool grouped = false) : base(owner, "\uE701", "Wi-Fi", grouped)
    {
        var content = new StackPanel { Width = 300, Spacing = 12 };
        content.Children.Add(MonobarPopupContent.Header("\uE701", "Wi-Fi", "Network connection"));
        var status = MonobarPopupContent.Card();
        var row = MonobarPopupContent.Row();
        var details = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(new TextBlock { Text = "Connection", FontSize = 12, Opacity = 0.62 });
        details.Children.Add(_state);
        row.Children.Add(details);
        Grid.SetColumn(_toggle, 1);
        row.Children.Add(_toggle);
        status.Child = row;
        content.Children.Add(status);
        InitializePopup(content, 340, 0);
        _toggle.Click += async (_, _) =>
        {
            var state = await MonobarSystemStatus.SetRadioStateAsync(RadioKind.WiFi, !(_enabled ?? false));
            if (state.HasValue) _enabled = state;
            Refresh();
        };
        _ = LoadStateAsync();
    }
    public override string Id => "wifi";
    public override void Refresh()
    {
        if (!Popup.IsVisible) return;
        var network = MonobarSystemStatus.GetNetworkStatus();
        _state.Text = _enabled.HasValue ? $"{(_enabled.Value ? "On" : "Off")} · {network.Label}" : "Wi-Fi status unavailable";
        _toggle.IsEnabled = _enabled.HasValue;
        _toggle.Content = _enabled == true ? "Turn off" : "Turn on";
    }
    private async System.Threading.Tasks.Task LoadStateAsync() { _enabled = await MonobarSystemStatus.GetRadioStateAsync(RadioKind.WiFi); Refresh(); }
}

internal sealed class VolumeMonobarModule : PopupMonobarModule
{
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 100, StepFrequency = 1, Height = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _value = new() { FontSize = 13 };
    private readonly TextBlock _sourcesEmpty = new() { Text = "No app audio sessions", FontSize = 12, Opacity = 0.62, Margin = new Thickness(0, 4, 0, 2) };
    private readonly StackPanel _sourcesPanel = new() { Spacing = 8 };
    private readonly Dictionary<string, AudioSourceRow> _sourceRows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _setTimer;
    private bool _updating;
    private bool _dragging;
    private bool _reading;
    private bool _disposed;
    private int _pendingValue;
    private DateTime _lastRead = DateTime.MinValue;
    private DateTime _lastSourcesRead = DateTime.MinValue;
    private bool _sourcesReading;
    public VolumeMonobarModule(MainWindow owner, bool grouped = false) : base(owner, "\uE995", "Volume", grouped)
    {
        var content = new StackPanel { Width = 300, Spacing = 12 };
        content.Children.Add(MonobarPopupContent.Header("\uE995", "Volume", "Output level"));
        var card = MonobarPopupContent.Card();
        var row = MonobarPopupContent.Row();
        row.Children.Add(new TextBlock { Text = "Volume", FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        _value.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _value.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_value, 1);
        row.Children.Add(_value);
        card.Child = new StackPanel { Spacing = 8, Children = { row, _slider } };
        content.Children.Add(card);
        content.Children.Add(new TextBlock { Text = "Apps", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(2, 2, 0, 0) });
        var sourcesCard = MonobarPopupContent.Card();
        var sourceScroll = new ScrollViewer { Content = _sourcesPanel, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var sourceList = new StackPanel { Spacing = 8, Children = { _sourcesEmpty, sourceScroll } };
        sourcesCard.Child = sourceList;
        content.Children.Add(sourcesCard);
        InitializePopup(content, 340, 0);
        ApplyAccent(_slider);
        _setTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _setTimer.Tick += (_, _) =>
        {
            _setTimer.Stop();
            int value = _pendingValue;
            _ = System.Threading.Tasks.Task.Run(() => MonobarSystemStatus.SetVolumePercent(value));
        };
        _slider.ValueChanged += (_, e) =>
        {
            if (_updating) return;
            _pendingValue = (int)e.NewValue;
            _setTimer.Stop();
            _setTimer.Start();
        };
        _slider.PointerPressed += (_, _) => _dragging = true;
        _slider.PointerReleased += (_, _) => _dragging = false;
        _slider.PointerCanceled += (_, _) => _dragging = false;
        Refresh();
    }
    public override string Id => "volume";
    public override void Refresh()
    {
        if (!Popup.IsVisible) return;
        if (!_dragging && !_reading && DateTime.UtcNow - _lastRead >= TimeSpan.FromSeconds(1))
        {
            _lastRead = DateTime.UtcNow;
            _ = RefreshVolumeAsync();
        }
        if (!_sourcesReading && DateTime.UtcNow - _lastSourcesRead >= TimeSpan.FromSeconds(2))
        {
            _lastSourcesRead = DateTime.UtcNow;
            _ = RefreshSourcesAsync();
        }
    }

    public override void Dispose()
    {
        _disposed = true;
        _setTimer.Stop();
        foreach (var row in _sourceRows.Values) row.Timer.Stop();
        Popup.Close();
    }

    private async System.Threading.Tasks.Task RefreshVolumeAsync()
    {
        _reading = true;
        int value = await System.Threading.Tasks.Task.Run(MonobarSystemStatus.GetVolumePercent);
        _reading = false;
        if (_disposed || !Popup.IsVisible || _dragging) return;
        _updating = true;
        _slider.Value = value;
        _updating = false;
        _value.Text = $"{value}%";
    }

    private async System.Threading.Tasks.Task RefreshSourcesAsync()
    {
        _sourcesReading = true;
        var sources = await System.Threading.Tasks.Task.Run(MonobarSystemStatus.GetAudioSourceVolumes);
        _sourcesReading = false;
        if (_disposed || !Popup.IsVisible) return;

        var present = sources.Select(source => source.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in _sourceRows.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            var row = _sourceRows[removed];
            row.Timer.Stop();
            _sourcesPanel.Children.Remove(row.Container);
            _sourceRows.Remove(removed);
        }
        foreach (var source in sources)
        {
            if (!_sourceRows.TryGetValue(source.Id, out var row))
            {
                row = CreateAudioSourceRow(source);
                _sourceRows[source.Id] = row;
                _sourcesPanel.Children.Add(row.Container);
            }
            row.Name.Text = source.Name;
            if (!row.Dragging)
            {
                row.Updating = true;
                row.Slider.Value = source.VolumePercent;
                row.Value.Text = $"{source.VolumePercent}%";
                row.Updating = false;
            }
        }
        _sourcesEmpty.Visibility = sources.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Popup.ResizeToContent();
    }

    private AudioSourceRow CreateAudioSourceRow(Monolith.Studio.Services.MonobarSystemStatus.AudioSourceVolume source)
    {
        var name = new TextBlock { Text = source.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var value = new TextBlock { Text = $"{source.VolumePercent}%", FontSize = 11, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Right };
        var heading = MonobarPopupContent.Row();
        heading.Children.Add(name);
        Grid.SetColumn(value, 1);
        heading.Children.Add(value);
        var slider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1, Height = 30, MinHeight = 0, Value = source.VolumePercent };
        ApplyAccent(slider);
        var container = new StackPanel { Spacing = 2, Children = { heading, slider } };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var row = new AudioSourceRow(source.Id, container, name, value, slider, timer);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            int volume = (int)slider.Value;
            _ = System.Threading.Tasks.Task.Run(() => MonobarSystemStatus.SetAudioSourceVolume(source.Id, volume));
        };
        slider.ValueChanged += (_, e) =>
        {
            if (row.Updating) return;
            value.Text = $"{(int)e.NewValue}%";
            timer.Stop();
            timer.Start();
        };
        slider.PointerPressed += (_, _) => row.Dragging = true;
        slider.PointerReleased += (_, _) => row.Dragging = false;
        slider.PointerCanceled += (_, _) => row.Dragging = false;
        return row;
    }

    private sealed class AudioSourceRow(string id, UIElement container, TextBlock name, TextBlock value, Slider slider, DispatcherTimer timer)
    {
        public string Id { get; } = id;
        public UIElement Container { get; } = container;
        public TextBlock Name { get; } = name;
        public TextBlock Value { get; } = value;
        public Slider Slider { get; } = slider;
        public DispatcherTimer Timer { get; } = timer;
        public bool Updating { get; set; }
        public bool Dragging { get; set; }
    }
}

internal sealed class BatteryMonobarModule : PopupMonobarModule
{
    private readonly TextBlock _status = new() { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium };
    private readonly TextBlock _brightnessValue = new() { FontSize = 12, Opacity = 0.72, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Slider _brightness = new() { Minimum = 0, Maximum = 100, StepFrequency = 1, Height = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool _updatingBrightness;
    private bool _draggingBrightness;
    private bool _brightnessLookupInProgress;
    private DateTime _brightnessUpdated = DateTime.MinValue;
    private readonly DispatcherTimer _brightnessSetTimer;
    private int _pendingBrightness;
    private bool _disposed;
    public BatteryMonobarModule(MainWindow owner, bool grouped = false) : base(owner, "\uEBAA", "Battery and brightness", grouped)
    {
        var content = new StackPanel { Width = 300, Spacing = 12 };
        content.Children.Add(MonobarPopupContent.Header("\uEBAA", "Power", "Battery and display"));
        var batteryCard = MonobarPopupContent.Card();
        var batteryRow = MonobarPopupContent.Row();
        batteryRow.Children.Add(new FontIcon { Glyph = "\uEBAA", FontSize = 16, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_status, 1);
        batteryRow.Children.Add(_status);
        batteryCard.Child = batteryRow;
        content.Children.Add(batteryCard);
        var brightnessCard = MonobarPopupContent.Card();
        var brightnessRow = MonobarPopupContent.Row();
        brightnessRow.Children.Add(new TextBlock { Text = "Brightness", FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_brightnessValue, 1);
        brightnessRow.Children.Add(_brightnessValue);
        brightnessCard.Child = new StackPanel { Spacing = 8, Children = { brightnessRow, _brightness } };
        content.Children.Add(brightnessCard);
        InitializePopup(content, 340, 0);
        ApplyAccent(_brightness);
        _brightnessSetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _brightnessSetTimer.Tick += (_, _) =>
        {
            _brightnessSetTimer.Stop();
            int value = _pendingBrightness;
            _ = System.Threading.Tasks.Task.Run(() => MonobarSystemStatus.SetBrightnessPercent(value));
        };
        _brightness.ValueChanged += (_, e) =>
        {
            if (_updatingBrightness) return;
            _pendingBrightness = (int)e.NewValue;
            _brightnessSetTimer.Stop();
            _brightnessSetTimer.Start();
        };
        _brightness.PointerPressed += (_, _) => _draggingBrightness = true;
        _brightness.PointerReleased += (_, _) => _draggingBrightness = false;
        _brightness.PointerCanceled += (_, _) => _draggingBrightness = false;
        Refresh();
    }
    public override string Id => "battery";
    public override void Refresh()
    {
        if (!Popup.IsVisible) return;
        var status = MonobarSystemStatus.GetBatteryStatus();
        _status.Text = status.HasBattery ? $"{status.Percent}% · {(status.Charging ? "Charging" : "On battery")}" : "Connected to power";
        if (!_draggingBrightness && !_brightnessLookupInProgress && DateTime.UtcNow - _brightnessUpdated > TimeSpan.FromSeconds(10))
        {
            _brightnessUpdated = DateTime.UtcNow;
            _ = RefreshBrightnessAsync();
        }
    }

    public override void Dispose()
    {
        _disposed = true;
        _brightnessSetTimer.Stop();
        Popup.Close();
    }

    private async System.Threading.Tasks.Task RefreshBrightnessAsync()
    {
        _brightnessLookupInProgress = true;
        int? brightness = await System.Threading.Tasks.Task.Run(MonobarSystemStatus.GetBrightnessPercent);
        _brightnessLookupInProgress = false;
        if (_disposed || _draggingBrightness) return;
        if (brightness.HasValue)
        {
            _updatingBrightness = true;
            _brightness.Value = brightness.Value;
            _updatingBrightness = false;
            _brightness.IsEnabled = true;
            _brightnessValue.Text = $"{brightness.Value}%";
        }
        else
        {
            _brightness.IsEnabled = false;
            _brightnessValue.Text = "Unavailable";
        }
    }
}

internal sealed class SystemStatusMonobarModule : MonobarModuleBase
{
    private readonly PopupMonobarModule[] _controls;

    public SystemStatusMonobarModule(MainWindow owner) : base(owner)
    {
        var wifi = new WifiMonobarModule(owner, grouped: true);
        var volume = new VolumeMonobarModule(owner, grouped: true);
        var battery = new BatteryMonobarModule(owner, grouped: true);
        _controls = new PopupMonobarModule[] { wifi, volume, battery };
        var icons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var control in _controls)
            icons.Children.Add(control.Element);

        Element = new Border
        {
            Height = ModuleHeight,
            Padding = new Thickness(3, 0, 3, 0),
            Background = Brush(22),
            BorderBrush = Brush(20),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = icons,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    public override string Id => "system-status";
    public override UIElement Element { get; }

    public override void Refresh()
    {
        foreach (var control in _controls)
            control.Refresh();
    }

    public override void Dispose()
    {
        foreach (var control in _controls)
            control.Dispose();
    }
}

internal sealed class NetworkSpeedMonobarModule : PopupMonobarModule
{
    private readonly TextBlock _down = new() { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock _up = new() { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    public NetworkSpeedMonobarModule(MainWindow owner) : base(owner, "\uE774", "Network speed")
    {
        var content = new StackPanel { Width = 300, Spacing = 12 };
        content.Children.Add(MonobarPopupContent.Header("\uE774", "Network", "Live traffic"));
        var rates = new Grid { ColumnSpacing = 10 };
        rates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rates.Children.Add(RateCard("\uE896", "Download", _down));
        var upload = RateCard("\uE898", "Upload", _up);
        Grid.SetColumn(upload, 1);
        rates.Children.Add(upload);
        content.Children.Add(rates);
        InitializePopup(content, 340, 0);
        Refresh();
    }
    public override string Id => "network-speed";
    public override void Refresh()
    {
        if (!Popup.IsVisible) return;
        var (down, up) = MonobarSystemStatus.GetNetworkRates();
        _down.Text = down;
        _up.Text = up;
    }
    private static Border RateCard(string glyph, string label, TextBlock value)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, Opacity = 0.75 });
        body.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.65 });
        body.Children.Add(value);
        var card = MonobarPopupContent.Card();
        card.Child = body;
        return card;
    }
}

internal sealed class MediaMonobarModule : PopupMonobarModule
{
    private const string Font = "Segoe MDL2 Assets";
    private readonly Image _cover = new() { Width = 72, Height = 72, Stretch = Stretch.UniformToFill };
    private readonly TextBlock _source = new() { Text = "Now playing", FontSize = 12, Opacity = 0.66 };
    private readonly TextBlock _title = new() { Text = "No active media", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { FontSize = 12, Opacity = 0.68, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Slider _position = new() { Minimum = 0, Maximum = 100, StepFrequency = 0.1, Height = 28, MinHeight = 0 };
    private readonly TextBlock _elapsed = new() { Text = "0:00", FontSize = 10, Opacity = 0.68 };
    private readonly TextBlock _duration = new() { Text = "0:00", FontSize = 10, Opacity = 0.68, HorizontalTextAlignment = TextAlignment.Right };
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly Button _next;
    private GlobalSystemMediaTransportControlsSession? _session;
    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _refreshInProgress;
    private bool _updatingPosition;
    private string _currentTrackKey = string.Empty;
    private int _coverLoadVersion;
    private TimeSpan _trackStart;
    private TimeSpan _trackEnd;

    public MediaMonobarModule(MainWindow owner) : base(owner, "\uE768", "Media controls")
    {
        var coverFrame = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(8), Background = Brush(24), Child = _cover, VerticalAlignment = VerticalAlignment.Center };
        var metadata = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        metadata.Children.Add(_title);
        metadata.Children.Add(_artist);
        var trackRow = new Grid { ColumnSpacing = 12 };
        trackRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        trackRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        trackRow.Children.Add(coverFrame);
        Grid.SetColumn(metadata, 1);
        trackRow.Children.Add(metadata);

        _previous = CreateTransportButton("\uE892", "Previous track", 34);
        _playPause = CreateTransportButton("\uE768", "Play", 38);
        _next = CreateTransportButton("\uE893", "Next track", 34);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        controls.Children.Add(_previous);
        controls.Children.Add(_playPause);
        controls.Children.Add(_next);

        var timeline = new Grid { ColumnSpacing = 10 };
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        timeline.Children.Add(_elapsed);
        Grid.SetColumn(_position, 1);
        timeline.Children.Add(_position);
        Grid.SetColumn(_duration, 2);
        timeline.Children.Add(_duration);

        var content = new StackPanel { Width = 360, Spacing = 10 };
        content.Children.Add(_source);
        var card = MonobarPopupContent.Card();
        var player = new StackPanel { Spacing = 8 };
        player.Children.Add(trackRow);
        player.Children.Add(timeline);
        player.Children.Add(controls);
        card.Child = player;
        content.Children.Add(card);
        InitializePopup(content, 396, 0);
        ApplyAccent(_position);

        _previous.Click += async (_, _) => await SendCommandAsync(async () => { await _session!.TrySkipPreviousAsync(); });
        _next.Click += async (_, _) => await SendCommandAsync(async () => { await _session!.TrySkipNextAsync(); });
        _playPause.Click += async (_, _) =>
        {
            if (_session is null) return;
            if (_session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                await _session.TryPauseAsync();
            else await _session.TryPlayAsync();
            _lastRefresh = DateTime.MinValue;
            Refresh();
        };
        _position.PointerReleased += async (_, _) => await SeekAsync();
        _position.KeyUp += async (_, _) => await SeekAsync();
        _ = RefreshSessionAsync();
    }

    public override string Id => "media";

    public override void Refresh()
    {
        if (!Popup.IsVisible || _refreshInProgress || DateTime.UtcNow - _lastRefresh < TimeSpan.FromSeconds(1)) return;
        _lastRefresh = DateTime.UtcNow;
        _ = RefreshSessionAsync();
    }

    private async System.Threading.Tasks.Task RefreshSessionAsync()
    {
        if (_refreshInProgress) return;
        _refreshInProgress = true;
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _session = manager.GetCurrentSession();
            if (_session is null)
            {
                SetTrack(string.Empty, "No active media", "", null);
                _source.Text = "Now playing";
                _trackStart = _trackEnd = TimeSpan.Zero;
                SetTimeline(TimeSpan.Zero, TimeSpan.Zero);
                UpdatePlaybackState(false);
                return;
            }

            var properties = await _session.TryGetMediaPropertiesAsync();
            var title = string.IsNullOrWhiteSpace(properties.Title) ? "Unknown title" : properties.Title;
            var artist = properties.Artist ?? string.Empty;
            var key = $"{_session.SourceAppUserModelId}|{title}|{artist}";
            _source.Text = "Now playing";
            SetTrack(key, title, artist, properties.Thumbnail);
            var playing = _session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            UpdatePlaybackState(playing);
            var timeline = _session.GetTimelineProperties();
            _trackStart = timeline.StartTime;
            _trackEnd = timeline.EndTime;
            SetTimeline(timeline.Position, _trackEnd - _trackStart);
        }
        catch (Exception ex)
        {
            App.LogStartup("Unable to refresh Monobar media controls", ex);
            SetTrack("unavailable", "Media unavailable", "", null);
            UpdatePlaybackState(false);
        }
        finally { _refreshInProgress = false; }
    }

    private void SetTimeline(TimeSpan position, TimeSpan duration)
    {
        _updatingPosition = true;
        var total = Math.Max(0, duration.TotalSeconds);
        var current = Math.Clamp(position.TotalSeconds, 0, total);
        _position.IsEnabled = _session is not null && total > 0;
        _position.Value = total > 0 ? current / total * 100 : 0;
        _elapsed.Text = FormatTime(TimeSpan.FromSeconds(current));
        _duration.Text = FormatTime(TimeSpan.FromSeconds(total));
        _updatingPosition = false;
    }

    private async System.Threading.Tasks.Task SeekAsync()
    {
        if (_updatingPosition || _session is null || _trackEnd <= _trackStart) return;
        var target = _trackStart + TimeSpan.FromTicks((long)((_trackEnd - _trackStart).Ticks * _position.Value / 100));
        try { await _session.TryChangePlaybackPositionAsync(target.Ticks); }
        catch (Exception ex) { App.LogStartup("Unable to seek Monobar media", ex); }
    }

    private async System.Threading.Tasks.Task SendCommandAsync(Func<System.Threading.Tasks.Task> command)
    {
        if (_session is null) return;
        try { await command(); }
        catch (Exception ex) { App.LogStartup("Unable to control Monobar media", ex); }
        _lastRefresh = DateTime.MinValue;
        Refresh();
    }

    private void SetTrack(string key, string title, string artist, Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail)
    {
        if (key == _currentTrackKey) return;
        _currentTrackKey = key;
        _title.Text = title;
        _artist.Text = artist;
        _cover.Source = null;
        var version = ++_coverLoadVersion;
        if (thumbnail is not null) _ = LoadCoverAsync(thumbnail, version);
    }

    private async System.Threading.Tasks.Task LoadCoverAsync(Windows.Storage.Streams.IRandomAccessStreamReference thumbnail, int version)
    {
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var bitmap = new BitmapImage { DecodePixelWidth = 192 };
            await bitmap.SetSourceAsync(stream);
            if (version == _coverLoadVersion) _cover.Source = bitmap;
        }
        catch { }
    }

    private void UpdatePlaybackState(bool isPlaying)
    {
        _playPause.Content = new FontIcon { Glyph = isPlaying ? "\uE769" : "\uE768", FontSize = 13, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(Font) };
        ToolTipService.SetToolTip(_playPause, isPlaying ? "Pause" : "Play");
        _previous.IsEnabled = _playPause.IsEnabled = _next.IsEnabled = _session is not null;
    }

    private static string FormatTime(TimeSpan value) => $"{(int)value.TotalMinutes}:{value.Seconds:00}";

    private static Button CreateTransportButton(string glyph, string tooltip, double size)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 13, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(Font) },
            Width = size, Height = size, MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(0), Background = Brush(0), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(size / 2), VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }
}

internal sealed class WallpaperMonobarModule : MonobarModuleBase
{
    private readonly MainWindow _owner;
    private readonly MonobarPopupWindow _popup;
    private readonly MonobarModuleConfig _config;
    private readonly Grid _wallpaperGrid = new()
    {
        ColumnSpacing = 10,
        RowSpacing = 10,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly ScrollViewer _wallpaperScroll = new() { MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { FontSize = 12, Opacity = 0.68, TextWrapping = TextWrapping.Wrap };
    private readonly System.Threading.SemaphoreSlim _previewGate = new(4);
    private readonly Dictionary<string, Border> _wallpaperCards = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedWallpaperPath;
    private bool _hasLoadedWallpapers;

    public WallpaperMonobarModule(MainWindow owner, MonobarModuleConfig config) : base(owner)
    {
        _owner = owner;
        _config = config;
        var choose = new Button { Content = "Choose image…", HorizontalAlignment = HorizontalAlignment.Stretch, Height = 34, MinHeight = 0 };
        choose.Click += async (_, _) => await ChooseWallpaperAsync();
        var refresh = new Button { Content = "Refresh", Height = 34, MinHeight = 0 };
        refresh.Click += async (_, _) => await LoadWallpapersAsync();
        var apply = new Button { Content = "Set wallpaper", Height = 34, MinHeight = 0 };
        apply.Click += (_, _) => ApplySelectedWallpaper();
        var content = new StackPanel { Width = 520, Spacing = 12 };
        content.Children.Add(MonobarPopupContent.Header("\uE91B", "Wallpaper", "Personalize your desktop"));
        var card = MonobarPopupContent.Card();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(refresh);
        actions.Children.Add(apply);
        _wallpaperGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _wallpaperGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _wallpaperScroll.Content = _wallpaperGrid;
        card.Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Select a preview to apply a wallpaper from your configured folder.", FontSize = 13, Opacity = 0.75, TextWrapping = TextWrapping.Wrap },
                _wallpaperScroll,
                _status,
                actions,
                choose
            }
        };
        content.Children.Add(card);
        _popup = new MonobarPopupWindow(MonobarPopupContent.PopupSurface(content, 560, 0), 560, 0);
        Element = IconButton("\uE91B", "Wallpaper picker", (_, _) => ShowPopup());
    }
    public override string Id => "wallpaper";
    public override UIElement Element { get; }
    public override void Dispose() => _popup.Close();
    private void ShowPopup()
    {
        _popup.ShowBelow(Element);
        if (!_hasLoadedWallpapers) _ = LoadWallpapersAsync();
    }

    private async System.Threading.Tasks.Task LoadWallpapersAsync()
    {
        string folder = _config.WallpaperFolder?.Trim() ?? string.Empty;
        _status.Text = string.IsNullOrWhiteSpace(folder) ? "Choose a wallpaper folder in Monobar settings." : "Loading images…";
        _wallpaperGrid.Children.Clear();
        _wallpaperGrid.RowDefinitions.Clear();
        _wallpaperCards.Clear();
        _selectedWallpaperPath = null;
        _hasLoadedWallpapers = false;
        if (string.IsNullOrWhiteSpace(folder)) return;

        string[] files;
        try
        {
            files = await System.Threading.Tasks.Task.Run(() =>
            {
                if (!Directory.Exists(folder)) return Array.Empty<string>();
                var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
                return Directory.EnumerateFiles(folder)
                    .Where(path => extensions.Contains(Path.GetExtension(path)))
                    .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                    .Take(48)
                    .ToArray();
            });
        }
        catch (Exception ex)
        {
            _status.Text = $"Unable to read this folder: {ex.Message}";
            return;
        }

        var previewLoads = new List<System.Threading.Tasks.Task>(files.Length);
        for (int i = 0; i < files.Length; i++)
        {
            int row = i / 2;
            if (row >= _wallpaperGrid.RowDefinitions.Count)
                _wallpaperGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string path = files[i];
            var name = new TextBlock
            {
                Text = Path.GetFileName(path),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 5, 8, 6),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 245))
            };
            var preview = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var tile = new Grid();
            tile.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tile.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var namePlate = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(205, 20, 20, 20)),
                Child = name
            };
            tile.Children.Add(preview);
            Grid.SetRow(namePlate, 1);
            tile.Children.Add(namePlate);
            var cardBorder = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 38, 38)),
                BorderBrush = Brush(26),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = tile
            };
            cardBorder.SizeChanged += (_, _) =>
            {
                if (preview.Source is BitmapImage bitmap && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                    SetPreviewSize(preview, cardBorder.ActualWidth - 2, bitmap.PixelWidth, bitmap.PixelHeight);
            };
            var button = new Button
            {
                Content = cardBorder,
                Padding = new Thickness(0),
                MinHeight = 0,
                Background = Brush(0),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag = path
            };
            button.Click += (_, _) => SelectWallpaper(path);
            Grid.SetRow(button, row);
            Grid.SetColumn(button, i % 2);
            _wallpaperGrid.Children.Add(button);
            _wallpaperCards[path] = cardBorder;
            previewLoads.Add(LoadPreviewAsync(preview, cardBorder, path));
        }

        await System.Threading.Tasks.Task.WhenAll(previewLoads);
        _hasLoadedWallpapers = true;
        _status.Text = files.Length == 0
            ? $"No supported images found in {folder}."
            : $"Showing {files.Length} wallpaper{(files.Length == 1 ? string.Empty : "s")}.";
        _popup.ResizeToContent();
    }

    private void SelectWallpaper(string path)
    {
        _selectedWallpaperPath = path;
        foreach (var (file, card) in _wallpaperCards)
            card.BorderBrush = file.Equals(path, StringComparison.OrdinalIgnoreCase)
                ? AccentBrush()
                : Brush(26);
        _status.Text = Path.GetFileName(path);
    }

    private void ApplySelectedWallpaper()
    {
        if (_selectedWallpaperPath is { } path && File.Exists(path))
        {
            NativeWallpaper.Set(path);
            _status.Text = $"Applied {Path.GetFileName(path)}.";
        }
    }

    private async System.Threading.Tasks.Task LoadPreviewAsync(Image preview, Border card, string path)
    {
        await _previewGate.WaitAsync();
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage { DecodePixelWidth = 512 };
            await bitmap.SetSourceAsync(stream);
            preview.Source = bitmap;
            SetPreviewSize(preview, card.ActualWidth - 2, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        catch { }
        finally { _previewGate.Release(); }
    }

    private static void SetPreviewSize(Image preview, double width, int pixelWidth, int pixelHeight)
    {
        if (width <= 0 || pixelWidth == 0 || pixelHeight == 0) return;
        preview.Width = width;
        preview.Height = width * pixelHeight / pixelWidth;
    }
    private async System.Threading.Tasks.Task ChooseWallpaperAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg"); picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".bmp");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_owner));
            var file = await picker.PickSingleFileAsync();
            if (file is not null) NativeWallpaper.Set(file.Path);
        }
        catch { }
    }
}

internal static class MonobarPopupContent
{
    public static Grid Row() => new()
    {
        ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } },
        VerticalAlignment = VerticalAlignment.Center
    };

    public static Border Card(UIElement? content = null) => new()
    {
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(25, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 12, 14, 12),
        Child = content
    };

    public static Grid Header(string glyph, string title, string subtitle)
    {
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(11),
            Background = MonobarModuleBase.AccentBrush(42),
            Child = new FontIcon { Glyph = glyph, FontSize = 17, Foreground = MonobarModuleBase.AccentBrush(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        header.Children.Add(icon);
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        labels.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Opacity = 0.66 });
        Grid.SetColumn(labels, 1);
        header.Children.Add(labels);
        return header;
    }

    public static Border PopupSurface(UIElement content, double width, double height) => new()
    {
        Width = width,
        Height = height > 0 ? height : double.NaN,
        // Use a light tint over Desktop Acrylic so the blur remains visible.
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(22, 26, 26, 26)),
        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(14),
        Child = content
    };
}

internal static class NativeWallpaper
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);
    public static void Set(string path) => SystemParametersInfo(20, 0, path, 3);
}

internal static class MonobarModuleFactory
{
    public static IMonobarModule? Create(MonobarModuleConfig config, MainWindow owner) => config.Id switch
    {
        "workspaces" => new WorkspacesMonobarModule(owner),
        "clock" => new ClockMonobarModule(owner, config),
        "launcher" => new LauncherMonobarModule(owner),
        "tray-menu" => new SystemTrayMonobarModule(owner),
        "taskbar" => new TaskbarMonobarModule(owner),
        "system-status" => new SystemStatusMonobarModule(owner),
        "network-speed" => new NetworkSpeedMonobarModule(owner),
        "media" => new MediaMonobarModule(owner),
        "wallpaper" => new WallpaperMonobarModule(owner, config),
        "window-title" => new WindowTitleMonobarModule(owner),
        "system-monitor" => new SystemMonitorMonobarModule(owner),
        "task-view" => new TaskViewMonobarModule(owner),
        "quick-settings" => new QuickSettingsMonobarModule(owner),
        "show-desktop" => new ShowDesktopMonobarModule(owner),
        "recycle-bin" => new RecycleBinMonobarModule(owner),
        _ => null
    };
}
