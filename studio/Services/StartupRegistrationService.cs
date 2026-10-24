using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Monolith.Studio.Services;

/// <summary>Registers Monolith to launch quietly at the current user's sign-in.</summary>
public static class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Monolith";
    public const string BackgroundArgument = "--background";

    public static void Apply(bool enabled)
    {
        try
        {
            using var runKey = enabled
                ? Registry.CurrentUser.CreateSubKey(RunKeyPath)
                : Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                if (enabled)
                    throw new InvalidOperationException("Could not open the current user's startup registry key.");
                return;
            }

            if (!enabled)
            {
                if (runKey.GetValue(ValueName) is not null)
                {
                    runKey.DeleteValue(ValueName, throwOnMissingValue: false);
                    App.LogStartup("Removed Monolith from user sign-in startup");
                }
                return;
            }

            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
                executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new InvalidOperationException("Could not determine the Monolith executable path.");

            string command = $"\"{executablePath}\" {BackgroundArgument}";
            if (!string.Equals(runKey.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
            {
                runKey.SetValue(ValueName, command, RegistryValueKind.String);
                App.LogStartup($"Registered Monolith for user sign-in: {executablePath}");
            }
        }
        catch (Exception ex)
        {
            App.LogStartup("Could not update Monolith user sign-in startup registration", ex);
        }
    }
}
