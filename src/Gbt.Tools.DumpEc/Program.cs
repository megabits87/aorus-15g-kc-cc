// AORUS 15G KC diagnostic dumper.
// READ-ONLY by default. EC writes are gated behind --i-understand-this-may-brick-my-laptop.
//
// Phase 0 scope: argument parsing and stub output. The real WinRing0-backed
// EC/MSR/WMI dumpers land in Phase 1 alongside Gbt.Hardware implementations.

using System.Reflection;
using Gbt.Hardware;

namespace Gbt.Tools.DumpEc;

internal static class Program
{
    private const string DangerToken = "--i-understand-this-may-brick-my-laptop";

    private static int Main(string[] args)
    {
        var parsed = ParseArgs(args);

        if (parsed.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (parsed.WriteRegister is not null && !parsed.DangerAcknowledged)
        {
            WriteRed("Refusing --write without the explicit consent token:");
            WriteRed($"  {DangerToken}");
            WriteRed("EC writes can permanently damage the laptop. Read the docs first.");
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            WriteRed("Gbt.Tools.DumpEc requires Windows. It talks to the EC via WinRing0.");
            return 3;
        }

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.0.0-dev";

        Console.WriteLine($"AORUS 15G KC dump-ec  v{version}");
        Console.WriteLine($"  output file : {parsed.OutputFile ?? "(stdout only)"}");
        Console.WriteLine($"  mode        : {(parsed.Watch ? $"watch ({parsed.WatchMs} ms)" : "single dump")}");
        Console.WriteLine();
        Console.WriteLine("Unverified hardware IDs that this build assumes (please confirm before relying):");
        foreach (var entry in UnverifiedHardwareIds.All)
        {
            Console.WriteLine($"  [{entry.Name,-32}] = {entry.Value,-12}  // {entry.Purpose}");
        }
        Console.WriteLine();
        Console.WriteLine("WMI / EC / MSR readers are not wired in this build (Phase 1).");
        Console.WriteLine("Once Gbt.Hardware ships its WinRing0-backed controllers, this tool will:");
        Console.WriteLine("  1. Read all 256 EC registers and print a hex/decoded table.");
        Console.WriteLine("  2. Read MSR 0x610 (MSR_PKG_POWER_LIMIT) and decode PL1/PL2.");
        Console.WriteLine("  3. Enumerate root\\WMI GBT* classes and dump method signatures.");
        Console.WriteLine("  4. Sample LibreHardwareMonitor sensors (CPU package, GPU, fans).");
        Console.WriteLine();
        Console.WriteLine("Until then, run --help to see the full planned CLI surface.");
        return 0;
    }

    private static ParsedArgs ParseArgs(string[] args)
    {
        var p = new ParsedArgs();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    p.ShowHelp = true;
                    break;
                case "--out":
                    p.OutputFile = SafeNext(args, ref i);
                    break;
                case "--watch":
                    p.Watch = true;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var ms))
                    {
                        p.WatchMs = ms;
                        i++;
                    }
                    break;
                case "--write":
                    p.WriteRegister = SafeNext(args, ref i);
                    p.WriteValue = SafeNext(args, ref i);
                    break;
                case DangerToken:
                    p.DangerAcknowledged = true;
                    break;
                default:
                    WriteRed($"Unknown argument: {args[i]}");
                    p.ShowHelp = true;
                    break;
            }
        }
        return p;
    }

    private static string? SafeNext(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            return null;
        }
        i++;
        return args[i];
    }

    private static void PrintHelp()
    {
        Console.WriteLine("dump-ec — AORUS 15G KC hardware probe (read-only by default)");
        Console.WriteLine();
        Console.WriteLine("USAGE");
        Console.WriteLine("  dump-ec [--out <file>] [--watch [ms]] [--write <reg> <value> --i-understand-...]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS");
        Console.WriteLine("  --out <file>          Tee the dump to a file as well as stdout.");
        Console.WriteLine("  --watch [ms]          Re-read sensor and fan registers every N ms (default 1000).");
        Console.WriteLine("  --write <reg> <val>   Write a hex value to an EC register. REQUIRES the consent");
        Console.WriteLine("                        token below. Only whitelisted registers are accepted.");
        Console.WriteLine($"  {DangerToken}");
        Console.WriteLine("                        Explicit acknowledgement required for any EC write.");
        Console.WriteLine("  -h, --help            Show this help.");
        Console.WriteLine();
        Console.WriteLine("Requires Administrator privileges. WinRing0 must be installed (see tools/fetch-winring0.ps1).");
    }

    private static void WriteRed(string msg)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(msg);
        Console.ForegroundColor = prev;
    }

    private sealed class ParsedArgs
    {
        public bool ShowHelp { get; set; }
        public string? OutputFile { get; set; }
        public bool Watch { get; set; }
        public int WatchMs { get; set; } = 1000;
        public string? WriteRegister { get; set; }
        public string? WriteValue { get; set; }
        public bool DangerAcknowledged { get; set; }
    }
}
