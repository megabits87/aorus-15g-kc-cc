namespace Gbt.Rgb;

/// <summary>
/// Per-zone or per-key RGB controller for the AORUS Fusion keyboard.
/// <para>
/// Phase 2 will implement <see cref="HidRgbController"/> backed by the USB HID
/// device 1044:7A3C. This Phase 0 file defines only the contract so that
/// <c>Gbt.Service</c> can take a dependency on <see cref="IRgbController"/>
/// without forcing the RGB stack to ship yet.
/// </para>
/// </summary>
public interface IRgbController
{
    /// <summary>True only when a real backing controller is present and the user is logged in.</summary>
    bool IsAvailable { get; }

    /// <summary>Set every key to a single RGB colour.</summary>
    void SetSolidColor(byte red, byte green, byte blue);

    /// <summary>Stop emitting effects and turn all backlight off.</summary>
    void TurnOff();
}

/// <summary>
/// Safe no-op <see cref="IRgbController"/>. Returned when the Fusion keyboard
/// is not present, when the HID device is in use by another process, or in
/// builds that have not yet wired the Phase-2 HID stack.
/// </summary>
public sealed class NullRgbController : IRgbController
{
    public bool IsAvailable => false;

    public void SetSolidColor(byte red, byte green, byte blue)
    {
        // no-op
    }

    public void TurnOff()
    {
        // no-op
    }
}
