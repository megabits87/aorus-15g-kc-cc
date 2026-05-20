# AORUS 15G KC Open Control Center

Open-source replacement for **GIGABYTE AORUS Control Center**, targeting the
**AORUS 15G KC** laptop (Intel Core i7-10870H, NVIDIA RTX 3060 Mobile, Windows 11).

The official Control Center fails to launch on Windows 11 with two known crashes:

1. `Intel.Overclocking.SDK.Tuning.TuningLibrary.GetSpeedOptimizerState()` →
   `System.NullReferenceException`. The Intel Speed Optimizer 2.0 is only supported
   on unlocked K/X-series CPUs; the H-series 10870H returns `null` and the legacy
   Control Center does not null-check it.
2. `SmartManager.WlanClient..ctor` → `System.Windows.Markup.XamlParseException`.
   The bundled `ManagedWifi`/`SimpleWifi.WlanClient` P/Invoke wrapper throws inside
   the constructor on Windows 11 when `WlanSvc` is in certain states, which surfaces
   as a XAML init failure on window load.

This project drops both broken dependencies entirely and rebuilds the control
surface around the laptop's documented Embedded Controller (EC) registers and
the Fusion RGB HID protocol.

## Status

**Phase 0 — foundation (this PR):** solution scaffold, IPC contracts in
`Gbt.Common`, hardware-layer interfaces in `Gbt.Hardware` (whitelist,
exceptions, unverified ID registry), Windows service host with `JsonRpcServer`
over a named pipe and an `InMemoryGbtService` stub that serves the contract
without touching real hardware, `Gbt.Tools.DumpEc` skeleton CLI, unit tests,
CI on both Linux and Windows, and the documentation set under [`docs/`](docs/).

**Phase 1 — hardware backends (next task):**
- Live system dashboard (CPU/GPU/EC temperatures, fan RPMs, battery state).
- Performance Profiles — Quiet / Normal / Gaming / Boost / Custom, applied via
  `MSR_PKG_POWER_LIMIT` (0x610) and the EC bits documented in
  [`docs/EC-REGISTERS.md`](docs/EC-REGISTERS.md).
- Custom fan curve editor backed by the EC writable register set.
- Battery charge limit (60% / 80% / 100%).
- The diagnostic `Gbt.Tools.DumpEc` becomes a real read/dump tool.

Phase 2 — RGB:
- Fusion RGB keyboard control (USB `1044:7a3c`) — see [`docs/RGB-PROTOCOL.md`](docs/RGB-PROTOCOL.md).

Phase 3 — UX polish:
- Application launcher (Manager), macros, diagnostics, MSI installer.

## Architecture (one paragraph)

Two processes split by privilege:

- **`Gbt.Service`** — Windows Service running as `LocalSystem`. Owns all hardware
  access (WMI `\root\WMI`, the embedded controller via WinRing0, HID for the
  keyboard), evaluates Performance Profiles, and runs the fan-curve loop. Exposes
  a typed JSON RPC over a Windows named pipe.
- **`Gbt.ControlCenter`** — WPF desktop UI running as the logged-in user. No
  hardware access; all state changes go through the named pipe. System tray host.

Sensor reads (CPU/GPU package temperatures and frequencies) use
[LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).
EC reads/writes use a bundled signed copy of `WinRing0` for ring-0 port I/O.

Full design in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## What this project deliberately does NOT do

- It does **not** call any Intel Overclocking SDK API. The 10870H is locked; OC
  is a no-op on this hardware. This sidesteps crash #1 entirely.
- It does **not** use `ManagedWifi` / `SimpleWifi.WlanClient`. Wi-Fi state, when
  needed, comes from `System.Net.NetworkInformation` and `Windows.Devices.WiFi`
  (WinRT). This sidesteps crash #2.
- It does **not** ship any GIGABYTE binaries or assets. The EC and HID protocol
  documentation comes from public reverse-engineering work credited in
  [`docs/EC-REGISTERS.md`](docs/EC-REGISTERS.md) and [`docs/RGB-PROTOCOL.md`](docs/RGB-PROTOCOL.md).
- It does **not** download drivers from GIGABYTE servers ("Smart Update" feature).
- It does **not** integrate with Microsoft Azure AI "performance optimization".

## Hardware target

- AORUS 15G KC (BIOS family R09 / Clevo P95x chassis, 10th-gen Intel "Comet Lake-H").
- Other AORUS / AERO laptops are **not** in scope for this fork. Many of the EC
  registers are shared across the 2020 AORUS line (15G KB / 15G XC / 15G YC, 17G),
  but each model needs its own validation. See [`docs/EC-REGISTERS.md`](docs/EC-REGISTERS.md)
  for the validation procedure with `Gbt.Tools.DumpEc`.

## Build (developer)

Requires Windows 10/11 (x64) and .NET 8 SDK.

```powershell
dotnet build Gbt.sln -c Release
dotnet test  tests/Gbt.Hardware.Tests
```

The WPF app and the Windows service target `net8.0-windows`. Cross-platform
CI compiles `Gbt.Common` and `Gbt.Hardware.Tests` on Linux; the Windows-only
projects require a Windows runner. See [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

## Run (end user)

1. Install the MSI from the GitHub Releases page (Phase 1 milestone).
2. The installer registers and starts `Gbt.Service` as `LocalSystem` and drops a
   signed copy of `WinRing0x64.sys` next to it.
3. Log in normally; `Gbt.ControlCenter` is launched into the system tray.

Until the installer lands, run from sources:

```powershell
# As Administrator:
dotnet publish src/Gbt.Service -c Release -r win-x64 --self-contained
sc.exe create GbtService binPath="...\\Gbt.Service.exe" start=auto
sc.exe start GbtService

# As the logged-in user:
dotnet run --project src/Gbt.ControlCenter -c Release
```

## Safety

EC writes can permanently damage hardware if the wrong register is written.
`Gbt.Hardware` only writes to registers documented in [`docs/EC-REGISTERS.md`](docs/EC-REGISTERS.md)
with a per-register whitelist enforced at the library boundary. The diagnostic
tool `Gbt.Tools.DumpEc` is **read-only** by default; the `--write` flag is
gated behind an explicit `--i-understand-this-may-brick-my-laptop` token.

## License

MIT. See [`LICENSE`](LICENSE). Not affiliated with GIGA-BYTE Technology Co., Ltd.
