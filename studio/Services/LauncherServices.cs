using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Monolith.Studio.Services;

namespace Monolith.Studio;

public interface ILauncherSearchService
{
    IReadOnlyList<LauncherResultGroup> Search(string query);
}

public interface IApplicationCatalog
{
    IReadOnlyList<LauncherResult> Applications { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
    Task RefreshAsync(CancellationToken cancellationToken);
}

public interface ILauncherActivationService
{
    Task LaunchAsync(LauncherResult result, CancellationToken cancellationToken);
}

public interface ILauncherWindowController
{
    void Toggle();
    void Show();
    void Hide();
}

public sealed class ApplicationCatalog : IApplicationCatalog
{
    private IReadOnlyList<LauncherResult> _applications = Array.Empty<LauncherResult>();
    public IReadOnlyList<LauncherResult> Applications => _applications;

    public async Task InitializeAsync(CancellationToken cancellationToken) => await RefreshAsync(cancellationToken);

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var refreshed = await Task.Run(() => DiscoverApplications(cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (refreshed.Count > 0) _applications = refreshed;
    }

    private static List<LauncherResult> DiscoverApplications(CancellationToken cancellationToken)
    {
        var indexed = new List<LauncherResult>();
        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(LauncherResult result)
        {
            if (string.IsNullOrWhiteSpace(result.Title) || string.IsNullOrWhiteSpace(result.ActivationTarget)) return;
            if (titles.Add(result.Title)) indexed.Add(result);
        }

        Add(new LauncherResult("Task Manager", "View running apps and system performance", "System destinations", "taskmgr.exe", "\uE9D9", "processes performance"));
        Add(new LauncherResult("File Explorer", "Browse folders and drives", "System destinations", "explorer.exe", "\uED25", "folders drives"));
        AddFolder("Desktop", Environment.SpecialFolder.DesktopDirectory, "personal files home");
        AddFolder("Documents", Environment.SpecialFolder.MyDocuments, "files writing");
        AddFolder("Pictures", Environment.SpecialFolder.MyPictures, "photos images");
        AddFolder("Music", Environment.SpecialFolder.MyMusic, "audio songs");
        AddFolder("Videos", Environment.SpecialFolder.MyVideos, "movies clips");
        Add(new LauncherResult("Recycle Bin", "Review and restore deleted files", "System destinations",
            "explorer|shell:RecycleBinFolder", "\uE74D", "deleted trash recycle"));

        void AddFolder(string title, Environment.SpecialFolder folder, string aliases)
        {
            string path = Environment.GetFolderPath(folder);
            if (Directory.Exists(path))
                Add(new LauncherResult(title, path, "System destinations", $"explorer|{path}", "\uED25", aliases));
        }

        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
            Add(new LauncherResult("Downloads", downloads, "System destinations", $"explorer|{downloads}", "\uE896", "downloaded files"));

        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string title = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.Name.TrimEnd('\\')} Drive" : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
                Add(new LauncherResult(title, $"{drive.DriveType} · {drive.TotalFreeSpace / (1024L * 1024 * 1024)} GB free",
                    "System destinations", $"explorer|{drive.Name}", "\uEDA2", "drive disk storage files"));
            }
        }
        catch { /* Removable and unavailable drives are not required for launcher startup. */ }

        string configDirectory = Path.GetDirectoryName(ConfigService.GetConfigPath())!;
        string logDirectory = Path.GetDirectoryName(ConfigService.GetLogPath())!;
        Add(new LauncherResult("Monolith Settings", "Open Monolith configuration", "Monolith",
            "monolith:settings", "\uE713", "preferences options"));
        Add(new LauncherResult("Configuration folder", configDirectory, "Monolith",
            $"explorer|{configDirectory}", "\uE8B7", "config settings files"));
        Add(new LauncherResult("Logs folder", logDirectory, "Monolith",
            $"explorer|{logDirectory}", "\uE9D9", "diagnostics troubleshooting"));
        Add(new LauncherResult("Install folder", AppContext.BaseDirectory, "Monolith",
            $"explorer|{AppContext.BaseDirectory}", "\uE8B7", "program files binaries"));

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
        };
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var extension = Path.GetExtension(file);
                    if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) continue;
                    var title = Path.GetFileNameWithoutExtension(file);
                    if (title.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    var result = new LauncherResult(title, string.Empty,
                        extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ? "Applications" : "Applications",
                        file, extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ? "\uE774" : null);
                    // Root level shortcuts are usually the user's actual apps; nested folders
                    // tend to be SDK tools and administrative entries. Keep both searchable.
                    result.IsProminent = string.Equals(Path.GetDirectoryName(file)?.TrimEnd(Path.DirectorySeparatorChar),
                        root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
                    Add(result);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* A protected or unavailable Start Menu folder should not block launch. */ }
        }

        try { AddPackagedApplications(Add, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { /* Packaged app enumeration is optional on Windows editions without the shell namespace. */ }
        return indexed;
    }

    private static void AddPackagedApplications(Action<LauncherResult> add, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return;
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return;
        object? shellObject = null;
        object? folderObject = null;
        object? itemsObject = null;
        try
        {
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null) return;
            dynamic shell = shellObject;
            folderObject = shell.Namespace("shell:AppsFolder");
            if (folderObject is null) return;
            dynamic folder = folderObject;
            itemsObject = folder.Items();
            if (itemsObject is null) return;
            dynamic items = itemsObject;
            int count = (int)items.Count;
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dynamic item = items.Item(i);
                string title = Convert.ToString(item.Name) ?? string.Empty;
                string appId = Convert.ToString(item.Path) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(appId)) continue;
                var result = new LauncherResult(title, "Packaged application", "Applications",
                    $"shell:AppsFolder\\{appId}", null, appId)
                {
                    IsProminent = true
                };
                add(result);
            }
        }
        finally
        {
            if (itemsObject is not null && Marshal.IsComObject(itemsObject)) Marshal.FinalReleaseComObject(itemsObject);
            if (folderObject is not null && Marshal.IsComObject(folderObject)) Marshal.FinalReleaseComObject(folderObject);
            if (shellObject is not null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
        }
    }
}

public sealed class LauncherSearchService : ILauncherSearchService
{
    private readonly IApplicationCatalog _catalog;
    private readonly IReadOnlyList<LauncherResult> _settings = WindowsSettingsCatalog.Create();

    public LauncherSearchService(IApplicationCatalog catalog) => _catalog = catalog;

    public IReadOnlyList<LauncherResultGroup> Search(string query)
    {
        var appResults = Rank(_catalog.Applications, query);
        var settingsResults = Rank(_settings, query);
        var systemResults = Rank(_catalog.Applications.Where(item => item.Category == "System destinations"), query);
        var monolithResults = Rank(_catalog.Applications.Where(item => item.Category == "Monolith"), query);
        appResults = appResults.Where(item => item.Category == "Applications").ToList();

        var groups = new List<LauncherResultGroup>();
        if (appResults.Count > 0) groups.Add(new LauncherResultGroup("applications", "Applications", appResults));
        if (settingsResults.Count > 0) groups.Add(new LauncherResultGroup("settings", "Windows settings", settingsResults));
        if (monolithResults.Count > 0) groups.Add(new LauncherResultGroup("monolith", "Monolith", monolithResults));
        if (systemResults.Count > 0) groups.Add(new LauncherResultGroup("system", "System destinations", systemResults));
        return groups;
    }

    private static List<LauncherResult> Rank(IEnumerable<LauncherResult> source, string query)
    {
        var results = source.Select(item => (Item: item, Score: Score(item, query)))
            .Where(match => match.Score >= 0);
        return string.IsNullOrWhiteSpace(query)
            ? results.Select(match => match.Item)
                .OrderByDescending(item => LauncherUsageStore.GetCount(item.ActivationTarget))
                .ThenByDescending(item => item.IsProminent)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ToList()
            : results.OrderByDescending(match => match.Score)
                .ThenByDescending(match => LauncherUsageStore.GetCount(match.Item.ActivationTarget))
                .ThenBy(match => match.Item.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(match => match.Item).ToList();
    }

    private static int Score(LauncherResult result, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        string normalizedQuery = Normalize(query);
        string title = Normalize(result.Title);
        string description = Normalize(result.Description ?? string.Empty);
        string[] aliases = result.SearchAliases.Select(Normalize).ToArray();
        string[] terms = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return 0;

        int total = 0;
        foreach (string term in terms)
        {
            int titleScore = MatchText(title, term, 10000);
            int aliasScore = aliases.Select(alias => MatchText(alias, term, 2400)).DefaultIfEmpty(-1).Max();
            int descriptionScore = MatchText(description, term, 1400);
            int best = Math.Max(titleScore, Math.Max(aliasScore, descriptionScore));
            if (best < 0) return -1;
            total += best;
        }

        if (title == normalizedQuery) total += 50000;
        else if (title.StartsWith(normalizedQuery, StringComparison.Ordinal)) total += 25000;
        return total;
    }

    private static int MatchText(string text, string term, int weight)
    {
        if (string.IsNullOrEmpty(text)) return -1;
        if (text == term) return weight;
        if (text.StartsWith(term, StringComparison.Ordinal)) return weight - 50;
        int wordIndex = text.IndexOf(" " + term, StringComparison.Ordinal);
        if (wordIndex >= 0) return weight - 150 - wordIndex;
        int index = text.IndexOf(term, StringComparison.Ordinal);
        if (index >= 0) return weight - 500 - index;
        int fuzzy = FuzzyScore(text, term);
        return fuzzy < 0 ? -1 : Math.Max(0, weight / 3 - fuzzy);
    }

    private static int FuzzyScore(string text, string term)
    {
        int cursor = 0, gap = 0;
        foreach (char character in term)
        {
            int found = text.IndexOf(character, cursor);
            if (found < 0) return -1;
            gap += found - cursor;
            cursor = found + 1;
        }
        return gap;
    }

    private static string Normalize(string value) => string.Join(' ', value.Trim().ToLowerInvariant()
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

internal static class LauncherUsageStore
{
    private static readonly object Gate = new();
    private static readonly string UsagePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monolith", "LauncherUsage.json");
    private static Dictionary<string, int>? _counts;

    public static int GetCount(string target)
    {
        lock (Gate)
        {
            EnsureLoaded();
            return _counts!.TryGetValue(target, out int count) ? count : 0;
        }
    }

    public static void RecordLaunch(LauncherResult result)
    {
        if (!string.Equals(result.Category, "Applications", StringComparison.OrdinalIgnoreCase)) return;
        lock (Gate)
        {
            EnsureLoaded();
            _counts![result.ActivationTarget] = GetCountUnsafe(result.ActivationTarget) + 1;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UsagePath)!);
                File.WriteAllText(UsagePath, JsonSerializer.Serialize(_counts));
            }
            catch { /* Usage ranking is optional and must never block launching an app. */ }
        }
    }

    private static int GetCountUnsafe(string target) => _counts!.TryGetValue(target, out int count) ? count : 0;

    private static void EnsureLoaded()
    {
        if (_counts is not null) return;
        try
        {
            var loaded = File.Exists(UsagePath)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(BoundedFile.ReadAllText(UsagePath, 1024 * 1024))
                : null;
            _counts = new Dictionary<string, int>(loaded ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

public sealed class LauncherActivationService : ILauncherActivationService
{
    public Task LaunchAsync(LauncherResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = result.ActivationTarget;
        if (target.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{target}\"", UseShellExecute = true });
        }
        else if (target.StartsWith("explorer|", StringComparison.OrdinalIgnoreCase))
        {
            string destination = target["explorer|".Length..];
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{destination}\"", UseShellExecute = true });
        }
        else
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        return Task.CompletedTask;
    }
}

public sealed class LauncherResultGroup : INotifyPropertyChanged
{
    private const int InitialVisibleCount = 5;
    private bool _isExpanded;
    private IReadOnlyList<LauncherResult> _allResults;

    public LauncherResultGroup(string id, string title, IReadOnlyList<LauncherResult> results, bool expanded = false)
    {
        Id = id;
        Title = title;
        _allResults = results;
        VisibleResults = new ObservableCollection<LauncherResult>();
        SetExpanded(expanded, notify: false);
    }

    public string Id { get; }
    public string Title { get; }
    public ObservableCollection<LauncherResult> VisibleResults { get; }
    public IReadOnlyList<LauncherResult> AllResults => _allResults;
    public int TotalCount => _allResults.Count;
    public bool IsExpanded => _isExpanded;
    public bool CanExpand => _allResults.Count > InitialVisibleCount;
    public string CountLabel => _allResults.Count > InitialVisibleCount ? $"{_allResults.Count} results" : string.Empty;
    public string ExpandLabel => _allResults.Count <= InitialVisibleCount ? string.Empty
        : _isExpanded ? "Show less  " : "Show all  ";
    public Visibility ExpandVisibility => _allResults.Count > InitialVisibleCount ? Visibility.Visible : Visibility.Collapsed;

    public void Toggle()
    {
        SetExpanded(!_isExpanded);
    }

    public void SetExpanded(bool expanded) => SetExpanded(expanded, notify: true);

    private void SetExpanded(bool expanded, bool notify)
    {
        _isExpanded = expanded;
        VisibleResults.Clear();
        foreach (var result in _allResults.Take(_isExpanded ? _allResults.Count : InitialVisibleCount)) VisibleResults.Add(result);
        if (notify)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class LauncherResult : INotifyPropertyChanged
{
    private bool _isSelected;
    private Task<string?>? _iconLoadTask;

    public LauncherResult(string title, string? description, string category, string activationTarget,
        string? glyph = null, params string[] searchAliases)
    {
        Id = $"{category}:{activationTarget}";
        Title = title;
        Description = description;
        Category = category;
        ActivationTarget = activationTarget;
        Glyph = glyph ?? (category == "Windows settings" ? "\uE713" : "\uECAA");
        SearchAliases = searchAliases;
    }

    public string Id { get; }
    public string Title { get; }
    public string? Description { get; }
    public Visibility DescriptionVisibility => string.IsNullOrWhiteSpace(Description) ? Visibility.Collapsed : Visibility.Visible;
    public string Category { get; }
    public string ActivationTarget { get; }
    public string IconKey => ActivationTarget;
    public string Glyph { get; }
    public bool IsProminent { get; set; }
    public IReadOnlyList<string> SearchAliases { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionBorderBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionBackgroundBrush)));
        }
    }

    public Brush SelectionBorderBrush => new SolidColorBrush(IsSelected ? AccentColorService.GetAccentColor() : Windows.UI.Color.FromArgb(0, 0, 0, 0));
    public Brush SelectionBackgroundBrush => new SolidColorBrush(IsSelected
        ? Windows.UI.Color.FromArgb(38, AccentColorService.GetAccentColor().R, AccentColorService.GetAccentColor().G, AccentColorService.GetAccentColor().B)
        : Windows.UI.Color.FromArgb(0, 0, 0, 0));

    public async Task<string?> LoadIconAsync() => await (_iconLoadTask ??= Task.Run(() => ShellIconCache.GetIconPath(ActivationTarget)));
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal static class WindowsSettingsCatalog
{
    public static IReadOnlyList<LauncherResult> Create()
    {
        if (!OperatingSystem.IsWindows() || Environment.OSVersion.Version.Major < 10) return Array.Empty<LauncherResult>();
        int build = Environment.OSVersion.Version.Build;
        var results = new List<LauncherResult>
        {
            new("Display", "Change brightness, scale, and display arrangement", "Windows settings", "ms-settings:display", "\uE7F4", "screen monitor brightness resolution"),
            new("Sound", "Change sound levels, inputs, and outputs", "Windows settings", "ms-settings:sound", "\uE767", "audio volume microphone speakers"),
            new("Network & internet", "Manage Wi-Fi, Ethernet, and network status", "Windows settings", "ms-settings:network-status", "\uE701", "wifi internet ethernet"),
            new("Bluetooth", "Connect Bluetooth devices", "Windows settings", "ms-settings:bluetooth", "\uE702", "devices pair headphones"),
            new("Personalization", "Change background, colors, and themes", "Windows settings", "ms-settings:personalization", "\uE790", "wallpaper background accent theme"),
            new("Apps", "Manage installed apps and optional features", "Windows settings", "ms-settings:appsfeatures", "\uE71D", "installed applications uninstall"),
            new("Windows Update", "Check for Windows updates", "Windows settings", "ms-settings:windowsupdate", "\uE777", "update upgrade"),
            new(build >= 22000 ? "Privacy & security" : "Privacy", "Manage app permissions and device security", "Windows settings", "ms-settings:privacy", "\uE72E", "permissions camera location privacy security"),
            new("Date & time", "Set the time zone and clock", "Windows settings", "ms-settings:dateandtime", "\uE823", "clock timezone date"),
            new("Storage", "View storage use and cleanup options", "Windows settings", "ms-settings:storagesense", "\uEDA2", "disk space cleanup drive"),
            new("Notifications", "Choose which alerts appear", "Windows settings", "ms-settings:notifications", "\uE91C", "alerts do not disturb"),
            new("Accessibility", "Adjust vision, hearing, and interaction settings", "Windows settings", "ms-settings:easeofaccess", "\uE776", "accessibility captions magnifier"),
            new("About", "View device specifications and Windows version", "Windows settings", "ms-settings:about", "\uE946", "device pc system information")
        };
        results.Add(new LauncherResult("Power & sleep", "Set power mode and screen sleep behavior", "Windows settings", "ms-settings:powersleep", "\uEBAA", "power sleep battery"));
        results.Add(new LauncherResult("Mouse", "Change pointer size, color, and behavior", "Windows settings", "ms-settings:easeofaccess-mouse", "\uE962", "pointer cursor touchpad trackpad"));
        if (build >= 22000)
            results.Add(new LauncherResult("Default apps", "Choose default apps for links and file types", "Windows settings", "ms-settings:defaultapps", "\uE74C", "browser file associations"));
        return results;
    }
}
