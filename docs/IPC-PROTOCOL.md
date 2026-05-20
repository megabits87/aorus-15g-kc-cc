# IPC protocol

The `Gbt.Service` Windows service exposes its API to the WPF UI over a Windows
named pipe, framed as JSON-RPC 2.0 via [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc).

## Endpoint

| Setting    | Value                                                                     |
| ---------- | ------------------------------------------------------------------------- |
| Transport  | Named pipe, byte mode, `PipeDirection.InOut`, `PipeOptions.Asynchronous`. |
| Pipe path  | `\\.\pipe\gbt-service` (override via `appsettings.json` → `Ipc:PipeName`). |
| Encoding   | UTF-8, JSON, line-delimited per StreamJsonRpc default.                    |
| Framing    | StreamJsonRpc length-prefixed framing.                                    |
| Auth       | OS-level ACL: `BUILTIN\Users` get `Read | Write`; only `LocalSystem` may listen. |

The server creates a new `NamedPipeServerStream` instance per connection, so
multiple UIs (or a UI + dump tool) can coexist.

## Contract

The wire surface is the [`IGbtService`](../src/Gbt.Common/Models.cs) interface
in `Gbt.Common`. Both ends bind to the same type; no hand-rolled JSON.

```csharp
public interface IGbtService
{
    Task<SensorSnapshot> GetSnapshotAsync();

    Task<PerformanceProfile> GetProfileAsync();
    Task SetPerformanceModeAsync(PerformanceMode mode);
    Task SetCustomProfileAsync(PerformanceProfile profile);

    Task<BatteryChargeLimit> GetBatteryChargeLimitAsync();
    Task SetBatteryChargeLimitAsync(BatteryChargeLimit limit);

    Task<DiagnosticsReport> RunDiagnosticsAsync();

    IAsyncEnumerable<SensorSnapshot> SubscribeAsync(CancellationToken ct);
}
```

JSON-RPC method names are the unprefixed C# method names (StreamJsonRpc default).
For example, the wire call for `SetPerformanceModeAsync(Boost)` is:

```json
{ "jsonrpc": "2.0", "id": 4, "method": "SetPerformanceModeAsync", "params": ["Boost"] }
```

`PerformanceMode` is serialized as the enum string ("Quiet", "Normal", "Gaming",
"Boost", "Custom"). All numeric fields are integers in their natural unit
(watts, %, RPM, °C).

## Streaming sensor snapshots

`SubscribeAsync` returns an `IAsyncEnumerable<SensorSnapshot>` that the client
consumes via `await foreach`. The server yields one snapshot every second.

```csharp
await foreach (var snap in proxy.SubscribeAsync(ct))
{
    // bind to UI…
}
```

Under the hood StreamJsonRpc maps this to a JSON-RPC subscription with
incremental notifications; the client cancellation token aborts the stream
on the server side.

## Errors

Server-side exceptions surface as JSON-RPC error responses. Notable error
types the client should be ready for:

| Exception                          | Origin                                  | Meaning                                                  |
| ---------------------------------- | --------------------------------------- | -------------------------------------------------------- |
| `ArgumentOutOfRangeException`      | `BatteryChargeLimit`, `FanCurvePoint`   | Caller sent an out-of-range value (50..100, 0..100).     |
| `ArgumentException`                | `SetCustomProfileAsync`                 | `Mode` was not `Custom`.                                  |
| `ArgumentNullException`            | most setters                            | Required record argument was missing.                    |
| `EcAccessViolationException` (P1+) | `Gbt.Hardware`                          | The implementation refused to write to a non-whitelisted EC register. |
| `WinRing0NotInstalledException` (P1+) | `Gbt.Hardware`                       | WinRing0 driver / DLL missing from the service host directory. |
| `MsrLockedException` (P1+)         | `Gbt.Hardware`                          | `MSR_PKG_POWER_LIMIT` bit 63 set; firmware refuses PL1/PL2 writes. |

## Versioning

- The contract is **additive**. New methods may be added at any time; existing
  signatures do not change once shipped.
- Renames go through an obsolescence cycle: new method first, mark old
  `[Obsolete]`, remove only in the following major.
- DTO records are versioned by name. Adding fields with sensible defaults is
  non-breaking; removing fields is breaking.

## Phase 0 vs Phase 1

In Phase 0 the contract is served by `InMemoryGbtService`. It returns plausible
but stub values (`CpuPackageC = NaN`, fan RPMs = 0, model name = "(stub)") so
that the UI follow-up task can wire its bindings and animations without
waiting for hardware code. Phase 1 swaps in a real implementation behind the
same DI registration; the WPF client is none the wiser.
