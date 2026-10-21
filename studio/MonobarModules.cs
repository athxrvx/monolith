using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Monolith.Studio.Services;
using Windows.Devices.Radios;
using Windows.Media.Control;
using Windows.Storage.Pickers;

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

internal sealed class LauncherMonobarModule : MonobarModuleBase
{
    public LauncherMonobarModule(MainWindow owner) : base(owner) => Element = IconButton("\uE721", "Search apps and actions", (_, _) => Owner.ToggleLauncher());
    public override string Id => "launcher";
    public override UIElement Element { get; }
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
                Background = selected
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 205, 255))
                    : Brush(115),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ToolTipService.SetToolTip(_buttons[i], selected ? $"Workspace {i + 1} (active)" : $"Workspace {i + 1}");
        }
    }
}

internal sealed class ClockMonobarModule : MonobarModuleBase
{
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

    public ClockMonobarModule(MainWindow owner) : base(owner)
    {
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
        _clock.Text = now.ToString("h:mm tt  ddd, MMM d");
        _selectedDate.Text = now.ToString("MMMM d, yyyy");
        _weekday.Text = now.ToString("dddd");
        _todaySummary.Text = _selectedDay.Date == DateTime.Today ? "No events" : _selectedDay.ToString("dddd, MMMM d");
        _localClock.Text = now.ToString("h:mm tt");
        BuildCalendar();
    }

    public override void Dispose() => _popup.Close();

    private void ShowCalendar(Button button)
    {
        Refresh();
        _popup.ShowBelow(button, centerOnAnchor: true);
    }

    private void BuildCalendar()
    {
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
                Background = selected ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 105, 55)) : Brush(0),
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
    protected PopupMonobarModule(MainWindow owner, string glyph, string title) : base(owner)
    {
        var button = IconButton(glyph, title, (_, _) => ShowPopup());
        Element = button;
    }

    public override UIElement Element { get; }
    public override void Dispose() => Popup.Close();
    protected void InitializePopup(UIElement content, int width, int height) =>
        Popup = new MonobarPopupWindow(MonobarPopupContent.PopupSurface(content, width, height), width, height);
    protected void ShowPopup()
    {
        Popup.ShowBelow(Element);
    }
}

internal sealed class WifiMonobarModule : PopupMonobarModule
{
    private readonly TextBlock _state = new() { FontSize = 13, Opacity = 0.72 };
    private readonly Button _toggle = new() { MinWidth = 92, Height = 32, MinHeight = 0, Padding = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private bool? _enabled;
    public WifiMonobarModule(MainWindow owner) : base(owner, "\uE701", "Wi-Fi")
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
    private bool _updating;
    public VolumeMonobarModule(MainWindow owner) : base(owner, "\uE995", "Volume")
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
        InitializePopup(content, 340, 0);
        _slider.ValueChanged += (_, e) => { if (!_updating) MonobarSystemStatus.SetVolumePercent((int)e.NewValue); };
        Refresh();
    }
    public override string Id => "volume";
    public override void Refresh() { int value = MonobarSystemStatus.GetVolumePercent(); _updating = true; _slider.Value = value; _updating = false; _value.Text = $"{value}%"; }
}

internal sealed class BatteryMonobarModule : PopupMonobarModule
{
    private readonly TextBlock _status = new() { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium };
    private readonly TextBlock _brightnessValue = new() { FontSize = 12, Opacity = 0.72, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Slider _brightness = new() { Minimum = 0, Maximum = 100, StepFrequency = 1, Height = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool _updatingBrightness;
    private bool _draggingBrightness;
    private DateTime _brightnessUpdated = DateTime.MinValue;
    public BatteryMonobarModule(MainWindow owner) : base(owner, "\uEBAA", "Battery and brightness")
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
        _brightness.ValueChanged += (_, e) => { if (!_updatingBrightness) MonobarSystemStatus.SetBrightnessPercent((int)e.NewValue); };
        _brightness.PointerPressed += (_, _) => _draggingBrightness = true;
        _brightness.PointerReleased += (_, _) => _draggingBrightness = false;
        _brightness.PointerCanceled += (_, _) => _draggingBrightness = false;
        Refresh();
    }
    public override string Id => "battery";
    public override void Refresh()
    {
        var status = MonobarSystemStatus.GetBatteryStatus();
        _status.Text = status.HasBattery ? $"{status.Percent}% · {(status.Charging ? "Charging" : "On battery")}" : "Connected to power";
        if (!_draggingBrightness && DateTime.UtcNow - _brightnessUpdated > TimeSpan.FromSeconds(10))
        {
            int? brightness = MonobarSystemStatus.GetBrightnessPercent();
            _brightnessUpdated = DateTime.UtcNow;
            if (brightness.HasValue)
            {
                _updatingBrightness = true;
                _brightness.Value = brightness.Value;
                _updatingBrightness = false;
                _brightness.IsEnabled = true;
                _brightnessValue.Text = $"{brightness.Value}%";
            }
            else { _brightness.IsEnabled = false; _brightnessValue.Text = "Unavailable"; }
        }
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
    public override void Refresh() { var (down, up) = MonobarSystemStatus.GetNetworkRates(); _down.Text = down; _up.Text = up; }
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
    private readonly TextBlock _nowPlaying = new() { Text = "No active media", TextWrapping = TextWrapping.Wrap };
    private readonly Button _playPause = new() { Content = "Play", MinWidth = 72, Height = 34, MinHeight = 0, Padding = new Thickness(12, 0, 12, 0) };
    private GlobalSystemMediaTransportControlsSession? _session;
    public MediaMonobarModule(MainWindow owner) : base(owner, "\uE768", "Media controls")
    {
        var content = new StackPanel { Width = 300, Spacing = 12 };
        content.Children.Add(MonobarPopupContent.Header("\uE768", "Now playing", "Media controls"));
        var nowPlayingCard = MonobarPopupContent.Card();
        _nowPlaying.FontSize = 14;
        _nowPlaying.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        nowPlayingCard.Child = _nowPlaying;
        content.Children.Add(nowPlayingCard);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var previous = new Button { Content = "\uE892", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"), Width = 36, Height = 34, MinHeight = 0, Padding = new Thickness(0) };
        var next = new Button { Content = "\uE893", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"), Width = 36, Height = 34, MinHeight = 0, Padding = new Thickness(0) };
        previous.Click += async (_, _) => { if (_session is not null) await _session.TrySkipPreviousAsync(); };
        next.Click += async (_, _) => { if (_session is not null) await _session.TrySkipNextAsync(); };
        _playPause.Click += async (_, _) =>
        {
            if (_session is null) return;
            if (_session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                await _session.TryPauseAsync();
            else await _session.TryPlayAsync();
            await RefreshSessionAsync();
        };
        controls.Children.Add(previous); controls.Children.Add(_playPause); controls.Children.Add(next);
        content.Children.Add(controls);
        InitializePopup(content, 340, 0);
    }
    public override string Id => "media";
    public override void Refresh() => _ = RefreshSessionAsync();
    private async System.Threading.Tasks.Task RefreshSessionAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _session = manager.GetCurrentSession();
            if (_session is null)
            {
                _nowPlaying.Text = "No active media";
                _playPause.Content = "Play";
                return;
            }
            var properties = await _session.TryGetMediaPropertiesAsync();
            _nowPlaying.Text = string.IsNullOrWhiteSpace(properties.Artist)
                ? properties.Title
                : $"{properties.Title}\n{properties.Artist}";
            _playPause.Content = _session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "Pause" : "Play";
        }
        catch { _nowPlaying.Text = "Media controls unavailable"; }
    }
}

internal sealed class WallpaperMonobarModule : MonobarModuleBase
{
    private readonly MainWindow _owner;
    private readonly MonobarPopupWindow _popup;
    public WallpaperMonobarModule(MainWindow owner) : base(owner)
    {
        _owner = owner;
        var choose = new Button { Content = "Choose image…", HorizontalAlignment = HorizontalAlignment.Stretch, Height = 34, MinHeight = 0 };
        choose.Click += async (_, _) => await ChooseWallpaperAsync();
        var content = new StackPanel { Width = 300, Spacing = 12 };
        content.Children.Add(MonobarPopupContent.Header("\uE91B", "Wallpaper", "Personalize your desktop"));
        var card = MonobarPopupContent.Card();
        card.Child = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = "Select an image file to use as your desktop background.", FontSize = 13, Opacity = 0.75, TextWrapping = TextWrapping.Wrap }, choose } };
        content.Children.Add(card);
        _popup = new MonobarPopupWindow(MonobarPopupContent.PopupSurface(content, 340, 0), 340, 0);
        Element = IconButton("\uE91B", "Wallpaper picker", (_, _) => ShowPopup());
    }
    public override string Id => "wallpaper";
    public override UIElement Element { get; }
    public override void Dispose() => _popup.Close();
    private void ShowPopup()
    {
        _popup.ShowBelow(Element);
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
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(42, 96, 205, 255)),
            Child = new FontIcon { Glyph = glyph, FontSize = 17, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 210, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
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
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(52, 26, 26, 26)),
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
    public static IMonobarModule? Create(string id, MainWindow owner) => id switch
    {
        "workspaces" => new WorkspacesMonobarModule(owner),
        "clock" => new ClockMonobarModule(owner),
        "launcher" => new LauncherMonobarModule(owner),
        "wifi" => new WifiMonobarModule(owner),
        "volume" => new VolumeMonobarModule(owner),
        "battery" => new BatteryMonobarModule(owner),
        "network-speed" => new NetworkSpeedMonobarModule(owner),
        "media" => new MediaMonobarModule(owner),
        "wallpaper" => new WallpaperMonobarModule(owner),
        "taskbar" => new TaskbarMonobarModule(owner),
        "system-monitor" => new SystemMonitorMonobarModule(owner),
        "task-view" => new TaskViewMonobarModule(owner),
        "show-desktop" => new ShowDesktopMonobarModule(owner),
        "recycle-bin" => new RecycleBinMonobarModule(owner),
        _ => null
    };
}
