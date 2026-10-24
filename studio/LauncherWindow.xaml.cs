using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Monolith.Studio.Services;
using WinRT.Interop;

namespace Monolith.Studio;

public sealed partial class LauncherWindow : Window, ILauncherWindowController
{
    private readonly IApplicationCatalog _catalog = new ApplicationCatalog();
    private readonly ILauncherActivationService _activationService = new LauncherActivationService();
    private readonly ILauncherSearchService _searchService;
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer? _resizeTimer;
    private readonly HashSet<string> _expandedGroups = new(StringComparer.Ordinal);
    private List<LauncherResult> _allApplications = new();
    private List<LauncherResult> _applicationStrip = new();
    private List<LauncherResultGroup> _displayGroups = new();
    private List<LauncherResult> _visibleResults = new();
    private CancellationTokenSource _catalogCancellation = new();
    private Task? _catalogLoadTask;
    private bool _isVisible;
    private bool _launchInProgress;
    private bool _resultsLoaded;
    private bool _showAllApplications;
    private bool _lightTheme;
    private bool _searchBoxFocused;
    private int _selectedResultIndex = -1;
    private IntPtr _previousForeground;

    public Action<string>? RequestOpenSettings { get; set; }

    public LauncherWindow()
    {
        InitializeComponent();
        _resizeTimer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
        if (_resizeTimer is not null)
        {
            _resizeTimer.Interval = TimeSpan.FromMilliseconds(90);
            _resizeTimer.IsRepeating = false;
            _resizeTimer.Tick += (_, _) => ResizeToContent();
        }
        AppsGrid.Loaded += (_, _) => UpdateApplicationsGridLayout();
        AppsGrid.SizeChanged += (_, _) => UpdateApplicationsGridLayout();
        _searchService = new LauncherSearchService(_catalog);
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        RemoveNativeFrame(_hwnd);
        CenterOnMonitor(IntPtr.Zero);
        ApplyAppearance();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && _isVisible)
                HidePalette();
            else if (_isVisible)
                FocusSearchBox();
        };
    }

    public void ApplyAppearance()
    {
        string material = App.MainViewModel.Config.BackdropMaterial;
        bool oled = material.Equals("Pure OLED Dark", StringComparison.OrdinalIgnoreCase);
        bool lightTheme = !oled && Convert.ToInt32(Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 0)) == 1;
        _lightTheme = lightTheme;
        LauncherRoot.RequestedTheme = lightTheme ? ElementTheme.Light : ElementTheme.Dark;
        try
        {
            SystemBackdrop = material switch
            {
                "Mica" => new MicaBackdrop { Kind = MicaKind.Base },
                "Mica Alt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                "Pure OLED Dark" => null,
                _ => new DesktopAcrylicBackdrop()
            };
        }
        catch
        {
            SystemBackdrop = null;
        }

        bool backdropUnavailable = !oled && SystemBackdrop is null;
        byte alpha = oled || backdropUnavailable
            ? (byte)255
            : (byte)(Math.Clamp(App.MainViewModel.Config.LauncherOpacity, 20, 100) * 255 / 100);
        byte surface = lightTheme ? (byte)248 : (byte)28;
        LauncherSurface.Background = new SolidColorBrush(
            Windows.UI.Color.FromArgb(alpha, oled ? (byte)0 : surface, oled ? (byte)0 : surface, oled ? (byte)0 : surface));
        LauncherSurface.BorderBrush = new SolidColorBrush(lightTheme
            ? Windows.UI.Color.FromArgb(40, 0, 0, 0)
            : Windows.UI.Color.FromArgb(48, 255, 255, 255));
        SearchHeader.Background = new SolidColorBrush(lightTheme
            ? Windows.UI.Color.FromArgb(20, 0, 0, 0)
            : Windows.UI.Color.FromArgb(38, 255, 255, 255));
        UpdateSearchHeaderBorder();
        if (LauncherSurface.Resources["SurfaceHigherBrush"] is SolidColorBrush surfaceBrush)
            surfaceBrush.Color = lightTheme ? Windows.UI.Color.FromArgb(255, 232, 232, 232) : Windows.UI.Color.FromArgb(255, 56, 56, 56);
        if (LauncherSurface.Resources["GroupSurfaceBrush"] is SolidColorBrush groupSurfaceBrush)
            groupSurfaceBrush.Color = lightTheme ? Windows.UI.Color.FromArgb(24, 0, 0, 0) : Windows.UI.Color.FromArgb(18, 255, 255, 255);
        if (LauncherSurface.Resources["GroupSurfaceBorderBrush"] is SolidColorBrush groupBorderBrush)
            groupBorderBrush.Color = lightTheme ? Windows.UI.Color.FromArgb(36, 0, 0, 0) : Windows.UI.Color.FromArgb(24, 255, 255, 255);
        if (LauncherSurface.Resources["LauncherSecondaryTextBrush"] is SolidColorBrush secondaryBrush)
            secondaryBrush.Color = lightTheme ? Windows.UI.Color.FromArgb(255, 48, 48, 48) : Windows.UI.Color.FromArgb(255, 212, 212, 212);
        if (LauncherSurface.Resources["LauncherTertiaryTextBrush"] is SolidColorBrush tertiaryBrush)
            tertiaryBrush.Color = lightTheme ? Windows.UI.Color.FromArgb(255, 92, 92, 92) : Windows.UI.Color.FromArgb(255, 168, 168, 168);
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref uint value, uint valueSize);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    private static void RemoveNativeFrame(IntPtr hwnd)
    {
        const int GwlStyle = -16;
        const int GwlExStyle = -20;
        const long WsBorder = 0x00800000L;
        const long WsDialogFrame = 0x00400000L;
        const long WsThickFrame = 0x00040000L;
        const long WsCaption = 0x00C00000L;
        const long WsExAppWindow = 0x00040000L;
        const long WsExToolWindow = 0x00000080L;
        const uint SwpNoMove = 0x0002;
        const uint SwpNoSize = 0x0001;
        const uint SwpNoZOrder = 0x0004;
        const uint SwpFrameChanged = 0x0020;

        long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style & ~(WsBorder | WsDialogFrame | WsThickFrame | WsCaption)));
        long extendedStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr((extendedStyle | WsExToolWindow) & ~WsExAppWindow));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);

        uint noBorder = 0xFFFFFFFE;
        DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(uint));
        uint roundCorners = 2;
        DwmSetWindowAttribute(hwnd, 33, ref roundCorners, sizeof(uint));
    }

    private void CenterOnMonitor(IntPtr foreground)
    {
        var area = DisplayArea.GetFromWindowId(
            foreground != IntPtr.Zero && IsWindow(foreground)
                ? Microsoft.UI.Win32Interop.GetWindowIdFromWindow(foreground)
                : Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd),
            DisplayAreaFallback.Primary);
        var workArea = area.WorkArea;
        var config = App.MainViewModel.Config;
        int widthPercent = Math.Clamp(config.LauncherWidthPercent, 40, 90);
        int heightPercent = Math.Clamp(config.LauncherHeightPercent, 65, 95);
        int width = Math.Min(workArea.Width - 32, Math.Max(480, (int)(workArea.Width * widthPercent / 100.0)));
        int height = Math.Min(workArea.Height - 32, Math.Max(440, (int)(workArea.Height * heightPercent / 100.0)));
        _appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
        _appWindow.Move(new Windows.Graphics.PointInt32
        {
            X = workArea.X + (workArea.Width - width) / 2,
            Y = workArea.Y + (workArea.Height - height) / 2
        });
    }

    public void Toggle() => TogglePalette();

    public void TogglePalette()
    {
        if (_isVisible) { HidePalette(); return; }
        Show();
    }

    public void Show()
    {
        if (_isVisible) { FocusSearchBox(); return; }
        _isVisible = true;
        _launchInProgress = false;
        IntPtr foreground = GetForegroundWindow();
        if (foreground != _hwnd) _previousForeground = foreground;
        CenterOnMonitor(_previousForeground);
        ApplyAppearance();
        _appWindow.Show();
        Activate();
        BringWindowToTop(_hwnd);
        SetForegroundWindow(_hwnd);
        FocusSearchBox();
        DispatcherQueue.GetForCurrentThread()?.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            if (!_isVisible) return;
            BringWindowToTop(_hwnd);
            SetForegroundWindow(_hwnd);
            FocusSearchBox();
        });
        _catalogLoadTask ??= LoadCatalogAsync(_catalogCancellation.Token);
        if (_resultsLoaded) ApplySearch(SearchBox.Text);
    }

    public void Hide() => HidePalette();

    public void HidePalette()
    {
        if (!_isVisible) return;
        _isVisible = false;
        _appWindow.Hide();
        IntPtr restoreTarget = _previousForeground;
        if (restoreTarget != IntPtr.Zero)
        {
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(DispatcherQueuePriority.High, () =>
            {
                if (IsWindow(restoreTarget)) SetForegroundWindow(restoreTarget);
            });
        }
    }

    private async Task LoadCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _catalog.InitializeAsync(cancellationToken);
            _resultsLoaded = true;
            ApplySearch(SearchBox.Text);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.LogStartup("Launcher app catalog refresh failed", ex);
            _resultsLoaded = true;
            ApplySearch(SearchBox.Text);
        }
    }

    private void ApplySearch(string query)
    {
        if (!_resultsLoaded)
        {
            ApplicationsSection.Visibility = Visibility.Collapsed;
            GroupsPanel.ItemsSource = null;
            EmptyState.Visibility = Visibility.Visible;
            EmptyStateText.Text = "Loading applications…";
            EmptyStateIcon.Glyph = "\uE895";
            ResultCountText.Text = string.Empty;
            _visibleResults.Clear();
            _selectedResultIndex = -1;
            return;
        }

        var groups = _searchService.Search(query).ToList();
        var appGroup = groups.FirstOrDefault(group => group.Id == "applications");
        _allApplications = appGroup?.AllResults.ToList() ?? new List<LauncherResult>();
        int columns = GetApplicationColumnCount();
        const int previewRows = 2;
        int previewCount = columns * previewRows;
        _applicationStrip = (_showAllApplications ? _allApplications : _allApplications.Take(previewCount).ToList());
        if (appGroup is not null) groups.Remove(appGroup);
        foreach (var group in groups) group.SetExpanded(_expandedGroups.Contains(group.Id));

        AppsGrid.ItemsSource = _applicationStrip;
        AppsExpandButton.Visibility = _allApplications.Count > previewCount ? Visibility.Visible : Visibility.Collapsed;
        AppsExpandLabel.Text = _showAllApplications ? "Show less  " : "Show all  ";
        UpdateApplicationsGridLayout();
        ApplicationsSection.Visibility = _applicationStrip.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AppsCountText.Text = appGroup is null ? string.Empty : $"{appGroup.AllResults.Count} apps";
        _displayGroups = groups;
        GroupsPanel.ItemsSource = _displayGroups;
        UpdateVisibleResults();
        EmptyState.Visibility = _visibleResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Text = string.IsNullOrWhiteSpace(query) ? "No applications found" : "No matches found";
        EmptyStateIcon.Glyph = "\uE721";

        int totalCount = groups.Sum(group => group.AllResults.Count) + _allApplications.Count;
        ResultCountText.Text = string.IsNullOrWhiteSpace(query) ? $"{totalCount} destinations" : $"{totalCount} matches";
        _selectedResultIndex = _visibleResults.Count > 0 ? 0 : -1;
        SelectResultAt(_selectedResultIndex);
        ScheduleContentResize();
    }

    private void ScheduleContentResize()
    {
        if (_resizeTimer is null)
        {
            ResizeToContent();
            return;
        }

        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void ResizeToContent()
    {
        if (!_isVisible || LauncherContent.ActualHeight <= 0) return;

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;
        var config = App.MainViewModel.Config;
        int maxHeight = Math.Min(workArea.Height - 32,
            (int)(workArea.Height * Math.Clamp(config.LauncherHeightPercent, 65, 95) / 100.0));
        double contentHeight = Math.Max(LauncherContent.ActualHeight,
            EmptyState.Visibility == Visibility.Visible ? EmptyState.ActualHeight + 24 : 0);
        double desiredDip = LauncherSurface.Padding.Top + LauncherSurface.Padding.Bottom
            + LauncherSurface.BorderThickness.Top + LauncherSurface.BorderThickness.Bottom
            + SearchHeader.ActualHeight + (LauncherRoot.RowSpacing * 2)
            + contentHeight + LauncherFooter.ActualHeight;
        double scale = LauncherRoot.XamlRoot?.RasterizationScale ?? 1.0;
        int desiredHeight = (int)Math.Ceiling(desiredDip * scale);
        int minHeight = Math.Min(360, maxHeight);
        int height = Math.Clamp(desiredHeight, minHeight, maxHeight);
        int width = _appWindow.Size.Width;
        if (_appWindow.Size.Height == height) return;

        _appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
        _appWindow.Move(new Windows.Graphics.PointInt32
        {
            X = workArea.X + (workArea.Width - width) / 2,
            Y = workArea.Y + (workArea.Height - height) / 2
        });
    }

    private int GetApplicationColumnCount()
    {
        return 6;
    }

    private void UpdateApplicationsGridLayout()
    {
        int columns = GetApplicationColumnCount();
        int rows = Math.Max(1, (int)Math.Ceiling(_applicationStrip.Count / (double)columns));
        const int rowHeight = 104;
        // Set the height even before WinUI creates the items panel. Otherwise the
        // XAML's one-row placeholder height clips the second row on first display.
        AppsGrid.Height = Math.Max(120, rows * rowHeight + 16);
        if (AppsGrid.ItemsPanelRoot is not ItemsWrapGrid wrapGrid || AppsGrid.ActualWidth <= 0) return;
        wrapGrid.ItemWidth = AppsGrid.ActualWidth / columns;
        wrapGrid.ItemHeight = rowHeight;
    }

    private void UpdateVisibleResults()
    {
        _visibleResults = _applicationStrip.Concat(_displayGroups.SelectMany(group => group.VisibleResults))
            .Distinct().ToList();
    }

    private void SelectResultAt(int index)
    {
        foreach (var item in _visibleResults) item.IsSelected = false;
        if (index < 0 || index >= _visibleResults.Count)
        {
            return;
        }

        _selectedResultIndex = index;
        var selected = _visibleResults[index];
        selected.IsSelected = true;
        DispatcherQueue.TryEnqueue(() => ScrollSelectionIntoView(selected));
    }

    private void ScrollSelectionIntoView(LauncherResult selected)
    {
        if (_applicationStrip.Contains(selected))
        {
            AppsGrid.ScrollIntoView(selected);
            if (AppsGrid.ContainerFromItem(selected) is UIElement appContainer)
                appContainer.StartBringIntoView();
            return;
        }

        foreach (var group in _displayGroups)
        {
            if (!group.VisibleResults.Contains(selected)) continue;
            if (GroupsPanel.ContainerFromItem(group) is not DependencyObject groupContainer) return;

            var resultsList = FindDescendant<ItemsControl>(groupContainer,
                control => ReferenceEquals(control.ItemsSource, group.VisibleResults));
            if (resultsList?.ContainerFromItem(selected) is UIElement resultContainer)
                resultContainer.StartBringIntoView();
            return;
        }
    }

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool> predicate)
        where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && predicate(match)) return match;
            if (FindDescendant(child, predicate) is T descendant) return descendant;
        }
        return null;
    }

    private void FocusSearchBox()
    {
        if (_isVisible) SearchBox.Focus(FocusState.Programmatic);
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _searchBoxFocused = true;
        UpdateSearchHeaderBorder();
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _searchBoxFocused = false;
        UpdateSearchHeaderBorder();
    }

    private void UpdateSearchHeaderBorder()
    {
        var accent = AccentColorService.GetAccentColor();
        var color = _searchBoxFocused
            ? Windows.UI.Color.FromArgb(110, accent.R, accent.G, accent.B)
            : _lightTheme
                ? Windows.UI.Color.FromArgb(28, 0, 0, 0)
                : Windows.UI.Color.FromArgb(36, 255, 255, 255);
        SearchHeader.BorderBrush = new SolidColorBrush(color);
    }

    private async void ResultIcon_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image || image.DataContext is not LauncherResult result
            || result.Category == "Windows settings") return;
        var iconPath = await result.LoadIconAsync();
        if (iconPath is null || !ReferenceEquals(image.DataContext, result)) return;
        var bitmap = new BitmapImage { DecodePixelWidth = 64, DecodePixelHeight = 64 };
        bitmap.UriSource = new Uri(iconPath);
        image.Source = bitmap;
        if (image.Parent is Grid iconGrid)
        {
            foreach (var child in iconGrid.Children)
                if (child is FontIcon fallbackIcon) fallbackIcon.Visibility = Visibility.Collapsed;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _showAllApplications = false;
        if (_resultsLoaded) ApplySearch(SearchBox.Text);
    }

    private void AppsExpandButton_Click(object sender, RoutedEventArgs e)
    {
        _showAllApplications = !_showAllApplications;
        if (_resultsLoaded) ApplySearch(SearchBox.Text);
    }

    private void CloseLauncher_Click(object sender, RoutedEventArgs e) => HidePalette();

    private void AppsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LauncherResult result) _ = LaunchAsync(result);
    }

    private void ResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LauncherResult result }) _ = LaunchAsync(result);
    }

    private void GroupHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LauncherResultGroup group }) return;
        group.Toggle();
        if (group.IsExpanded) _expandedGroups.Add(group.Id);
        else _expandedGroups.Remove(group.Id);
        int oldIndex = _selectedResultIndex;
        UpdateVisibleResults();
        SelectResultAt(Math.Clamp(oldIndex, 0, Math.Max(0, _visibleResults.Count - 1)));
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HidePalette();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            LaunchSelected();
            e.Handled = true;
        }
        else if (e.Key is Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Up
                 or Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right)
        {
            if (_visibleResults.Count > 0)
            {
                int current = _selectedResultIndex < 0 ? 0 : _selectedResultIndex;
                bool inApplicationGrid = current < _applicationStrip.Count;
                int target = current;

                if (inApplicationGrid)
                {
                    int columns = GetApplicationColumnCount();
                    switch (e.Key)
                    {
                        case Windows.System.VirtualKey.Left when current % columns > 0:
                            target = current - 1;
                            break;
                        case Windows.System.VirtualKey.Right when current % columns < columns - 1
                            && current + 1 < _applicationStrip.Count:
                            target = current + 1;
                            break;
                        case Windows.System.VirtualKey.Up when current >= columns:
                            target = current - columns;
                            break;
                        case Windows.System.VirtualKey.Down when current + columns < _applicationStrip.Count:
                            target = current + columns;
                            break;
                        case Windows.System.VirtualKey.Down when _applicationStrip.Count < _visibleResults.Count:
                            target = _applicationStrip.Count;
                            break;
                    }
                }
                else if (e.Key is Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Up)
                {
                    int step = e.Key == Windows.System.VirtualKey.Down ? 1 : -1;
                    target = Math.Clamp(current + step, 0, _visibleResults.Count - 1);
                }

                SelectResultAt(target);
            }
            e.Handled = true;
        }
    }

    private void LaunchSelected()
    {
        if (!_launchInProgress && _selectedResultIndex >= 0 && _selectedResultIndex < _visibleResults.Count)
            _ = LaunchAsync(_visibleResults[_selectedResultIndex]);
    }

    private async Task LaunchAsync(LauncherResult result)
    {
        if (_launchInProgress) return;
        _launchInProgress = true;
        HidePalette();
        try
        {
            if (result.ActivationTarget.Equals("monolith:settings", StringComparison.OrdinalIgnoreCase))
                RequestOpenSettings?.Invoke("General");
            else
                await _activationService.LaunchAsync(result, _catalogCancellation.Token);
            LauncherUsageStore.RecordLaunch(result);
        }
        catch (Exception ex)
        {
            _launchInProgress = false;
            App.LogStartup($"Could not launch {result.Title}", ex);
        }
    }
}
