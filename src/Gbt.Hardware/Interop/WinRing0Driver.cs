using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Gbt.Hardware;

/// <summary>
/// Owns the lifetime of the WinRing0 driver for the whole process. Construct once (it is registered
/// as a DI singleton), share the instance with the EC and MSR controllers, and dispose on shutdown.
/// All port/MSR access funnels through the <see cref="Gate"/> lock so an EC read handshake can never
/// be interleaved with an MSR write from another thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinRing0Driver : IDisposable
{
    private readonly ILogger<WinRing0Driver> _logger;
    private bool _initialized;
    private bool _disposed;

    /// <summary>Serialises every ring-0 access. EC reads/writes are multi-step and must be atomic.</summary>
    internal readonly object Gate = new();

    public WinRing0Driver(ILogger<WinRing0Driver> logger)
    {
        _logger = logger;
    }

    public bool IsInitialized => _initialized;

    /// <summary>
    /// Loads and initialises the driver. Idempotent. Throws <see cref="WinRing0NotInstalledException"/>
    /// if the DLL is missing or the kernel driver could not be started (the usual cause is running
    /// without Administrator rights, or the .sys not being signed/allowed on this machine).
    /// </summary>
    public void EnsureInitialized()
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized)
            {
                return;
            }

            int ok;
            uint status;
            try
            {
                ok = WinRing0Native.InitializeOls();
                status = WinRing0Native.GetDllStatus();
            }
            catch (DllNotFoundException)
            {
                throw new WinRing0NotInstalledException(
                    "WinRing0x64.dll was not found next to the service executable. " +
                    "Run tools/fetch-winring0.ps1 to download the signed driver.");
            }
            catch (BadImageFormatException)
            {
                throw new WinRing0NotInstalledException(
                    "WinRing0x64.dll could not be loaded (architecture mismatch — a 64-bit build is required).");
            }

            if (ok == 0 || status != 0)
            {
                throw new WinRing0NotInstalledException(
                    $"WinRing0 failed to initialise (InitializeOls={ok}, GetDllStatus=0x{status:X}). " +
                    "Administrator rights are required and the driver must be allowed to load.");
            }

            _initialized = true;
            _logger.LogInformation("WinRing0 ring-0 driver initialised");
        }
    }

    internal byte ReadIoPortByte(ushort port)
    {
        EnsureInitialized();
        return WinRing0Native.ReadIoPortByte(port);
    }

    internal void WriteIoPortByte(ushort port, byte value)
    {
        EnsureInitialized();
        WinRing0Native.WriteIoPortByte(port, value);
    }

    /// <summary>Reads a 64-bit MSR. Returns the value as (high &lt;&lt; 32 | low).</summary>
    internal ulong ReadMsr(uint index)
    {
        EnsureInitialized();
        uint eax = 0, edx = 0;
        if (WinRing0Native.Rdmsr(index, ref eax, ref edx) == 0)
        {
            throw new InvalidOperationException($"RDMSR 0x{index:X} failed.");
        }
        return ((ulong)edx << 32) | eax;
    }

    internal void WriteMsr(uint index, ulong value)
    {
        EnsureInitialized();
        var eax = (uint)(value & 0xFFFFFFFF);
        var edx = (uint)(value >> 32);
        if (WinRing0Native.Wrmsr(index, eax, edx) == 0)
        {
            throw new InvalidOperationException($"WRMSR 0x{index:X} failed.");
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed)
            {
                return;
            }
            if (_initialized)
            {
                try
                {
                    WinRing0Native.DeinitializeOls();
                }
                catch
                {
                    // best-effort teardown on shutdown
                }
                _initialized = false;
            }
            _disposed = true;
        }
    }
}
