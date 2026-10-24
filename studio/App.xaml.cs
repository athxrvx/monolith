using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.UI.Xaml;
using Monolith.Studio.ViewModels;
using Monolith.Studio.Services;

namespace Monolith.Studio;

public partial class App : Application
{
    private const int SwRestore = 9;
    private const int SwShow = 5;
    private const int GwlpExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private static Mutex? _singleInstanceMutex;
    private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    private Window? _window;
    internal static bool IsBackgroundLaunch { get; private set; }
    public static MainViewModel MainViewModel { get; } = new();
    public static Action? RefreshHotkeys { get; set; }
    public static Action<bool>? SetHotkeysEnabled { get; set; }
    public static MainWindow? MainWindowInstance { get; set; }

    internal static void LogStartup(string message, Exception? exception = null) => WriteStartupLog(message, exception);

    public App()
    {
        WriteStartupLog("Application constructor entered", null);
        InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;
        WriteStartupLog("Application constructed", null);
        UnhandledException += (_, e) => WriteStartupLog("Unhandled WinUI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteStartupLog("Unhandled process exception", e.ExceptionObject as Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            IsBackgroundLaunch = string.Equals(args.Arguments?.Trim(), StartupRegistrationService.BackgroundArgument, StringComparison.OrdinalIgnoreCase)
                || Environment.GetCommandLineArgs().Skip(1).Any(argument =>
                    string.Equals(argument, StartupRegistrationService.BackgroundArgument, StringComparison.OrdinalIgnoreCase));
            WriteStartupLog("Launch received", null);
            WriteStartupLog(IsBackgroundLaunch ? "Starting in background mode" : "Starting with settings window", null);
            WriteStartupLog("Single-instance check started", null);
            bool isFirstInstance = TryAcquireSingleInstance();
            WriteStartupLog($"Single-instance check finished: {(isFirstInstance ? "first instance" : "existing instance found")}", null);
            if (!isFirstInstance)
            {
                bool activated = ActivateExistingInstance();
                WriteStartupLog($"Existing instance activation: {(activated ? "window restored" : "window not found")}", null);
                Current.Exit();
                return;
            }

            // Construct the shell in both modes so tray actions and hotkeys are available.
            _window = new MainWindow();
            WriteStartupLog("Main window constructed", null);
            if (IsBackgroundLaunch)
            {
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
                ShowWindow(handle, 0); // SW_HIDE; keep the settings shell available to tray actions.
                WriteStartupLog("Settings window kept hidden for background launch", null);
            }
            else
            {
                _window.Activate();
                WriteStartupLog("Main window activated", null);
            }
            _ = InitializeViewModelAsync();
        }
        catch (Exception ex)
        {
            WriteStartupLog("Failed to create the main window", ex);
            throw;
        }
    }

    private static bool TryAcquireSingleInstance()
    {
        string user = Environment.UserName;
        var mutex = new Mutex(false, $"Local\\Monolith.Studio.{user}");
        try
        {
            if (mutex.WaitOne(0))
            {
                _singleInstanceMutex = mutex;
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            // The prior process exited unexpectedly. This thread now owns the mutex.
            _singleInstanceMutex = mutex;
            return true;
        }

        mutex.Dispose();
        return false;
    }

    private static bool ActivateExistingInstance()
    {
        foreach (var process in Process.GetProcessesByName("Monolith"))
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                    continue;

                IntPtr targetWindow = IntPtr.Zero;
                int targetPid = process.Id;
                EnumWindows((hwnd, _) =>
                {
                    GetWindowThreadProcessId(hwnd, out uint ownerPid);
                    if (ownerPid != targetPid) return true;
                    if ((GetWindowLongPtr(hwnd, GwlpExStyle).ToInt64() & WsExToolWindow) != 0) return true;
                    var title = new StringBuilder(128);
                    GetWindowText(hwnd, title, title.Capacity);
                    if (!string.Equals(title.ToString(), "Monolith", StringComparison.OrdinalIgnoreCase)) return true;
                    targetWindow = hwnd;
                    return false;
                }, IntPtr.Zero);

                if (targetWindow == IntPtr.Zero)
                    continue;

                ShowWindow(targetWindow, SwShow);
                ShowWindow(targetWindow, SwRestore);
                bool foregrounded = SetForegroundWindow(targetWindow);
                WriteStartupLog($"Activated settings window for process {targetPid}; foreground request: {foregrounded}", null);
                return true;
            }
            catch (Exception ex)
            {
                WriteStartupLog("Could not activate the existing Monolith window", ex);
            }
            finally
            {
                process.Dispose();
            }
        }
        return false;
    }

    private static async System.Threading.Tasks.Task InitializeViewModelAsync()
    {
        try
        {
            WriteStartupLog("Settings initialization started", null);
            await MainViewModel.InitializeAsync();
            StartupRegistrationService.Apply(MainViewModel.Config.EngineStartup);
            TaskbarVisibilityService.Apply(MainViewModel.Config.HideTaskbar);
            AccentColorService.RefreshAccent();
            RefreshHotkeys?.Invoke();
            SetHotkeysEnabled?.Invoke(MainViewModel.Config.MonolithEnabled);
            if (App.Current is App configuredApp && configuredApp._window is MainWindow configuredMainWindow)
            {
                configuredMainWindow.SetLauncherAvailable(MainViewModel.Config.MonolithEnabled && MainViewModel.Config.LauncherEnabled);
                configuredMainWindow.ApplyMonobarConfiguration();
                if (!IsBackgroundLaunch && !MainViewModel.Config.OnboardingCompleted)
                    await configuredMainWindow.ShowOnboardingIfNeededAsync();
            }
            WriteStartupLog("Settings initialization finished", null);
        }
        catch (Exception ex)
        {
            WriteStartupLog("Settings initialized with an error", ex);
        }
    }

    private static void WriteStartupLog(string context, Exception? exception)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Monolith");
            Directory.CreateDirectory(folder);
            string detail = exception?.ToString() ?? "No exception details available.";
            File.AppendAllText(Path.Combine(folder, "monolith.log"), $"[{DateTimeOffset.Now:O}] {context}: {detail}{Environment.NewLine}");
        }
        catch
        {
            // Logging must not cause an additional startup failure.
        }
    }
}

