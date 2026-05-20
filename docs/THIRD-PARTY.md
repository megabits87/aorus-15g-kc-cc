# Third-party components

The project links several open-source components at build time, and bundles a
small number of native binaries at install time. Every entry here ships under
a license compatible with the project's MIT license, and any redistributed
binary is checksummed.

## NuGet packages (managed dependencies)

| Package                                      | License        | Notes |
| -------------------------------------------- | -------------- | ----- |
| `Microsoft.Extensions.*` (Hosting, Logging, Configuration, DI) | MIT | .NET 8 BCL extensions. |
| `StreamJsonRpc`                              | MIT            | JSON-RPC 2.0 over a `Stream` (the named pipe). |
| `System.IO.Pipes.AccessControl`              | MIT            | `PipeSecurity` for the named-pipe ACL. |
| `Serilog`, `Serilog.Sinks.File`, `Serilog.Sinks.EventLog`, `Serilog.Extensions.Hosting` | Apache-2.0 | Service logging. |
| `LibreHardwareMonitorLib`                    | MPL-2.0        | CPU package / GPU temperatures, per-core frequencies. The Phase 1 sensor backend. |
| `System.Management`                          | MIT            | `\\root\\WMI` access. |
| `HidSharp` (Phase 2)                         | Apache-2.0     | USB HID I/O for the Fusion keyboard. |
| `CommunityToolkit.Mvvm` (UI task)            | MIT            | MVVM source-generator helpers for WPF. |
| `ModernWpfUI` (UI task)                      | MIT            | WinUI-styled controls for WPF. |
| `LiveChartsCore.SkiaSharpView.WPF` (UI task) | MIT            | Charts for the dashboard. |
| `H.NotifyIcon.Wpf` (UI task)                 | MIT            | Tray icon. |
| Test packages (xunit, FluentAssertions, Moq, coverlet) | MIT | CI-only. |

All NuGet versions are pinned centrally in [`Directory.Packages.props`](../Directory.Packages.props).

## Native binaries (NOT in this repo)

These binaries are required at runtime by `Gbt.Service` and `Gbt.Tools.DumpEc`,
but are **not** committed. They are fetched at install time by
[`tools/fetch-winring0.ps1`](../tools/fetch-winring0.ps1) and copied into
`src/Gbt.Hardware/runtimes/win-x64/native/` for development, or next to the
service binary at install time.

### WinRing0 v1.3.x

| Property | Value |
| -------- | ----- |
| Files    | `WinRing0x64.sys`, `WinRing0x64.dll` |
| License  | BSD-3-Clause (original) / GPL-2.0 (signed fork). See the upstream license file. |
| Purpose  | Kernel-mode I/O port + MSR access driver. Required for EC port reads/writes (0x62 / 0x66) and `Rdmsr`/`Wrmsr` of `MSR_PKG_POWER_LIMIT`. |
| Source   | https://github.com/GermanAizek/WinRing0 (signed fork) |
| SHA256   | **TODO Phase 1** — pin after the script downloads the released build the first time. |

> **Why this is not committed.** The signed driver is a binary we did not
> produce. Pinning it via a SHA256-verified download script keeps redistribution
> clean and ensures end users can audit what was installed.

### LibreHardwareMonitor `inpoutx64.sys` and `WinRing0`

`LibreHardwareMonitorLib` itself does not bundle a kernel driver in the NuGet
package; it expects the host process to have ring-0 access available. Our
service satisfies this requirement with the same `WinRing0x64.sys`.

## Attribution

Public reverse engineering that informed the EC register and WMI method maps:

- [tangalbert919/gigabyte-laptop-wmi](https://github.com/tangalbert919/gigabyte-laptop-wmi) — Linux WMI/EC driver for AERO / AORUS laptops.
- [tangalbert919/p37-ec-aero-15](https://github.com/tangalbert919/p37-ec-aero-15) — raw EC register dumps and fan control.
- [blmhemu/opengigabyte](https://github.com/blmhemu/opengigabyte) — Linux driver for Gigabyte keyboards.
- [t-8ch/linux-gigabyte-wmi-driver](https://github.com/t-8ch/linux-gigabyte-wmi-driver) — desktop board WMI driver, historical reference.

No GIGABYTE-copyrighted binaries or text are vendored.

## License compatibility

The project is MIT (`LICENSE` at the repository root). The MPL-2.0 and Apache-2.0
NuGet dependencies are dynamically linked and stay under their own license —
this is fine for an MIT distribution. The GPL-2.0 of the signed WinRing0 fork
affects only the WinRing0 binary itself; we do not statically link or modify
it.
