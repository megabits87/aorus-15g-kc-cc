using System.Runtime.Versioning;
using Gbt.Common;
using Gbt.Hardware;
using Gbt.Rgb;
using Gbt.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Gbt.Service is a Windows service host and only runs on Windows.");
    return 1;
}

return RunWindows(args);

[SupportedOSPlatform("windows")]
static int RunWindows(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddOptions<IpcOptions>().Bind(builder.Configuration.GetSection(IpcOptions.SectionName));
    builder.Services.AddOptions<StorageOptions>().Bind(builder.Configuration.GetSection(StorageOptions.SectionName));

    // Dev/CI escape hatch: "Hardware:UseInMemory": true serves synthetic data with no ring-0 access.
    var useInMemory = builder.Configuration.GetValue<bool>("Hardware:UseInMemory");
    if (useInMemory)
    {
        builder.Services.AddSingleton<IGbtService, InMemoryGbtService>();
    }
    else
    {
        // Ring-0 driver shared by the EC and MSR controllers (disposed by the host on shutdown).
        builder.Services.AddSingleton<WinRing0Driver>();
        builder.Services.AddSingleton<IEcController, WinRing0EcController>();
        builder.Services.AddSingleton<IMsrController, WinRing0MsrController>();
        builder.Services.AddSingleton<ISensorService, LhmSensorService>();
        builder.Services.AddSingleton<IWmiClient, GbtWmiClient>();
        builder.Services.AddSingleton<IPerformanceModeApplier, MsrPerformanceModeApplier>();
        builder.Services.AddSingleton<IBatteryService, WmiBatteryService>();
        builder.Services.AddSingleton<IFanCurveEngine, FanCurveEngine>();
        builder.Services.AddSingleton<PerformanceProfilePersister>();
        builder.Services.AddSingleton<HardwareGbtService>();
        builder.Services.AddSingleton<IGbtService>(sp => sp.GetRequiredService<HardwareGbtService>());

        builder.Services.AddHostedService<HardwareBootstrapWorker>();
        builder.Services.AddHostedService<HotkeyWorker>();
    }

    builder.Services.AddSingleton<IRgbController, NullRgbController>();

    builder.Services.AddHostedService<JsonRpcServer>();
    builder.Services.AddHostedService<HeartbeatWorker>();

    // Bind StorageOptions once (it is also registered via AddOptions above) so the log path and the
    // service share a single source of truth for the storage root instead of duplicating the default.
    var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
        ?? new StorageOptions();
    var storageRoot = Environment.ExpandEnvironmentVariables(storageOptions.Root);
    var logDir = Path.Combine(storageRoot, "logs");
    Directory.CreateDirectory(logDir);

    builder.Services.AddSerilog((sp, loggerConfig) => loggerConfig
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .Enrich.WithProperty("Service", "GbtControlCenter")
        .WriteTo.File(
            Path.Combine(logDir, "service-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
        .WriteTo.EventLog(
            source: "GbtControlCenter",
            manageEventSource: false,
            restrictedToMinimumLevel: LogEventLevel.Warning));

    builder.Services.AddWindowsService(o =>
    {
        o.ServiceName = "GbtControlCenter";
    });

    using var host = builder.Build();
    host.Run();
    return 0;
}
