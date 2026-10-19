using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Monolith.Studio.Services;

public class EngineIpcClient : IAsyncDisposable
{
    private const string PipeName = "monolith_ipc";
    private NamedPipeClientStream? _pipeClient;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public event Action<bool>? ConnectionChanged;
    public event Action<string>? StateReceived;

    public bool IsConnected => _pipeClient?.IsConnected ?? false;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = ConnectLoopAsync(_cts.Token);
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_pipeClient == null || !_pipeClient.IsConnected)
                {
                    await CleanupAsync();

                    var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await client.ConnectAsync(1500, ct);

                    _pipeClient = client;
                    _writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                    _reader = new StreamReader(client, Encoding.UTF8);

                    ConnectionChanged?.Invoke(true);

                    // Read loop
                    while (!ct.IsCancellationRequested && _reader != null)
                    {
                        var line = await _reader.ReadLineAsync(ct);
                        if (line == null) break;
                        StateReceived?.Invoke(line);
                    }
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

            try
            {
                await Task.Delay(2000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<bool> SendCommandAsync(string command, object? args = null)
    {
        if (_pipeClient == null || !_pipeClient.IsConnected || _writer == null)
            return false;

        var payload = new
        {
            source = "Monolith.Studio",
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

