using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Gbt.Hardware;

/// <summary>
/// Raw P/Invoke surface for the WinRing0 ring-0 driver (<c>WinRing0x64.dll</c>), the same signed
/// kernel shim that LibreHardwareMonitor, ThrottleStop and HWiNFO use for port and MSR access.
/// <para>
/// Nothing here is safe to call directly from business logic — go through <see cref="WinRing0Driver"/>,
/// <c>WinRing0EcController</c> and <c>WinRing0MsrController</c>, which add lifetime management, the
/// EC handshake and the register whitelist. The DLL must sit next to the executable; see
/// <c>tools/fetch-winring0.ps1</c>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WinRing0Native
{
    private const string Dll = "WinRing0x64.dll";

    /// <returns>Non-zero on success.</returns>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int InitializeOls();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DeinitializeOls();

    /// <summary>0 == OLS_DLL_NO_ERROR. Anything else indicates a driver load / signature problem.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint GetDllStatus();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte ReadIoPortByte(ushort port);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void WriteIoPortByte(ushort port, byte value);

    /// <returns>Non-zero on success. <paramref name="eax"/> = low 32 bits, <paramref name="edx"/> = high 32 bits.</returns>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Rdmsr(uint index, ref uint eax, ref uint edx);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Wrmsr(uint index, uint eax, uint edx);

    /// <summary>Read an MSR pinned to a specific logical processor (affinity mask).</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int RdmsrTx(uint index, ref uint eax, ref uint edx, UIntPtr threadAffinityMask);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int WrmsrTx(uint index, uint eax, uint edx, UIntPtr threadAffinityMask);
}
