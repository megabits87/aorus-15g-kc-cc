using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Gbt.Common;
using StreamJsonRpc;

namespace Gbt.ControlCenter;

/// <summary>
/// Connects the WPF UI (running as the logged-in user) to <c>Gbt.Service</c> (LocalSystem) over the
/// named pipe and exposes a strongly-typed <see cref="IGbtService"/> proxy via StreamJsonRpc. Server
/// and client both use JsonRpc defaults, so the method names line up without extra configuration.
/// </summary>
public sealed class GbtServiceClient : IAsyncDisposable
{
    private const string PipeName = "gbt-service";

    private NamedPipeClientStream? _pipe;
    private JsonRpc? _rpc;

    public IGbtService? Service { get; private set; }

    public bool IsConnected => _pipe is { IsConnected: true };

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(5000, ct).ConfigureAwait(false);
        _rpc = JsonRpc.Attach(_pipe);
        Service = _rpc.Attach<IGbtService>();
    }

    public async ValueTask DisposeAsync()
    {
        _rpc?.Dispose();
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
        }
    }
}
