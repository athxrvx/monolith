using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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

public sealed partial class LauncherWindow : Window
{
    private readonly List<LauncherResult> _allResults = new();
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;
    private bool _isVisible;
    private bool _launchInProgress;
    private bool _resultsLoaded;
    private bool _suppressSearchChange;
    private Task? _resultsLoadTask;

    public LauncherWindow()
    {
        InitializeComponent();
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
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        const int width = 600;
        const int height = 420;
        _appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
        _appWindow.Move(new Windows.Graphics.PointInt32
        {
            X = workArea.X + (workArea.Width - width) / 2,
            Y = workArea.Y + (workArea.Height - height) / 2
        });
        SystemBackdrop = new DesktopAcrylicBackdrop();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && _isVisible)
                HidePalette();
            else if (_isVisible)
                FocusSearchBox();
        };
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    private static void RemoveNativeFrame(IntPtr hwnd)
    {
        const int GwlStyle = -16;
        const long WsBorder = 0x00800000L;
        const long WsDialogFrame = 0x00400000L;
        const long WsThickFrame = 0x00040000L;
        const long WsCaption = 0x00C00000L;
        const uint SwpNoMove = 0x0002;
        const uint SwpNoSize = 0x0001;
        const uint SwpNoZOrder = 0x0004;
        const uint SwpFrameChanged = 0x0020;

        long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        long framelessStyle = style & ~(WsBorder | WsDialogFrame | WsThickFrame | WsCaption);
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(framelessStyle));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);

        uint noBorder = 0xFFFFFFFE;
        DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(uint)); // DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE
        uint noCorners = 1; // DWMWCP_DONOTROUND
        DwmSetWindowAttribute(hwnd, 33, ref noCorners, sizeof(uint)); // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    public void TogglePalette()
    {
        if (_isVisible)
        {
            HidePalette();
            return;
        }
        _isVisible = true;
        _launchInProgress = false;
        _suppressSearchChange = true;
        SearchBox.Text = string.Empty;
        _suppressSearchChange = false;
        FilterResults(string.Empty);
        _appWindow.Show();
        Activate();
        BringWindowToTop(_hwnd);
        SetForegroundWindow(_hwnd);
        SearchBox.Focus(FocusState.Programmatic);
        DispatcherQueue.GetForCurrentThread()?.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            if (!_isVisible) return;
            BringWindowToTop(_hwnd);
            SetForegroundWindow(_hwnd);
            SearchBox.Focus(FocusState.Programmatic);
        });
        _resultsLoadTask ??= LoadResultsAsync();
    }

    private void FocusSearchBox()
    {
        if (!_isVisible) return;
        SearchBox.Focus(FocusState.Programmatic);
    }

    public void HidePalette()
    {
        _isVisible = false;
        _appWindow.Hide();
    }

    private async Task LoadResultsAsync()
    {
        var results = await Task.Run(() =>
        {
            var indexed = new List<LauncherResult>();
        void Add(string title, string target, string category)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(target)) return;
            if (indexed.Any(item => item.Title.Equals(title, StringComparison.OrdinalIgnoreCase))) return;
            indexed.Add(new LauncherResult(title, target, category));
        }

        Add("Task Manager", "taskmgr.exe", "Windows tool");
        Add("Windows Terminal", "wt.exe", "Windows tool");
        Add("PowerShell", "powershell.exe", "Windows tool");
        Add("Command Prompt", "cmd.exe", "Windows tool");
        Add("File Explorer", "explorer.exe", "Windows tool");
        Add("Windows Settings", "ms-settings:", "Windows tool");

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
        };
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(file);
                    if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) continue;
                    var title = Path.GetFileNameWithoutExtension(file);
                    if (title.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    Add(title, file, extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ? "Web app" : "Application");
                }
            }
            catch { }
        }
            return indexed;
        });
        _allResults.Clear();
        _allResults.AddRange(results);
        _resultsLoaded = true;
        FilterResults(SearchBox.Text);
    }

    private void FilterResults(string query)
    {
        if (!_resultsLoaded)
        {
            ResultsList.ItemsSource = null;
            EmptyStateText.Visibility = Visibility.Visible;
            EmptyStateText.Text = "Loading apps…";
            ResultCountText.Text = string.Empty;
            return;
        }
        IEnumerable<LauncherResult> results = _allResults;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            results = results.Select(item => (Item: item, Score: MatchScore(terms, $"{item.Title} {item.Subtitle}")))
                .Where(match => match.Score >= 0)
                .OrderByDescending(match => match.Score)
                .Select(match => match.Item);
        }
        var visibleResults = results.Take(20).ToList();
        ResultsList.ItemsSource = visibleResults;
        EmptyStateText.Visibility = ResultsList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Text = "No matches";
        ResultCountText.Text = string.IsNullOrWhiteSpace(query)
            ? $"{visibleResults.Count} apps and tools"
            : $"{visibleResults.Count} matches";
        if (ResultsList.Items.Count > 0) ResultsList.SelectedIndex = 0;
    }

    private async void ResultIcon_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image || image.DataContext is not LauncherResult result) return;
        var iconPath = await result.LoadIconAsync();
        if (iconPath is not null)
        {
            // The launcher displays 32px icons; decode them at display size so
            // shell icons (often 256px) do not occupy unnecessary bitmap memory.
            var bitmap = new BitmapImage { DecodePixelWidth = 32, DecodePixelHeight = 32 };
            bitmap.UriSource = new Uri(iconPath);
            image.Source = bitmap;
        }
    }

    private static int MatchScore(string[] terms, string text)
    {
        int score = 0;
        foreach (var term in terms)
        {
            int index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return -1;
            score += 100 - Math.Min(index, 99) + (index == 0 ? 100 : 0);
        }
        return score;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_suppressSearchChange) FilterResults(sender.Text);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => LaunchSelected();

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LauncherResult result) Launch(result);
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
        else if (e.Key is Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Up)
        {
            int count = ResultsList.Items.Count;
            if (count > 0)
            {
                int step = e.Key == Windows.System.VirtualKey.Down ? 1 : -1;
                int current = ResultsList.SelectedIndex < 0 ? 0 : ResultsList.SelectedIndex;
                ResultsList.SelectedIndex = (current + step + count) % count;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
            e.Handled = true;
        }
    }

    private void LaunchSelected()
    {
        if (!_launchInProgress && ResultsList.SelectedItem is LauncherResult result)
        {
            _launchInProgress = true;
            Launch(result);
        }
    }

    private void Launch(LauncherResult result)
    {
        HidePalette();
        try
        {
            Process.Start(new ProcessStartInfo { FileName = result.Target, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not open {result.Title}: {ex.Message}");
        }
    }
}

public sealed class LauncherResult
{
    public LauncherResult(string title, string target, string category)
    {
        Title = title;
        Target = target;
        Category = category;
    }

    public string Title { get; }
    public string Target { get; }
    public string Category { get; }
    public string Subtitle => Target;
    private Task<string?>? _iconLoadTask;
    public async Task<string?> LoadIconAsync() =>
        await (_iconLoadTask ??= Task.Run(() => ShellIconCache.GetIconPath(Target)));

    public string Glyph => Category switch
    {
        "Windows tool" => "\uE713",
        "Web app" => "\uE774",
        _ => "\uECAA"
    };
}
