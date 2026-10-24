using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal static class InstallCommon
{
    internal const string ProductName = "Monolith";
    internal const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Monolith";
    private const string WorkspaceHiddenProperty = "Monolith.WorkspaceHidden";
    private const int SwShowNoActivate = 8;
    private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr parameter);
    internal static string ConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "monolith");
    internal static string LegacyDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Monolith");
    internal static string CacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monolith");
    internal static string DefaultInstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Monolith");

    internal static bool IsInstalled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
                string? registeredPath = key?.GetValue("InstallLocation") as string;
                return IsSafeInstallPath(registeredPath)
                    && File.Exists(Path.Combine(DefaultInstallDirectory, "Monolith.exe"));
            }
            catch { return false; }
        }
    }

    private static bool IsSafeInstallPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(DefaultInstallDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    internal static void Install(Stream payload, string destination, Action<int> progress)
    {
        string staging = Path.Combine(Path.GetTempPath(), $"Monolith-Setup-{Guid.NewGuid():N}");
        string installParent = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        string prepared = Path.Combine(installParent, $".Monolith-update-{Guid.NewGuid():N}");
        string backup = Path.Combine(installParent, $".Monolith-backup-{Guid.NewGuid():N}");
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

            Directory.CreateDirectory(installParent);
            Directory.CreateDirectory(prepared);
            foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(staging, file);
                string target = Path.Combine(prepared, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }

            progress(84);
            StopProcess("Monolith");
            StopProcess("monolith_engine");
            RestoreWorkspaceHiddenWindows();

            bool movedExisting = false;
            try
            {
                if (Directory.Exists(destination))
                {
                    Directory.Move(destination, backup);
                    movedExisting = true;
                }
                Directory.Move(prepared, destination);
                RegisterInstall(destination);
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Monolith.lnk"), Path.Combine(destination, "Monolith.exe"));
            }
            catch
            {
                if (movedExisting && Directory.Exists(backup))
                {
                    if (Directory.Exists(destination))
                        Directory.Move(destination, prepared);
                    Directory.Move(backup, destination);
                }
                throw;
            }

            if (movedExisting)
                TryDeleteDirectory(backup);
            progress(100);
        }
        finally
        {
            TryDeleteDirectory(staging);
            TryDeleteDirectory(prepared);
        }
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
                if (!process.WaitForExit(5000) && !process.HasExited)
                    throw new InvalidOperationException($"Could not stop {name}. Close Monolith and run the update again.");
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex) { throw new InvalidOperationException($"Could not stop {name}. Close Monolith and run the update again.", ex); }
            finally { process.Dispose(); }
        }
    }

    private static void RestoreWorkspaceHiddenWindows()
    {
        EnumWindows((hwnd, _) =>
        {
            if (GetProp(hwnd, WorkspaceHiddenProperty) != IntPtr.Zero)
            {
                RemoveProp(hwnd, WorkspaceHiddenProperty);
                ShowWindow(hwnd, SwShowNoActivate);
            }
            return true;
        }, IntPtr.Zero);
    }

    [DllImport("user32.dll", EntryPoint = "EnumWindows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "GetPropW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetProp(IntPtr hwnd, string name);

    [DllImport("user32.dll", EntryPoint = "RemovePropW", CharSet = CharSet.Unicode)]
    private static extern IntPtr RemoveProp(IntPtr hwnd, string name);

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    private static void DeleteShortcut(string path) { try { File.Delete(path); } catch { } }
    internal static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { } }
}
