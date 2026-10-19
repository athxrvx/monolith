using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Monolith.Models;
using Monolith.Services;

namespace Monolith.Views;

public partial class LauncherWindow : Window
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private readonly AppIndexer _indexer = new();
    private readonly IpcClient _ipcClient = new();
    private readonly Task<List<AppItem>> _indexTask;
    private List<AppItem> _allApps = new();
    private List<AppItem> _filteredApps = new();
    private bool _isClosingFromAnimation;
    private bool _indexReady;

    public Action? RequestOpenStudio;

    public LauncherWindow()
    {
        InitializeComponent();
        // Start indexing during app startup so the first hotkey press doesn't
        // have to wait for the Start Menu scan and icon extraction.
        _indexTask = _indexer.IndexApplicationsAsync();
        Loaded += LauncherWindow_Loaded;
        Deactivated += LauncherWindow_Deactivated;
    }

    private async void LauncherWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _ipcClient.StartAsync();

        await RefreshAppIndexAsync();
        PositionOnActiveScreen();
    }

    private async Task RefreshAppIndexAsync()
    {
        try
        {
            EmptyState.Text = "Loading apps…";
            EmptyState.Visibility = Visibility.Visible;
            _allApps = await _indexTask;
            _indexReady = true;
            UpdateFilter(SearchBox.Text);
        }
        catch
        {
            _indexReady = true;
            _allApps.Clear();
            UpdateFilter(SearchBox.Text);
        }
    }

    private void PositionOnActiveScreen()
    {
        if (!GetCursorPos(out var pt)) return;

        var hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

        if (GetMonitorInfo(hMonitor, ref mi))
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            double screenLeft = mi.rcWork.Left / dpi.DpiScaleX;
            double screenTop = mi.rcWork.Top / dpi.DpiScaleY;
            double screenWidth = (mi.rcWork.Right - mi.rcWork.Left) / dpi.DpiScaleX;
            double screenHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / dpi.DpiScaleY;

            Left = screenLeft + ((screenWidth - Width) / 2.0);
            Top = screenTop + ((screenHeight - ActualHeight) / 2.0);
        }
    }

    public void ShowPalette()
    {
        PositionOnActiveScreen();
        SearchBox.Text = string.Empty;
        Show();
        Visibility = Visibility.Visible;
        Activate();
        AnimateEntrance();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            Activate();
            Keyboard.Focus(SearchBox);
            SearchBox.Focus();
        }));
    }

    public void HidePalette()
    {
        AnimateExit();
    }

    public void TogglePalette()
    {
        if (Visibility == Visibility.Visible && Opacity > 0.05)
        {
            HidePalette();
        }
        else
        {
            ShowPalette();
        }
    }

    private void AnimateEntrance()
    {
        _isClosingFromAnimation = false;
        Opacity = 0.0;
        WindowScaleTransform.ScaleX = 0.96;
        WindowScaleTransform.ScaleY = 0.96;

        var duration = TimeSpan.FromMilliseconds(120);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var opacityAnim = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
        var scaleXAnim = new DoubleAnimation(0.96, 1.0, duration) { EasingFunction = ease };
        var scaleYAnim = new DoubleAnimation(0.96, 1.0, duration) { EasingFunction = ease };

        BeginAnimation(OpacityProperty, opacityAnim);
        WindowScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnim);
        WindowScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnim);

        SearchBox.Focus();
    }

    private void AnimateExit(Action? onComplete = null)
    {
        if (_isClosingFromAnimation) return;
        _isClosingFromAnimation = true;

        var duration = TimeSpan.FromMilliseconds(90);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var opacityAnim = new DoubleAnimation(Opacity, 0.0, duration) { EasingFunction = ease };
        var scaleXAnim = new DoubleAnimation(WindowScaleTransform.ScaleX, 0.98, duration) { EasingFunction = ease };
        var scaleYAnim = new DoubleAnimation(WindowScaleTransform.ScaleY, 0.98, duration) { EasingFunction = ease };

        opacityAnim.Completed += (_, _) =>
        {
            Visibility = Visibility.Hidden;
            _isClosingFromAnimation = false;
            onComplete?.Invoke();
        };

        BeginAnimation(OpacityProperty, opacityAnim);
        WindowScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnim);
        WindowScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnim);
    }

    private void LauncherWindow_Deactivated(object? sender, EventArgs e)
    {
        if (Visibility == Visibility.Visible && !_isClosingFromAnimation)
        {
            AnimateExit();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchBox.Text;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;

        UpdateFilter(query);
    }

    private void UpdateFilter(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _filteredApps = _allApps.Take(25).ToList();
        }
        else
        {
            var querySpan = query.AsSpan().Trim();
            var scored = new List<(AppItem Item, int Score)>();

            foreach (var app in _allApps)
            {
                int score = FuzzySearch.Score(querySpan, app.Title.AsSpan());
                if (score < 0 && !string.IsNullOrEmpty(app.Subtitle))
                {
                    int subScore = FuzzySearch.Score(querySpan, app.Subtitle.AsSpan());
                    if (subScore >= 0)
                    {
                        score = subScore - 15;
                    }
                }

                if (score >= 0)
                {
                    scored.Add((app, score));
                }
            }

            _filteredApps = scored
                .OrderByDescending(x => x.Score)
                .Take(25)
                .Select(x => x.Item)
                .ToList();
        }

        ResultsList.ItemsSource = _filteredApps;
        if (_filteredApps.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        }
        EmptyState.Text = _indexReady
            ? (string.IsNullOrWhiteSpace(query) ? "No apps available" : "No matching apps")
            : "Loading apps…";
        EmptyState.Visibility = _filteredApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultCountText.Text = !_indexReady
            ? string.Empty
            : string.IsNullOrWhiteSpace(query)
                ? $"{_allApps.Count} apps"
                : $"{_filteredApps.Count} matches";
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            AnimateExit();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            if (ResultsList.Items.Count > 0)
            {
                int nextIndex = (ResultsList.SelectedIndex + 1) % ResultsList.Items.Count;
                ResultsList.SelectedIndex = nextIndex;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            if (ResultsList.Items.Count > 0)
            {
                int prevIndex = ResultsList.SelectedIndex - 1;
                if (prevIndex < 0) prevIndex = ResultsList.Items.Count - 1;
                ResultsList.SelectedIndex = prevIndex;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Home || e.Key == Key.End)
        {
            if (ResultsList.Items.Count > 0)
            {
                ResultsList.SelectedIndex = e.Key == Key.Home ? 0 : ResultsList.Items.Count - 1;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (ResultsList.SelectedItem is AppItem selected)
            {
                ExecuteItem(selected);
            }
            e.Handled = true;
            return;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is AppItem selected)
        {
            ExecuteItem(selected);
        }
    }

    private async void ExecuteItem(AppItem item)
    {
        AnimateExit();

        if (item.IsEngineCommand)
        {
            if (item.EngineCommand == "OPEN_STUDIO")
            {
                RequestOpenStudio?.Invoke();
            }
            else
            {
                await _ipcClient.SendCommandAsync(item.EngineCommand, item.EngineArgs);
            }
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = item.TargetPath,
                Arguments = item.Arguments,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _ = _ipcClient.DisposeAsync();
        base.OnClosed(e);
    }
}

