using Gbt.Common;

namespace Gbt.Hardware;

public interface IEcController
{
    byte Read(byte register);
    void Write(byte register, byte value);
}

public interface IMsrController
{
    ulong Read(uint msr, int cpu);
    void Write(uint msr, ulong value, int cpu);
}

public interface IWmiClient
{
    uint InvokeWmbc(uint methodId, uint arg);
    uint InvokeWmbd(uint methodId, uint arg);
    IReadOnlyList<string> EnumerateClasses();
}

public interface ISensorService
{
    SensorSnapshot Read();
}

public interface IPerformanceModeApplier
{
    void Apply(PerformanceProfile profile);
}

public interface IBatteryService
{
    int GetChargeLimit();
    void SetChargeLimit(int percent);
}

public interface IFanCurveEngine
{
    void Start();
    void Stop();
    void SetCurve(FanCurve curve);
}
