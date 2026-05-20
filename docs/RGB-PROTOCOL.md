# Fusion RGB protocol

> **Phase-2 placeholder.** The Fusion HID protocol is documented here once we
> start reverse-engineering it on the actual AORUS 15G KC keyboard.

## Hardware

| Field   | Value                                                       |
| ------- | ----------------------------------------------------------- |
| USB VID | `0x1044` (Chicony Electronics, GIGABYTE OEM)                |
| USB PID | `0x7A3C` (AORUS 15G family, per public reverse engineering) |
| Layout  | Per-key RGB, ~108 keys (US ISO TKL-ish), single zone variant on some SKUs |
| Brightness | Two firmware levels (`Fn+F4` up / `Fn+F3` down)          |

## Known references

- [blmhemu/opengigabyte](https://github.com/blmhemu/opengigabyte) documents the
  VID/PID mapping for the family and the keycode translator for AERO / AORUS
  function keys.
- The original AORUS Fusion editor speaks plain HID over the keyboard's
  interface 1 (LED endpoint). The frames are short (typically 64 bytes) and
  command-prefixed; the exact opcodes are not documented publicly.

## Phase-0 surface

`Gbt.Rgb` ships only the contract:

```csharp
public interface IRgbController
{
    bool IsAvailable { get; }
    void SetSolidColor(byte red, byte green, byte blue);
    void TurnOff();
}
```

and a `NullRgbController` that returns `IsAvailable = false`. `Gbt.Service`
takes a singleton `IRgbController`; the WPF UI binds the lighting tab against
this interface so the bindings stay valid even when no real backend is
wired.

## Phase-2 deliverables

1. `HidRgbController : IRgbController`, backed by [HidSharp](https://github.com/IntergatedCircuits/HidSharp).
2. `RGB-PROTOCOL.md` — table of HID report frames, decoded with a capture from
   the original Fusion app over USBPcap.
3. Built-in effect engine: solid, wave, breathing, reactive, ripple.
4. Per-key colour editor in the WPF UI.
5. Macro engine that maps `Fn+<key>` shortcuts to user actions.
