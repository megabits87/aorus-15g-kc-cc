using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Gbt.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StreamJsonRpc;

namespace Gbt.Service;

/// <summary>
/// Hosts a Windows named-pipe server that serves <see cref="IGbtService"/> over
/// JSON-RPC (StreamJsonRpc). Each accepted client gets its own pipe instance and
/// its own JSON-RPC session sharing the same underlying <see cref="IGbtService"/>
/// implementation, so subscribe streams are independent but state changes are
/// observed by every connected UI.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class JsonRpcServer : BackgroundService
{
    private readonly IGbtService _impl;
    private readonly ILogger<JsonRpcServer> _logger;
    private readonly IpcOptions _options;
    private int _activeClients;

    public JsonRpcServer(
        IGbtService impl,
        IOptions<IpcOptions> options,
        ILogger<JsonRpcServer> logger)
    {
        _impl = impl;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JSON-RPC server starting on named pipe \\\\.\\pipe\\{Pipe}", _options.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipeInstance(_options.PipeName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to create pipe instance; retrying in 1s");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WaitForConnection failed; recycling pipe instance");
                await pipe.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _ = HandleClientAsync(pipe, stoppingToken);
        }

        _logger.LogInformation("JSON-RPC server stopping");
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        var active = Interlocked.Increment(ref _activeClients);
        _logger.LogInformation("Control Center client connected ({Active} active)", active);
        try
        {
            using var rpc = JsonRpc.Attach(pipe, _impl);
            using var _ = stoppingToken.Register(() => rpc.Dispose());
            await rpc.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JSON-RPC client session terminated abnormally");
        }
        finally
        {
            var remaining = Interlocked.Decrement(ref _activeClients);
            _logger.LogInformation("Control Center client disconnected ({Active} active)", remaining);
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static NamedPipeServerStream CreatePipeInstance(string pipeName)
    {
        var security = new PipeSecurity();

        // BUILTIN\Users: read + write so the WPF Control Center (running as the logged-in user)
        // can connect to the service (running as LocalSystem). No CreateNewInstance — only
        // the service itself, owned by SYSTEM, may listen.
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        security.AddAccessRule(new PipeAccessRule(
            users,
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
            AccessControlType.Allow));

        // Owner (the service principal) gets full control.
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(
            system,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            pipeSecurity: security);
    }
}
