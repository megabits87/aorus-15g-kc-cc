using System.Runtime.Versioning;
using Gbt.Common;
using Microsoft.Extensions.Logging;

namespace Gbt.Hardware;

/// <summary>
/// Battery charge-limit control via the GBT WMI provider. The limit is validated against
/// <see cref="BatteryChargeLimit"/> (50-100 %) and cached so reads remain stable even before the WMI
/// method mapping is verified. When the underlying <see cref="IWmiClient"/> reports the call is
/// unverified, the requested value is still cached and a warning is logged rather than throwing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiBatteryService : IBatteryService
{
    private readonly IWmiClient _wmi;
    private readonly ILogger<WmiBatteryService> _logger;
    private readonly object _gate = new();
    private int _cachedLimit = 100;

    public WmiBatteryService(IWmiClient wmi, ILogger<WmiBatteryService> logger)
    {
        _wmi = wmi;
        _logger = logger;
    }

    public int GetChargeLimit()
    {
        try
        {
            var raw = _wmi.InvokeWmbd(UnverifiedHardwareIds.WmbdGetBatteryChargeLimit, 0);
            var value = (int)(raw & 0xFF);
            if (value is >= 50 and <= 100)
            {
                lock (_gate)
                {
                    _cachedLimit = value;
                }
                return value;
            }
        }
        catch (NotSupportedException)
        {
            // mapping unverified — fall back to the cached value below
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read battery charge limit; returning cached value");
        }

        lock (_gate)
        {
            return _cachedLimit;
        }
    }

    public void SetChargeLimit(int percent)
    {
        // Reuse the model's validation so the 50-100 contract lives in exactly one place.
        var limit = new BatteryChargeLimit(percent);

        lock (_gate)
        {
            _cachedLimit = limit.Percent;
        }

        try
        {
            _wmi.InvokeWmbc(UnverifiedHardwareIds.WmbcSetBatteryChargeLimit, (uint)limit.Percent);
            _logger.LogInformation("Battery charge limit set to {Percent}%", limit.Percent);
        }
        catch (NotSupportedException)
        {
            _logger.LogWarning(
                "Battery charge limit cached at {Percent}% but not applied: GBT WMI mapping is unverified.",
                limit.Percent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply battery charge limit {Percent}%", limit.Percent);
        }
    }
}
