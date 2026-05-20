# Architecture

> Phase-0 reference. Updated as Phase 1 lands real hardware backends.

## Goals

1. **Replace** the broken **GIGABYTE AORUS Control Center** for the AORUS 15G KC laptop on Windows 11.
2. Skip the two known crash sites of the original app:
   - `Intel.Overclocking.SDK.Tuning.TuningLibrary.GetSpeedOptimizerState()` returning `null`
     on a locked H-series CPU; never call the XTU SDK in the first place.
   - `SmartManager.WlanClient..ctor` throwing inside `ManagedWifi`; never use that wrapper.
3. Make every EC and MSR write **auditable and reversible**, so a buggy
   profile cannot brick the hardware.

## Process model

```
+--------------------------+         named pipe \\.\pipe\gbt-service        +-------------------------+
|  Gbt.Service             |  <===== JSON-RPC (StreamJsonRpc) =====>        |  Gbt.ControlCenter      |
|  Windows Service         |                                                 |  WPF desktop app        |
|  Runs as LocalSystem     |                                                 |  Runs as logged-in user |
|                          |                                                 |                         |
|  +--------------------+  |                                                 |  +-------------------+  |
|  | InMemoryGbtService |  |     <-- Phase 0 stub                            |  |   MVVM ViewModels |  |
|  |  (Phase 1 → real)  |  |                                                 |  +-------------------+  |
|  +--------------------+  |                                                 |                         |
|  | HeartbeatWorker    |  |                                                 |                         |
|  +--------------------+  |                                                 |                         |
|  | JsonRpcServer      |  |                                                 |                         |
|  +--------------------+  |                                                 |                         |
+--------------------------+                                                 +-------------------------+
            |                                                                          |
            | Phase 1 →                                                                |  Gbt.Tools.DumpEc
            |   WinRing0EcController                                                   |  (separate exe, admin,
            |   WinRing0MsrController                                                  |   read-only by default)
            |   WmiClient (root\WMI GBT*)                                              |
            |   LhmSensorService                                                       |
            |   BatteryService                                                         |
            v                                                                          v
+-----------------------------------------------------------------------------------------------------+
|                                       Hardware                                                      |
|   EC ports 0x62/0x66  •  MSR 0x610 (PL1/PL2)  •  WMI \\root\\WMI  •  USB 1044:7A3C (Fusion RGB)     |
+-----------------------------------------------------------------------------------------------------+
```

The split has two reasons:

- **Privilege.** WinRing0 needs to load a kernel driver and talk to MSRs / I/O
  ports — only LocalSystem has that right. WPF must not run elevated.
- **Lifecycle.** The fan-curve loop and the persisted Performance Profile must
  survive a user log-off. A Windows Service is the only host that lives across
  sessions.

## Threading model

| Thread / pool       | Owner                  | Purpose                                                |
| ------------------- | ---------------------- | ------------------------------------------------------ |
| Main / host         | `Gbt.Service`          | `IHost` lifecycle, DI scope                             |
| `HeartbeatWorker`   | `Gbt.Service`          | One-minute log heartbeat                               |
| `JsonRpcServer`     | `Gbt.Service`          | Accept loop on the named pipe                          |
| RPC client task     | `Gbt.Service` (pooled) | One `Task` per connected UI; lives until disconnect    |
| **Phase 1 →** Fan-curve loop | `Gbt.Service` | 2 Hz: read temps → compute duty → EC write           |
| **Phase 1 →** Sensor broadcaster | `Gbt.Service` | 1 Hz: build `SensorSnapshot`, fan out to clients |
| UI thread           | `Gbt.ControlCenter`    | WPF / Dispatcher                                       |
| RPC reader thread   | `Gbt.ControlCenter`    | Streams `IAsyncEnumerable<SensorSnapshot>` to VM       |

The shared mutable state inside `InMemoryGbtService` (current `PerformanceProfile`,
`BatteryChargeLimit`) is guarded by a private lock. The Phase 1 real
implementation keeps the same locking shape so the IPC layer does not have to
care which backend is wired.

## Settings persistence

- Storage root resolves to `%ProgramData%\GbtControlCenter` by default (override
  via `appsettings.json` → `Storage:Root`).
- `settings.json` holds the last applied profile and the battery charge limit.
- Writes are atomic: write to `.tmp`, fsync, rename. Loaded at startup.
- Phase 0 stub does not yet persist — Phase 1 wires `PerformanceProfilePersister`.

## Kill switch

On graceful service stop:

1. The IPC server stops accepting new clients.
2. `FanCurveEngine` is stopped; the EC fan-control-mode register is restored to "auto".
3. `PerformanceModeApplier` re-applies the **Normal** preset (PL1=45 W, PL2=60 W) via MSR.
4. Settings are flushed.

If the service is *not* stopped gracefully (power loss, crash), the EC will
naturally revert to its firmware default fan curve on the next AC reseat; the
laptop is never left in a state where the fans are pinned off.

## Out of scope (and why)

| Original CC feature             | Why we drop it                                                                    |
| ------------------------------- | --------------------------------------------------------------------------------- |
| Intel XTU / Speed Optimizer      | i7-10870H is locked. The SDK is the source of crash #1. PL1/PL2 via MSR replaces it. |
| `ManagedWifi.WlanClient`         | Source of crash #2. We read Wi-Fi state via `System.Net.NetworkInformation` only. |
| Azure AI "performance optimization" | Proprietary, marginal value, not reproducible without the CC backend.          |
| Smart Update (driver downloads)  | Cannot legally redistribute GIGABYTE drivers; users use Windows Update / GB site. |
| Per-app launcher (Manager)       | Out of MVP; Phase 3 if there is demand.                                           |

## See also

- [`EC-REGISTERS.md`](EC-REGISTERS.md) — what the service is allowed to read/write on the EC.
- [`IPC-PROTOCOL.md`](IPC-PROTOCOL.md) — the JSON-RPC contract that UI clients bind to.
- [`THIRD-PARTY.md`](THIRD-PARTY.md) — licenses of bundled binaries.
