// BB-3 (2026-04-29): schema-v1 JSONL exporter for BenchmarkDotNet.
//
// BDN already produces Markdown + HTML + CSV reports; ADR-013 mandates
// every committed `baselines/*.jsonl` follow the schema-v1 6-field
// provenance prefix. This exporter bridges the two: every benchmark
// run flushes one JSONL line per [Benchmark] case to
// `BenchmarkDotNet.Artifacts/results/<Bench>-bdn.jsonl`, preserving
// the same provenance shape as the JSONL records in
// Voxelforge.Benchmarks/.
//
// The provenance fields (schema_version, machine_id, git_sha,
// bench_name, build_config, timestamp) are duplicated locally rather
// than imported from Voxelforge.Benchmarks because that
// project is its own assembly — taking a dependency on it from here
// would make the dependency graph circular through the App reference.

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;

namespace Voxelforge.MicroBenchmarks;

internal sealed class BdnJsonlExporter : IExporter
{
    public string Name => "schema-v1-jsonl";

    public void ExportToLog(Summary summary, ILogger logger) { /* no console output */ }

    public IEnumerable<string> ExportToFiles(Summary summary, ILogger consoleLogger)
    {
        string outDir = Path.Combine(summary.ResultsDirectoryPath);
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, $"{summary.Title}-bdn.jsonl");

        var ci = CultureInfo.InvariantCulture;
        string machineId = MachineFingerprint.ComputeId();
        string gitSha = MachineFingerprint.GitSha();
        string buildConfig =
#if DEBUG
            "Debug"
#else
            "Release"
#endif
            ;
        string timestamp = DateTime.UtcNow.ToString("O", ci);

        using var w = new StreamWriter(outPath, append: false);
        foreach (var report in summary.Reports)
        {
            var stats = report.ResultStatistics;
            if (stats == null) continue;

            string benchName = $"micro-{report.BenchmarkCase.Descriptor.Type.Name}-{report.BenchmarkCase.Descriptor.WorkloadMethod.Name}";
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"schema_version\":1,");
            sb.Append("\"machine_id\":\"").Append(machineId).Append("\",");
            sb.Append("\"git_sha\":\"").Append(gitSha).Append("\",");
            sb.Append("\"bench_name\":\"").Append(benchName).Append("\",");
            sb.Append("\"build_config\":\"").Append(buildConfig).Append("\",");
            sb.Append("\"timestamp\":\"").Append(timestamp).Append("\",");
            sb.Append("\"mean_ns\":").Append(stats.Mean.ToString("G6", ci)).Append(',');
            sb.Append("\"median_ns\":").Append(stats.Median.ToString("G6", ci)).Append(',');
            sb.Append("\"stddev_ns\":").Append(stats.StandardDeviation.ToString("G6", ci)).Append(',');
            sb.Append("\"min_ns\":").Append(stats.Min.ToString("G6", ci)).Append(',');
            sb.Append("\"max_ns\":").Append(stats.Max.ToString("G6", ci)).Append(',');
            sb.Append("\"n\":").Append(stats.N.ToString(ci));

            var allocBytes = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase);
            if (allocBytes.HasValue)
            {
                sb.Append(",\"alloc_bytes\":").Append(allocBytes.Value.ToString(ci));
            }

            sb.Append('}');
            w.WriteLine(sb.ToString());
        }
        return new[] { outPath };
    }
}

// Self-contained machine + git provenance for the JSONL prefix.
// Mirrors Voxelforge.Benchmarks/MachineInfo.cs intentionally
// — duplicating ~50 lines avoids cross-assembly coupling and keeps
// the BDN exporter dependency-free.
internal static class MachineFingerprint
{
    private static string? _idCached;
    public static string ComputeId()
    {
        if (_idCached != null) return _idCached;
        string cpu = ReadCpuModel();
        int cores = Environment.ProcessorCount;
        string os = RuntimeInformation.OSDescription;
        string dotnet = RuntimeInformation.FrameworkDescription;
        string buildConfig =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        string tuple = string.Join("|", cpu, cores, os, dotnet, buildConfig);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(tuple));
        var sb = new StringBuilder(16);
        for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
        _idCached = sb.ToString();
        return _idCached;
    }

    // Audit 01-security L6/L7: `FileName = "git"` resolves via the OS
    // PATH. See Voxelforge.Benchmarks.MachineInfo.GitSha() for the
    // shared threat-model rationale (the bare-name lookup does not
    // materially widen voxelforge's workstation attack surface).
    private static string? _gitShaCached;
    public static string GitSha()
    {
        if (_gitShaCached != null) return _gitShaCached;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) { _gitShaCached = "unknown"; return _gitShaCached; }
            string sha = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            _gitShaCached = (p.ExitCode == 0 && sha.Length == 40) ? sha : "unknown";
        }
        catch
        {
            _gitShaCached = "unknown";
        }
        return _gitShaCached;
    }

    private static string ReadCpuModel()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "unknown";
        }
        catch { return "unknown"; }
    }
}
