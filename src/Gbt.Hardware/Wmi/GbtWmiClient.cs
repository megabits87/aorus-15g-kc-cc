using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Gbt.Hardware;

/// <summary>
/// Thin wrapper over the GIGABYTE ACPI-WMI provider in <c>root\WMI</c>. Class enumeration is fully
/// implemented and used by <c>Gbt.Tools.DumpEc</c> to discover the provider on real hardware.
/// <para>
/// The <see cref="InvokeWmbc"/>/<see cref="InvokeWmbd"/> method IDs are UNVERIFIED (see
/// <see cref="UnverifiedHardwareIds"/>). Until the exact GBT class and method names are confirmed on
/// the device, invocation throws <see cref="NotSupportedException"/> rather than guessing and risking
/// an unintended ACPI call.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GbtWmiClient : IWmiClient
{
    private const string Namespace = @"\\.\root\WMI";

    private readonly ILogger<GbtWmiClient> _logger;

    public GbtWmiClient(ILogger<GbtWmiClient> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> EnumerateClasses()
    {
        var found = new List<string>();
        try
        {
            var scope = new ManagementScope(Namespace);
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM meta_class"));
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                using (obj)
                {
                    var name = obj.ClassPath?.ClassName;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    if (name.StartsWith("GB", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("WMIACPI", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate root\\WMI classes");
        }

        return found;
    }

    public uint InvokeWmbc(uint methodId, uint arg) => ThrowUnverified(nameof(InvokeWmbc), methodId);

    public uint InvokeWmbd(uint methodId, uint arg) => ThrowUnverified(nameof(InvokeWmbd), methodId);

    private static uint ThrowUnverified(string method, uint methodId) =>
        throw new NotSupportedException(
            $"{method}(0x{methodId:X8}) is not wired yet: the GBT WMI class/method mapping is unverified. " +
            "Run Gbt.Tools.DumpEc on the AORUS 15G KC to confirm the provider, then implement the call.");
}
