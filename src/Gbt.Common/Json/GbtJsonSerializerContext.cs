using System.Text.Json.Serialization;

namespace Gbt.Common.Json;

[JsonSerializable(typeof(PerformanceMode))]
[JsonSerializable(typeof(FanCurvePoint))]
[JsonSerializable(typeof(FanCurve))]
[JsonSerializable(typeof(PerformanceProfile))]
[JsonSerializable(typeof(BatteryChargeLimit))]
[JsonSerializable(typeof(SensorSnapshot))]
[JsonSerializable(typeof(DiagnosticsReport))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
public sealed partial class GbtJsonSerializerContext : JsonSerializerContext
{
}
