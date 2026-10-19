using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal static class InstallCommon
{
    internal const string ProductName = "Monolith";
    internal const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Monolith";
    internal static string ConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "monolith");
    internal static string LegacyDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Monolith");
    internal static string CacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monolith");

    internal static void Install(Stream payload, string destination, Action<int> progress)
    {
        string staging = Path.Combine(Path.GetTempPath(), $"Monolith-Setup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true))
            {
                int total = Math.Max(archive.Entries.Count, 1), done = 0;
                foreach (var entry in archive.Entries)
                {
                    string output = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!output.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The installation archive contains an invalid file path.");
                    if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(output);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                        entry.ExtractToFile(output, overwrite: true);
                    }
                    progress(++done * 75 / total);
                }
            }

        StopProcess("Monolith");
        StopProcess("monolith_engine");
        TryDeleteDirectory(destination);
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(staging, file);
                string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
            progress(82);
            RegisterInstall(destination);
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Monolith.lnk"), Path.Combine(destination, "Monolith.exe"));
            progress(100);
        }
        finally { TryDeleteDirectory(staging); }
    }

    internal static void RegisterInstall(string destination)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
        key.SetValue("DisplayName", "Monolith");
        key.SetValue("DisplayVersion", "0.1.0");
        key.SetValue("Publisher", "Monolith contributors");
        key.SetValue("InstallLocation", destination);
        key.SetValue("DisplayIcon", Path.Combine(destination, "Monolith.exe"));
        key.SetValue("UninstallString", $"\"{Path.Combine(destination, "MonolithUninstall.exe")}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    internal static void CreateShortcut(string shortcutPath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is not available to create a Start menu shortcut.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
        shortcut.IconLocation = targetPath + ",0";
        shortcut.Description = "Monolith desktop environment";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    internal static void Uninstall(string installDirectory, bool removeUserData)
    {
        StopProcess("Monolith");
        StopProcess("monolith_engine");
        DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Monolith.lnk"));
        try { Registry.CurrentUser.DeleteSubKeyTree(RegistryKey, throwOnMissingSubKey: false); } catch { }
        TryDeleteDirectory(installDirectory);
        if (removeUserData)
        {
            TryDeleteDirectory(ConfigDirectory);
            TryDeleteDirectory(LegacyDataDirectory);
            TryDeleteDirectory(CacheDirectory);
        }
    }

    private static void StopProcess(string name)
    {
        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                if (process.CloseMainWindow()) process.WaitForExit(2500);
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit(2500);
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    private static void DeleteShortcut(string path) { try { File.Delete(path); } catch { } }
    internal static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { } }
}
