// Pitfall #8 — must subprocess. Do NOT inline-call BenchSA.Run from this test.
//
// First Process.Start-based test in the .Tests project. Pattern is
// the deliberate template for future xUnit-safe subprocess tests
// (e.g. anything that needs to instantiate `new PicoGK.Library` —
// see ADR-005). SA itself is pure-math (no Library), but the
// subprocess hop also pins cross-process determinism: a passing
// test catches a static-state contamination that an in-process
// call would mask.
//
// Two invocations of `--bench-sa --seed 42 --design-preset merlin
// --iterations 100 --repeat 1` must produce byte-identical BENCH
// numbers when rounded to 1 decimal place. Float-flavored fields
// (peak_wall_t_k etc.) have last-bit jitter across processes and
// 1 dp is the deterministic-to contract.
//
// Why 1-DP tolerance and not bit-identical (#623, 2026-05-17):
// the in-process determinism contract (ADR-042 + the VFD013/014/015
// analyzer trio) targets *same-process* SA reproducibility — same
// process, same seed, same vector → bit-identical Score. Across
// processes the OS scheduler, JIT-tier transitions, and FP-flag
// inheritance vary at the sub-1 % level on physics-aggregate
// scalars (peak wall T, mass flow, Isp). For a preliminary-design
// tool (LIMITATIONS.md §1: "10-20 % accuracy band") 1-DP cross-
// process is genuinely strict enough — anything tighter would
// catch noise, not regressions. The fingerprint format pins 1-DP
// per field deliberately.

using System.Text.RegularExpressions;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class BenchSADeterminismTests
{
    [Fact(Skip = "Subprocess / wall-clock heavy; run via dotnet test --filter \"FullyQualifiedName~BenchSADeterminism\" when verifying BB-2")]
    [Trait("Category", "Subprocess")]
    public void BenchSA_Merlin_Seed42_TwoSubprocessInvocations_ProduceIdenticalBench()
    {
        string exe = LocateBenchmarksExe();
        Assert.True(File.Exists(exe), $"Benchmarks exe not found at {exe} — run `dotnet build -c Release` first.");

        // Use a temp output path so we don't pollute committed baselines.
        using var out1 = TestTempFile.WithUniqueName("determinism-1", "jsonl");
        using var out2 = TestTempFile.WithUniqueName("determinism-2", "jsonl");

        var bench1 = RunBenchSA(exe, out1.Path, seed: 42, iterations: 100, repeat: 1);
        var bench2 = RunBenchSA(exe, out2.Path, seed: 42, iterations: 100, repeat: 1);

        Assert.True(bench1.Count > 0, "First subprocess produced no BENCH lines");
        Assert.True(bench2.Count > 0, "Second subprocess produced no BENCH lines");

        // Diff every key that's in BOTH runs. Skip wall_total_ms +
        // per_iter_*_us + per_iter_cv (timing-sensitive). Keep the
        // physics fingerprint scalars and SA-state counters — those
        // MUST be deterministic.
        var skip = new HashSet<string>(StringComparer.Ordinal)
        {
            "wall_total_ms",
            "per_iter_p50_us", "per_iter_p90_us", "per_iter_p99_us",
            "per_iter_mean_us", "per_iter_stdev_us", "per_iter_cv",
        };

        var keys = bench1.Keys.Intersect(bench2.Keys).Except(skip).ToList();
        Assert.True(keys.Count > 10, $"Too few comparable keys ({keys.Count}); something is structurally off.");

        var diffs = new List<string>();
        foreach (var k in keys)
        {
            string v1 = bench1[k];
            string v2 = bench2[k];
            if (!StringEqualsRoundedToOneDp(v1, v2))
                diffs.Add($"{k}: '{v1}' vs '{v2}'");
        }

        Assert.True(diffs.Count == 0,
            "BENCH determinism FAILED — fields differ between two same-seed subprocess runs:\n  "
          + string.Join("\n  ", diffs));
    }

    private static Dictionary<string, string> RunBenchSA(string exe, string outPath,
                                                         int seed, int iterations, int repeat)
    {
        var result = SubprocessRunner.Run(
            exe,
            args: new[]
            {
                "--bench-sa",
                "--design-preset", "merlin",
                "--seed",          seed.ToString(),
                "--iterations",    iterations.ToString(),
                "--repeat",        repeat.ToString(),
                "--out",           outPath,
            },
            timeoutMs: 120_000);

        // T12: prefer SubprocessResult.DescribeFailure so the test
        // message names the actual fault (exe-missing / timeout / non-zero
        // exit) instead of a generic "did not exit".
        Assert.True(result.Succeeded, result.DescribeFailure());

        var rx = new Regex(@"^BENCH\s+(\w+)=(.+)$", RegexOptions.Multiline);
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in rx.Matches(result.Stdout))
            d[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        return d;
    }

    // Two BENCH values are "deterministic to 1 dp" when, parsed as
    // doubles, round to the same value at one decimal place. Falls
    // back to plain string equality for non-numeric values
    // (preset names, booleans, integer iteration counts).
    private static bool StringEqualsRoundedToOneDp(string a, string b)
    {
        if (a == b) return true;
        if (double.TryParse(a, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var da) &&
            double.TryParse(b, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var db))
        {
            return Math.Round(da, 1) == Math.Round(db, 1);
        }
        return false;
    }

    private static string LocateBenchmarksExe() =>
        SubprocessRunner.LocateUnderRepo(
            "Voxelforge.Benchmarks/bin/Release/net9.0-windows/Voxelforge.Benchmarks.exe");
}
