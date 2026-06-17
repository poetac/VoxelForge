// BB-3 (2026-04-29): BenchmarkDotNet runner entry point.
//
// Routes `dotnet run -c Release` (or the assembly directly) to BDN's
// switcher so any [Benchmark] in this project is invokable by class
// name or `--filter '*'` for the full suite. Pass `--list flat` to
// enumerate without running.
//
// The switcher attaches BdnJsonlExporter so each run emits a
// schema-v1-compliant JSONL row alongside BDN's default Markdown +
// HTML reports — keeps `baselines/micro-*.jsonl` regenerable through
// the same surface the Sprint BB CLI uses.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Voxelforge.MicroBenchmarks;

public static class Program
{
    public static int Main(string[] args)
    {
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(new BdnJsonlExporter())
            .AddLogger(ConsoleLogger.Default)
            .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(40));

        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args, config);
        return 0;
    }
}
