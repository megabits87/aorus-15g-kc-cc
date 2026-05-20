using System.Runtime.Versioning;
using Gbt.Common;
using Gbt.Rgb;
using Gbt.Service;
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

    // Singleton in-memory service. Phase 1 replaces this with a real hardware-backed implementation.
    builder.Services.AddSingleton<IGbtService, InMemoryGbtService>();
    builder.Services.AddSingleton<IRgbController, NullRgbController>();

    builder.Services.AddHostedService<JsonRpcServer>();
    builder.Services.AddHostedService<HeartbeatWorker>();

    var storageRoot = Environment.ExpandEnvironmentVariables(
        builder.Configuration.GetValue<string>($"{StorageOptions.SectionName}:Root")
        ?? "%ProgramData%\\GbtControlCenter");
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
