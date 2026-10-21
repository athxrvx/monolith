using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Monolith.Services;

public sealed class EngineSupervisor
{
    private static readonly Lazy<EngineSupervisor> _instance = new(() => new EngineSupervisor());
    public static EngineSupervisor Instance => _instance.Value;

    private Process? _engineProcess;
    private readonly string _targetExePath;
    private bool _desiredRunningState;

    public event Action<bool>? StateChanged;

    private EngineSupervisor()
    {
        string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monolith", "bin");
        Directory.CreateDirectory(localAppData);
        _targetExePath = Path.Combine(localAppData, "monolith_engine.exe");
    }

    public bool IsRunning
    {
        get
        {
            try
            {
                if (_engineProcess != null && !_engineProcess.HasExited)
                    return true;

                return ReadExternalEngineProcess(process => true, false);
            }
            catch
            {
                return false;
            }
        }
    }

    public int ProcessId
    {
        get
        {
            try
            {
                if (_engineProcess != null && !_engineProcess.HasExited)
                    return _engineProcess.Id;

                return ReadExternalEngineProcess(process => process.Id, 0);
            }
            catch
            {
                return 0;
            }
        }
    }

    public string MemoryUsage
    {
        get
        {
            try
            {
                return ReadExternalEngineProcess(
                    process => $"{process.WorkingSet64 / (1024.0 * 1024.0):F1} MB",
                    "0.0 MB");
            }
            catch { }
            return "0.0 MB";
        }
    }

    public async Task<bool> StartAsync()
    {
        _desiredRunningState = true;
        EnsureExtracted();

        if (IsRunning)
        {
            StateChanged?.Invoke(true);
            return true;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _targetExePath,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(_targetExePath)
            };

            _engineProcess = Process.Start(psi);
            if (_engineProcess != null)
            {
                _engineProcess.EnableRaisingEvents = true;
                _engineProcess.Exited += OnEngineExited;
                StateChanged?.Invoke(true);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start Monolith.Engine: {ex.Message}");
        }

        StateChanged?.Invoke(false);
        return false;
    }

    public void Stop()
    {
        _desiredRunningState = false;
        if (_engineProcess != null)
        {
            try
            {
                if (!_engineProcess.HasExited)
                {
                    _engineProcess.Kill(entireProcessTree: true);
                    _engineProcess.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to stop the supervised Monolith engine: {ex.Message}");
            }
            finally
            {
                _engineProcess.Dispose();
                _engineProcess = null;
            }
        }

        foreach (var process in Process.GetProcessesByName("monolith_engine"))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to stop Monolith engine process {process.Id}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        StateChanged?.Invoke(false);
    }

    private static T ReadExternalEngineProcess<T>(Func<Process, T> read, T fallback)
    {
        var processes = Process.GetProcessesByName("monolith_engine");
        try
        {
            return processes.Length > 0 ? read(processes[0]) : fallback;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    public async Task RestartAsync()
    {
        Stop();
        await Task.Delay(400);
        await StartAsync();
    }

    private void OnEngineExited(object? sender, EventArgs e)
    {
        StateChanged?.Invoke(false);

        // Auto restart if still desired
        if (_desiredRunningState)
        {
            Task.Delay(1000).ContinueWith(_ =>
            {
                if (_desiredRunningState && !IsRunning)
                {
                    _ = StartAsync();
                }
            });
        }
    }

    private void EnsureExtracted()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            // Try finding resource ending with monolith_engine.exe
            string? resourceName = null;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith("monolith_engine.exe", StringComparison.OrdinalIgnoreCase))
                {
                    resourceName = name;
                    break;
                }
            }

            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    bool needExtract = true;
                    if (File.Exists(_targetExePath))
                    {
                        var fi = new FileInfo(_targetExePath);
                        if (fi.Length == stream.Length)
                        {
                            needExtract = false;
                        }
                    }

                    if (needExtract)
                    {
                        // Stop any existing instance before overwriting
                        var procs = Process.GetProcessesByName("monolith_engine");
                        foreach (var p in procs) { try { p.Kill(); } catch { } }

                        using var fs = new FileStream(_targetExePath, FileMode.Create, FileAccess.Write);
                        stream.CopyTo(fs);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error extracting monolith_engine.exe: {ex.Message}");
        }
    }
}

