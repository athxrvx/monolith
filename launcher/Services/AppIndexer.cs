using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Monolith.Models;

namespace Monolith.Services;

public sealed class AppIndexer
{
    private static readonly Lazy<ImageSource> FallbackAppIcon = new(CreateFallbackIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    public async Task<List<AppItem>> IndexApplicationsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<AppItem>();
            var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Built-in Monolith Engine Commands
            AddEngineCommands(results, seenTitles);

            // 2. Built-in Windows System Tools
            AddSystemTools(results, seenTitles);

            // 3. Scan User and Common Start Menu Directories
            var searchPaths = new List<string>();

            string userStartMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            if (Directory.Exists(userStartMenu)) searchPaths.Add(userStartMenu);

            string commonStartMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
            if (Directory.Exists(commonStartMenu)) searchPaths.Add(commonStartMenu);

            foreach (var rootPath in searchPaths)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        string ext = Path.GetExtension(file);
                        if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                            !ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string title = Path.GetFileNameWithoutExtension(file);

                        // Filter unhelpful uninstallers or helpers
                        if (title.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                            title.Contains("Readme", StringComparison.OrdinalIgnoreCase) ||
                            title.Contains("Help", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!seenTitles.Add(title)) continue;

                        string subtitle = file;
                        string targetPath = file;

                        if (ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
                        {
                            string? url = TryParseUrlFile(file);
                            if (!string.IsNullOrEmpty(url))
                            {
                                subtitle = url;
                                targetPath = url;
                            }
                        }

                        var item = new AppItem
                        {
                            Title = title,
                            Subtitle = subtitle,
                            TargetPath = targetPath,
                            Category = ext.Equals(".url", StringComparison.OrdinalIgnoreCase) ? "Web Application" : "Application"
                        };

                        // Extract icon safely
                        try
                        {
                            item.Icon = ExtractIconFromFile(file) ?? FallbackAppIcon.Value;
                        }
                        catch
                        {
                            item.Icon = FallbackAppIcon.Value;
                        }

                        results.Add(item);
                    }
                }
                catch
                {
                    // Ignore inaccessible permission directories
                }
            }

            return results;
        }, cancellationToken);
    }

    private static void AddEngineCommands(List<AppItem> results, HashSet<string> seen)
    {
        void AddCmd(string title, string subtitle, string command, object? args = null)
        {
            if (seen.Add(title))
            {
                results.Add(new AppItem
                {
                    Title = title,
                    Subtitle = subtitle,
                    TargetPath = string.Empty,
                    Category = "Action",
                    IsEngineCommand = true,
                    EngineCommand = command,
                    EngineArgs = args,
                    Icon = FallbackAppIcon.Value
                });
            }
        }

        AddCmd("Open Settings", "Open the desktop environment settings", "OPEN_STUDIO");
        AddCmd("Reload Configuration", "Reload the saved window manager configuration", "RELOAD_CONFIG");
        AddCmd("Toggle Floating", "Toggle the active window between tiled and floating", "TOGGLE_FLOATING");

        for (int i = 1; i <= 6; i++)
        {
            AddCmd($"Switch to Workspace {i}", $"Switch to virtual desktop {i}", "FOCUS_WORKSPACE", new { workspace = i });
        }
    }

    private static void AddSystemTools(List<AppItem> results, HashSet<string> seen)
    {
        void AddSys(string title, string target, string subtitle)
        {
            if (seen.Add(title))
            {
                results.Add(new AppItem
                {
                    Title = title,
                    Subtitle = subtitle,
                    TargetPath = target,
                    Category = "System Tool",
                    Icon = ExtractIconFromFile(target) ?? FallbackAppIcon.Value
                });
            }
        }

        AddSys("Task Manager", "taskmgr.exe", "System Resource & Process Monitor");
        AddSys("Windows Terminal", "wt.exe", "Modern Windows Terminal");
        AddSys("PowerShell", "powershell.exe", "Windows PowerShell CLI");
        AddSys("Command Prompt", "cmd.exe", "Windows Command Processor");
        AddSys("File Explorer", "explorer.exe", "Files and Folders");
        AddSys("Windows Settings", "ms-settings:", "System Configuration");
    }

    private static string? TryParseUrlFile(string filePath)
    {
        try
        {
            foreach (var line in File.ReadLines(filePath))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(4).Trim();
                }
            }
        }
        catch { }
        return null;
    }

    public static ImageSource? ExtractIconFromFile(string filePath)
    {
        var shinfo = new SHFILEINFO();
        IntPtr hImg = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);

        if (hImg == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                shinfo.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            DestroyIcon(shinfo.hIcon);
        }
    }

    private static ImageSource CreateFallbackIcon()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0, 229, 255));
            brush.Freeze();
            dc.DrawRoundedRectangle(brush, null, new Rect(4, 4, 24, 24), 6, 6);
        }

        var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}

