using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Monolith.Services;

public sealed class IpcClient : IAsyncDisposable
{
    private const string PipeName = "monolith_ipc";
    private NamedPipeClientStream? _pipeClient;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private CancellationTokenSource? _cts;
    private Task? _readLoopTask;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public event Action<string>? StateReceived;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _pipeClient?.IsConnected ?? false;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = MonitorConnectionLoopAsync(_cts.Token);
    }

    private async Task MonitorConnectionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_pipeClient == null || !_pipeClient.IsConnected)
                {
                    await CleanupAsync();

                    var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    // Try connecting with a 2-second timeout
                    await client.ConnectAsync(2000, ct);

                    _pipeClient = client;
                    _writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                    _reader = new StreamReader(client, Encoding.UTF8);

                    ConnectionChanged?.Invoke(true);

                    // Start reading loop
                    _readLoopTask = ReadLoopAsync(ct);
                    await _readLoopTask;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                ConnectionChanged?.Invoke(false);
                await CleanupAsync();
            }

            // Retry after backoff
            try
            {
                await Task.Delay(2500, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _reader != null)
        {
            try
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null)
                {
                    // Pipe closed by server
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    StateReceived?.Invoke(line);
                }
            }
            catch
            {
                break;
            }
        }
        ConnectionChanged?.Invoke(false);
    }

    public async Task<bool> SendCommandAsync(string command, object? args = null)
    {
        if (_pipeClient == null || !_pipeClient.IsConnected || _writer == null)
            return false;

        var payload = new
        {
            source = "Monolith.Launcher",
            command,
            args = args ?? new { }
        };

        string json = JsonSerializer.Serialize(payload);

        await _writeLock.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(json);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task CleanupAsync()
    {
        if (_writer != null)
        {
            try { await _writer.DisposeAsync(); } catch { }
            _writer = null;
        }

        if (_reader != null)
        {
            try { _reader.Dispose(); } catch { }
            _reader = null;
        }

        if (_pipeClient != null)
        {
            try { await _pipeClient.DisposeAsync(); } catch { }
            _pipeClient = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        await CleanupAsync();
        _cts?.Dispose();
        _writeLock.Dispose();
    }
}

