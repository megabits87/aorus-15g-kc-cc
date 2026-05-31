using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Gbt.Hardware;

/// <summary>
/// Talks to the ACPI Embedded Controller through the legacy 0x62/0x66 I/O port pair using the
/// standard RD_EC / WR_EC handshake. Reads are unrestricted; writes are gated by
/// <see cref="RegisterWhitelist"/> so only the fan registers can ever be touched.
/// <para>
/// The 0x62/0x66 ports and the fan registers are UNVERIFIED on the AORUS 15G KC — confirm with
/// <c>Gbt.Tools.DumpEc</c> before trusting fan control on real hardware.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinRing0EcController : IEcController
{
    // Standard ACPI EC interface.
    private const ushort DataPort = 0x62;
    private const ushort CommandPort = 0x66;
    private const byte ReadCommand = 0x80;   // RD_EC
    private const byte WriteCommand = 0x81;  // WR_EC
    private const byte StatusOutputBufferFull = 0x01; // OBF
    private const byte StatusInputBufferFull = 0x02;  // IBF

    private const int SpinLimit = 10_000;

    private readonly WinRing0Driver _driver;
    private readonly ILogger<WinRing0EcController> _logger;

    public WinRing0EcController(WinRing0Driver driver, ILogger<WinRing0EcController> logger)
    {
        _driver = driver;
        _logger = logger;
    }

    public byte Read(byte register)
    {
        lock (_driver.Gate)
        {
            _driver.EnsureInitialized();
            WaitForInputBufferEmpty();
            _driver.WriteIoPortByte(CommandPort, ReadCommand);
            WaitForInputBufferEmpty();
            _driver.WriteIoPortByte(DataPort, register);
            WaitForOutputBufferFull();
            return _driver.ReadIoPortByte(DataPort);
        }
    }

    public void Write(byte register, byte value)
    {
        // Hard safety boundary: refuse anything outside the fan whitelist before we touch the EC.
        RegisterWhitelist.AssertCanWrite(register);

        lock (_driver.Gate)
        {
            _driver.EnsureInitialized();
            WaitForInputBufferEmpty();
            _driver.WriteIoPortByte(CommandPort, WriteCommand);
            WaitForInputBufferEmpty();
            _driver.WriteIoPortByte(DataPort, register);
            WaitForInputBufferEmpty();
            _driver.WriteIoPortByte(DataPort, value);
        }

        _logger.LogDebug("EC[0x{Reg:X2}] <= 0x{Val:X2}", register, value);
    }

    private void WaitForInputBufferEmpty()
    {
        for (var i = 0; i < SpinLimit; i++)
        {
            if ((_driver.ReadIoPortByte(CommandPort) & StatusInputBufferFull) == 0)
            {
                return;
            }
        }
        throw new TimeoutException("EC input buffer never drained (IBF stuck high).");
    }

    private void WaitForOutputBufferFull()
    {
        for (var i = 0; i < SpinLimit; i++)
        {
            if ((_driver.ReadIoPortByte(CommandPort) & StatusOutputBufferFull) != 0)
            {
                return;
            }
        }
        throw new TimeoutException("EC output buffer never filled (OBF stuck low).");
    }
}
