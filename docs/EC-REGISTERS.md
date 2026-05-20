# Embedded Controller (EC) registers — AORUS 15G KC

> **Every value below is UNVERIFIED until confirmed on the actual machine with
> `Gbt.Tools.DumpEc`.** The numbers are pulled from public reverse-engineering
> of close-relative laptops (AORUS 15G family, AERO 15) and the original AORUS
> Control Center. They are a starting hypothesis, not a guarantee.

## How to read this table

| Column   | Meaning                                                                        |
| -------- | ------------------------------------------------------------------------------ |
| Address  | EC register, hex.                                                              |
| Name     | Logical name used in the source.                                                |
| R/W      | `R` = read-only in this codebase, `RW` = also writable (whitelisted).          |
| Purpose  | Short description of the field.                                                |
| Status   | `VALIDATED` once it has been confirmed on a real 15G KC, otherwise `UNVERIFIED`. |
| Source   | Where the hypothesis came from (community reverse engineering link).            |

## Read map (sensors)

| Address | Name             | R/W | Purpose                                | Status     | Source |
| ------- | ---------------- | --- | -------------------------------------- | ---------- | ------ |
| `0x60`  | `CpuTempC`       | R   | CPU temperature, °C                    | UNVERIFIED | [p37-ec-aero-15](https://github.com/tangalbert919/p37-ec-aero-15) |
| `0x61`  | `GpuTempC`       | R   | GPU temperature, °C                    | UNVERIFIED | [p37-ec-aero-15](https://github.com/tangalbert919/p37-ec-aero-15) |
| `0xFC`  | `CpuFanRpmLo`    | R   | CPU fan RPM, low byte (`/ 256`)        | UNVERIFIED | [gigabyte-laptop-wmi](https://github.com/tangalbert919/gigabyte-laptop-wmi) |
| `0xFD`  | `CpuFanRpmHi`    | R   | CPU fan RPM, high byte                  | UNVERIFIED | [gigabyte-laptop-wmi](https://github.com/tangalbert919/gigabyte-laptop-wmi) |
| `0xFE`  | `GpuFanRpmLo`    | R   | GPU fan RPM, low byte                  | UNVERIFIED | [gigabyte-laptop-wmi](https://github.com/tangalbert919/gigabyte-laptop-wmi) |
| `0xFF`  | `GpuFanRpmHi`    | R   | GPU fan RPM, high byte                  | UNVERIFIED | [gigabyte-laptop-wmi](https://github.com/tangalbert919/gigabyte-laptop-wmi) |
| `0xC2`  | `BatteryHealth`  | R   | Battery health, 0–100                  | UNVERIFIED | reverse engineering of original CC |

## Write map (whitelisted)

These are the **only** registers `Gbt.Hardware` is allowed to write. Anything
else throws `EcAccessViolationException`. See
[`RegisterWhitelist.cs`](../src/Gbt.Hardware/RegisterWhitelist.cs).

| Address | Name                       | R/W | Purpose                              | Status     | Source |
| ------- | -------------------------- | --- | ------------------------------------ | ---------- | ------ |
| `0xB0`  | `CpuFanDutyRegister`       | RW  | CPU fan duty, 0–229 (`100% ≈ 229`)   | UNVERIFIED | [p37-ec-aero-15](https://github.com/tangalbert919/p37-ec-aero-15) |
| `0xB1`  | `GpuFanDutyRegister`       | RW  | GPU fan duty, same scale             | UNVERIFIED | [p37-ec-aero-15](https://github.com/tangalbert919/p37-ec-aero-15) |
| `0xB2`  | `FanControlModeRegister`   | RW  | Auto vs custom fan loop master switch | UNVERIFIED | [p37-ec-aero-15](https://github.com/tangalbert919/p37-ec-aero-15) |

## MSRs (CPU only, not EC)

These are read/written via WinRing0 `Rdmsr`/`Wrmsr` and are not part of the
EC whitelist.

| MSR     | Name                  | Bits           | Purpose                              |
| ------- | --------------------- | -------------- | ------------------------------------ |
| `0x610` | `MSR_PKG_POWER_LIMIT` | `0..14`        | PL1 power limit, units of MSR_PKG_POWER_UNIT (`0x606`) |
|         |                       | `15`           | PL1 enable                            |
|         |                       | `16..23`       | PL1 time window (encoded)             |
|         |                       | `32..46`       | PL2 power limit                       |
|         |                       | `47`           | PL2 enable                            |
|         |                       | `48..55`       | PL2 time window                       |
|         |                       | `63`           | Lock bit. If set, writes are rejected. |

`Gbt.Hardware.WinRing0MsrController` (Phase 1) refuses to write `0x610` when
bit 63 is set and throws `MsrLockedException`.

## WMI methods (root\WMI)

The original CC interacts with the EC through WMI methods `WMBC` (write) and
`WMBD` (read) on a vendor-specific class. The exact class name varies per BIOS
revision (`GBT_WMI`, `GBTSetting`, …). Use `Gbt.Tools.DumpEc` once Phase 1 is
in to enumerate them.

| Method ID  | Name (assumed)                | Purpose                              | Status     |
| ---------- | ----------------------------- | ------------------------------------ | ---------- |
| `0x000001` | `WmbcSetPerformanceMode`      | Set GBT-side performance flag        | UNVERIFIED |
| `0x000002` | `WmbcSetBatteryChargeLimit`   | Apply 60 / 80 / 100% charge cap      | UNVERIFIED |
| `0x000003` | `WmbdGetBatteryChargeLimit`   | Read current cap                      | UNVERIFIED |

See [`UnverifiedHardwareIds.cs`](../src/Gbt.Hardware/UnverifiedHardwareIds.cs)
for the same list in code form. The service logs a `[UNVERIFIED]` warning at
startup for every ID still flagged.

## Validation procedure

When you can finally run `Gbt.Tools.DumpEc` on the actual laptop:

1. Boot Windows 11 with **the original AORUS Control Center uninstalled** (so
   it does not race for the EC).
2. Run `Gbt.Tools.DumpEc dump --out before.txt`. This captures every EC byte.
3. Reinstall the original CC, change one setting (e.g. fan mode → Gaming).
4. Stop the original CC, run `dump --out after.txt`.
5. `diff before.txt after.txt` tells you exactly which registers move.
6. Update the relevant row above from `UNVERIFIED` to `VALIDATED` and flip the
   corresponding constant in `RegisterWhitelist.cs` or `UnverifiedHardwareIds.cs`.
7. Open a PR with the `before`/`after` dumps attached.

## Safety

- The fan control whitelist is intentionally narrow. Even if `Gbt.Hardware`
  invokes `IEcController.Write` with a wild address, the operation throws
  before the byte hits the EC.
- The diagnostic dumper is **read-only by default**. The `--write` flag is
  gated behind the consent token `--i-understand-this-may-brick-my-laptop`,
  and even with that token only whitelisted registers are accepted.
- Phase 1 will add an EC kill-switch on service shutdown that restores
  register `0xB2` to its "auto" value, so the fans are never left pinned off.
