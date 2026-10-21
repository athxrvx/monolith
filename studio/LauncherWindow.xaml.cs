using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
    private IntPtr _previousForeground;

    public Action<string>? RequestOpenSettings { get; set; }

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
        const int width = 660;
        const int height = 410;
        _appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
        _appWindow.Move(new Windows.Graphics.PointInt32
        {
            X = workArea.X + (workArea.Width - width) / 2,
            Y = workArea.Y + (workArea.Height - height) / 2
        });
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
        SystemBackdrop = material switch
        {
            "Mica" => new MicaBackdrop { Kind = MicaKind.Base },
            "Mica Alt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            "Pure OLED Dark" => null,
            _ => new DesktopAcrylicBackdrop()
        };

        byte alpha = oled ? (byte)255 : (byte)(Math.Clamp(App.MainViewModel.Config.LauncherOpacity, 20, 100) * 255 / 100);
        LauncherSurface.Background = new SolidColorBrush(
            Windows.UI.Color.FromArgb(alpha, oled ? (byte)0 : (byte)28, oled ? (byte)0 : (byte)28, oled ? (byte)0 : (byte)28));
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

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostWindowMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hwnd);

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
        uint roundCorners = 2; // DWMWCP_ROUND: use the native Windows 11 corner treatment.
        DwmSetWindowAttribute(hwnd, 33, ref roundCorners, sizeof(uint)); // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    public void TogglePalette()
    {
        if (_isVisible)
        {
            HidePalette();
            return;
        }
        _isVisible = true;
        IntPtr foreground = GetForegroundWindow();
        if (foreground != _hwnd)
            _previousForeground = foreground;
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
            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string title, string target, string category)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(target)) return;
            if (!titles.Add(title)) return;
            indexed.Add(new LauncherResult(title, target, category));
        }

        void AddAction(string title, string subtitle, string action, string glyph)
        {
            if (titles.Add(title)) indexed.Add(new LauncherResult(title, subtitle, "Monolith", action, glyph));
        }

        AddAction("Open Monolith Settings", "Configure the window manager", "settings:General", "\uE713");
        AddAction("Open Appearance Settings", "Change launcher material and animations", "settings:Appearance", "\uE790");
        AddAction("Previous Workspace", "Switch to the previous Monolith workspace", "workspace:previous", "\uE72B");
        AddAction("Next Workspace", "Switch to the next Monolith workspace", "workspace:next", "\uE72A");
        AddAction("Toggle Floating", "Change how the focused window is managed", "window:float", "\uE8A7");
        AddAction("Toggle Monocle", "Show the focused window by itself", "window:monocle", "\uE8A9");
        AddAction("Tile Vertically", "Add or remove the focused window from a column", "window:vertical", "\uE8A9");
        AddAction("Close Focused Window", "Close the window that was active before opening the launcher", "window:close", "\uE8BB");
        AddAction("Minimize Focused Window", "Minimize the previously active window", "window:minimize", "\uE921");
        AddAction("Maximize or Restore Focused Window", "Toggle the previous window between maximized and normal", "window:maximize", "\uE922");
        AddAction("Restart Monolith Engine", "Restart window management", "engine:restart", "\uE777");
        for (int workspace = 1; workspace <= 9; workspace++)
        {
            AddAction($"Switch to Workspace {workspace}", $"Show Monolith workspace {workspace}", $"workspace:{workspace}", "\uE8D4");
            AddAction($"Move focused window to Workspace {workspace}", $"Send the current window to workspace {workspace}", $"move:{workspace}", "\uE8D4");
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
            EmptyState.Visibility = Visibility.Visible;
            EmptyStateText.Text = "Loading apps…";
            EmptyStateIcon.Glyph = "\uE895";
            ResultCountText.Text = string.Empty;
            return;
        }
        IEnumerable<LauncherResult> results = _allResults;
        bool commandsOnly = query.TrimStart().StartsWith('>');
        string searchQuery = commandsOnly ? query.TrimStart()[1..].Trim() : query;
        if (commandsOnly)
            results = results.Where(item => item.Category == "Monolith");
        else if (string.IsNullOrWhiteSpace(searchQuery))
            results = results.Where(item => item.Category != "Monolith");
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var terms = searchQuery.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            results = results.Select(item => (Item: item, Score: MatchScore(terms, $"{item.Title} {item.Subtitle} {item.Category}")))
                .Where(match => match.Score >= 0)
                .OrderByDescending(match => match.Score)
                .Select(match => match.Item);
        }
        var visibleResults = results.Take(20).ToList();
        ResultsList.ItemsSource = visibleResults;
        EmptyState.Visibility = ResultsList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Text = "No matches";
        EmptyStateIcon.Glyph = commandsOnly ? "\uE946" : "\uE721";
        ResultCountText.Text = commandsOnly
            ? $"{visibleResults.Count} Monolith actions"
            : string.IsNullOrWhiteSpace(searchQuery)
                ? $"{visibleResults.Count} apps and tools"
                : $"{visibleResults.Count} matches";
        if (ResultsList.Items.Count > 0) ResultsList.SelectedIndex = 0;
        else
        {
            PreviewIcon.Source = null;
            PreviewGlyph.Glyph = commandsOnly ? "\uE946" : "\uE721";
            PreviewTitle.Text = commandsOnly ? "No actions found" : "No matches";
            PreviewSubtitle.Text = commandsOnly
                ? "Try another command or search term."
                : "Try a different name or search term.";
            PreviewCategoryPill.Visibility = Visibility.Collapsed;
        }
    }

    private void ActionsButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = ">";
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not LauncherResult result) return;

        PreviewIcon.Source = null;
        PreviewGlyph.Glyph = result.Glyph;
        PreviewTitle.Text = result.Title;
        PreviewSubtitle.Text = result.Subtitle;
        PreviewCategory.Text = result.Category;
        PreviewCategoryPill.Visibility = Visibility.Visible;

        if (result.Category == "Monolith") return;
        var iconPath = await result.LoadIconAsync();
        if (iconPath is null || !ReferenceEquals(ResultsList.SelectedItem, result)) return;

        var bitmap = new BitmapImage { DecodePixelWidth = 96, DecodePixelHeight = 96 };
        bitmap.UriSource = new Uri(iconPath);
        PreviewIcon.Source = bitmap;
    }

    private async void ResultIcon_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image || image.DataContext is not LauncherResult result) return;
        if (result.Category == "Monolith") return;
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

    private async void Launch(LauncherResult result)
    {
        if (result.Category == "Monolith")
        {
            await ExecuteMonolithActionAsync(result);
            return;
        }

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

    private async Task ExecuteMonolithActionAsync(LauncherResult result)
    {
        string action = result.Target;
        HidePalette();
        try
        {
            if (action.StartsWith("settings:", StringComparison.Ordinal))
            {
                RequestOpenSettings?.Invoke(action[9..]);
                return;
            }

            if (action.StartsWith("workspace:", StringComparison.Ordinal))
            {
                string value = action[10..];
                if (value == "next")
                    await App.MainViewModel.NavigateWorkspaceAsync(1);
                else if (value == "previous")
                    await App.MainViewModel.NavigateWorkspaceAsync(-1);
                else if (int.TryParse(value, out int workspace))
                    await App.MainViewModel.SwitchWorkspaceAsync(workspace);
                return;
            }

            if (action.StartsWith("move:", StringComparison.Ordinal) && int.TryParse(action[5..], out int targetWorkspace))
            {
                await App.MainViewModel.MoveActiveWindowToWorkspaceAsync(targetWorkspace, _previousForeground.ToInt64());
                return;
            }

            if (action == "window:float") await App.MainViewModel.ToggleFloatingAsync(_previousForeground.ToInt64());
            else if (action == "window:monocle") await App.MainViewModel.ToggleMonocleAsync(_previousForeground.ToInt64());
            else if (action == "window:vertical") await App.MainViewModel.ToggleVerticalTileAsync(_previousForeground.ToInt64());
            else if (action == "window:close" && _previousForeground != IntPtr.Zero)
                PostWindowMessage(_previousForeground, 0x0010, UIntPtr.Zero, IntPtr.Zero);
            else if (action == "window:minimize" && _previousForeground != IntPtr.Zero)
                ShowWindow(_previousForeground, 6);
            else if (action == "window:maximize" && _previousForeground != IntPtr.Zero)
                ShowWindow(_previousForeground, IsZoomed(_previousForeground) ? 9 : 3);
            else if (action == "engine:restart") await App.MainViewModel.RestartEngineAsync();
        }
        catch (Exception ex)
        {
            App.LogStartup($"Launcher action failed: {result.Title}", ex);
        }
    }
}

public sealed class LauncherResult
{
    public LauncherResult(string title, string target, string category, string? glyph = null)
        : this(title, target, category, target, glyph)
    {
    }

    public LauncherResult(string title, string subtitle, string category, string target, string? glyph)
    {
        Title = title;
        Subtitle = subtitle;
        Target = target;
        Category = category;
        _glyph = glyph;
    }

    public string Title { get; }
    public string Target { get; }
    public string Category { get; }
    public string Subtitle { get; }
    private readonly string? _glyph;
    private Task<string?>? _iconLoadTask;
    public async Task<string?> LoadIconAsync() =>
        await (_iconLoadTask ??= Task.Run(() => ShellIconCache.GetIconPath(Target)));

    public string Glyph => _glyph ?? (Category switch
    {
        "Windows tool" => "\uE713",
        "Web app" => "\uE774",
        _ => "\uECAA"
    });
}
