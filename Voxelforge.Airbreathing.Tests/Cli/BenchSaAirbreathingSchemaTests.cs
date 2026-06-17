// BenchSaAirbreathingSchemaTests.cs — subprocess schema-validation tests for
// the --bench-sa-airbreathing CLI subcommand.
//
// All tests are skip-marked: they require a Release build of
// Voxelforge.Benchmarks and are intended for manual/CI verification after
// a build rather than routine `dotnet test` runs.
//
// Coverage (4 tests):
//   1  J85 turbojet preset: exit 0, schema_version==1, bench_name correct
//   2  Mattingly ramjet preset: exit 0, provenance fields present, preset_kind correct
//   3  Unknown preset: non-zero exit code
//   4  Baseline files committed: baselines/ contains committed JSONL for both presets

using System.Diagnostics;
using System.Text.Json;

namespace Voxelforge.Airbreathing.Tests.Cli;

public sealed class BenchSaAirbreathingSchemaTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string LocateBenchmarksExe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "voxelforge.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException(
                "Could not find voxelforge.sln walking up from AppContext.BaseDirectory.");
        return Path.Combine(dir.FullName,
            "Voxelforge.Benchmarks", "bin", "Release",
            "net9.0-windows", "Voxelforge.Benchmarks.exe");
    }

    private static string LocateBaselinesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "voxelforge.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException(
                "Could not find voxelforge.sln walking up from AppContext.BaseDirectory.");
        return Path.Combine(dir.FullName, "Voxelforge.Benchmarks", "baselines");
    }

    private static (int exitCode, string stdout, string stderr) RunBenchmarks(
        string exe,
        string[] args,
        int timeoutMs = 120_000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(timeoutMs);
        return (p.ExitCode, stdout, stderr);
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact(Skip = "Subprocess/wall-clock; build Voxelforge.Benchmarks Release then run manually")]
    [Trait("Category", "Subprocess")]
    public void J85Turbojet_50Iters_EmitsValidSchemaV1Record()
    {
        string exe = LocateBenchmarksExe();
        Assert.True(File.Exists(exe), $"Benchmarks exe not found at {exe}. Run `dotnet build -c Release` first.");

        using var tmpDir = new TempDir();
        string outPath = Path.Combine(tmpDir.Path, "j85-schema-test.jsonl");

        var (exitCode, _, _) = RunBenchmarks(exe, new[]
        {
            "--bench-sa-airbreathing",
            "--preset",     "j85-turbojet",
            "--seed",       "42",
            "--iterations", "50",
            "--repeat",     "1",
            "--out",        outPath,
        });

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath), "JSONL output file was not created.");

        string line = File.ReadLines(outPath).First();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.Equal(1,                       root.GetProperty("schema_version").GetInt32());
        Assert.Equal("bench-sa-airbreathing", root.GetProperty("bench_name").GetString());
        Assert.Equal("j85-turbojet",          root.GetProperty("preset").GetString());
        Assert.Equal("turbojet",              root.GetProperty("preset_kind").GetString());
        Assert.Equal(42,                      root.GetProperty("seed").GetInt32());

        // Timing fields present
        Assert.True(root.TryGetProperty("per_iter_p50_us",   out _), "per_iter_p50_us missing");
        Assert.True(root.TryGetProperty("per_iter_p90_us",   out _), "per_iter_p90_us missing");
        Assert.True(root.TryGetProperty("per_iter_mean_us",  out _), "per_iter_mean_us missing");
        Assert.True(root.TryGetProperty("wall_total_ms",     out _), "wall_total_ms missing");
        Assert.True(root.TryGetProperty("iterations_completed", out _), "iterations_completed missing");

        // Provenance prefix
        Assert.True(root.TryGetProperty("machine_id",    out _), "machine_id missing");
        Assert.True(root.TryGetProperty("git_sha",       out _), "git_sha missing");
        Assert.True(root.TryGetProperty("build_config",  out _), "build_config missing");
        Assert.True(root.TryGetProperty("timestamp",     out _), "timestamp missing");
    }

    [Fact(Skip = "Subprocess/wall-clock; build Voxelforge.Benchmarks Release then run manually")]
    [Trait("Category", "Subprocess")]
    public void MattinglyRamjet_50Iters_EmitsValidSchemaV1Record()
    {
        string exe = LocateBenchmarksExe();
        Assert.True(File.Exists(exe), $"Benchmarks exe not found at {exe}. Run `dotnet build -c Release` first.");

        using var tmpDir = new TempDir();
        string outPath = Path.Combine(tmpDir.Path, "ramjet-schema-test.jsonl");

        var (exitCode, _, _) = RunBenchmarks(exe, new[]
        {
            "--bench-sa-airbreathing",
            "--preset",     "mattingly-ramjet",
            "--seed",       "42",
            "--iterations", "50",
            "--repeat",     "1",
            "--out",        outPath,
        });

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath), "JSONL output file was not created.");

        string line = File.ReadLines(outPath).First();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.Equal(1,                       root.GetProperty("schema_version").GetInt32());
        Assert.Equal("bench-sa-airbreathing", root.GetProperty("bench_name").GetString());
        Assert.Equal("mattingly-ramjet",      root.GetProperty("preset").GetString());
        Assert.Equal("ramjet",                root.GetProperty("preset_kind").GetString());

        // All 6 provenance prefix fields
        Assert.True(root.TryGetProperty("schema_version", out _), "schema_version missing");
        Assert.True(root.TryGetProperty("machine_id",     out _), "machine_id missing");
        Assert.True(root.TryGetProperty("git_sha",        out _), "git_sha missing");
        Assert.True(root.TryGetProperty("bench_name",     out _), "bench_name missing");
        Assert.True(root.TryGetProperty("build_config",   out _), "build_config missing");
        Assert.True(root.TryGetProperty("timestamp",      out _), "timestamp missing");

        // best_isp_s: null or positive numeric
        Assert.True(root.TryGetProperty("best_isp_s", out var ispProp), "best_isp_s missing");
        if (ispProp.ValueKind != JsonValueKind.Null)
            Assert.True(ispProp.GetDouble() > 0, "best_isp_s should be positive when feasible");
    }

    [Fact(Skip = "Subprocess/wall-clock; build Voxelforge.Benchmarks Release then run manually")]
    [Trait("Category", "Subprocess")]
    public void UnknownPreset_ReturnsNonZeroExitCode()
    {
        string exe = LocateBenchmarksExe();
        Assert.True(File.Exists(exe), $"Benchmarks exe not found at {exe}. Run `dotnet build -c Release` first.");

        var (exitCode, _, _) = RunBenchmarks(exe, new[]
        {
            "--bench-sa-airbreathing",
            "--preset", "does-not-exist",
        }, timeoutMs: 10_000);

        Assert.NotEqual(0, exitCode);
    }

    [Fact(Skip = "Subprocess/wall-clock; build Voxelforge.Benchmarks Release then run manually")]
    [Trait("Category", "Subprocess")]
    public void BothPresets_BaselineFilesCommitted()
    {
        string baselines = LocateBaselinesDir();
        Assert.True(Directory.Exists(baselines), $"baselines/ dir not found at {baselines}.");

        foreach (string presetKey in new[] { "j85-turbojet", "mattingly-ramjet" })
        {
            var files = Directory.GetFiles(baselines,
                $"bench-sa-airbreathing-{presetKey}-*.jsonl");
            Assert.True(files.Length > 0,
                $"No committed baseline found for preset '{presetKey}' under {baselines}. "
              + "Run --bench-sa-airbreathing and commit the output.");

            string latestFile = files.OrderByDescending(f => f).First();
            string[] lines = File.ReadAllLines(latestFile);
            Assert.True(lines.Length >= 1,
                $"Baseline file {latestFile} is empty — expected ≥1 JSONL record.");

            // Validate schema_version on the first record.
            using var doc = JsonDocument.Parse(lines[0]);
            int sv = doc.RootElement.GetProperty("schema_version").GetInt32();
            Assert.Equal(1, sv);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best-effort */ }
        }
    }
}
