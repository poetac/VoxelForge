// T2.2 (2026-04-25): smoke + regression cover for voxelforge-eval.
// Spawns the CLI as a subprocess (matches BenchSADeterminismTests
// pattern), pipes a designer-constructed JSON request, parses the
// stdout score JSON, and asserts the round-trip is sensible.
//
// T12 (2026-04-28): exe discovery + spawn + result inspection now
// run through SubprocessRunner / SubprocessResult. The "exe not yet
// built" code path is preserved (single-project test invocations
// don't build the Eval CLI).

using System.IO;
using System.Text;
using System.Text.Json;
using Voxelforge.Combustion;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class VoxelforgeEvalSubprocessTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // Match voxelforge-eval's serializer config (AllowNamedFloatingPointLiterals)
        // so the parser-side accepts "Infinity"/"NaN" if they appear.
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    // The Eval project's OutDir override drops the exe into the main
    // app's bin dir (matches StlExporter pattern). Both Debug + Release
    // configurations land under the same parent.
    private static string LocateExe()
    {
        string config =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        return SubprocessRunner.LocateUnderRepo(
            $"Voxelforge/bin/{config}/net9.0-windows/voxelforge-eval.exe");
    }

    private static string SeedJson()
    {
        var seed = AutoSeeder.Seed(new EngineSpec(
            PropellantPair: PropellantPair.LOX_CH4,
            Thrust_N: 5_000.0,
            ChamberPressure_Pa: 6e6,
            ExpansionRatio: 12.0));
        var doc = new
        {
            id = "smoke-1",
            Conditions = seed.Conditions,
            Design = seed.Design,
        };
        return JsonSerializer.Serialize(doc, JsonOpts);
    }

    [Fact]
    public void OneShot_RoundTripsDesign_AndProducesScore()
    {
        string exe = LocateExe();
        var probe = SubprocessRunner.ProbeExe(exe);
        if (!probe.ExeExists)
        {
            // Test discovery may run before voxelforge-eval has been
            // built (single-project test invocations). Skip rather than
            // fail in that case — a full sln build is required for this
            // test to be meaningful.
            return;
        }

        string requestJson = SeedJson();
        var result = SubprocessRunner.Run(exe, args: null, stdin: requestJson, timeoutMs: 30_000);

        Assert.True(result.Succeeded, result.DescribeFailure());
        Assert.False(string.IsNullOrWhiteSpace(result.Stdout),
            $"voxelforge-eval produced no stdout.\nstderr:\n{result.Stderr}");

        // Output is a single JSON object with optional id + score body.
        using var doc = JsonDocument.Parse(result.Stdout.Trim());
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("id", out var idEl));
        Assert.Equal("smoke-1", idEl.GetString());

        if (root.TryGetProperty("error", out var errEl))
            Assert.Fail($"voxelforge-eval returned an error: {errEl.GetString()}");

        Assert.True(root.TryGetProperty("score", out var scoreEl),
            "Output JSON missing 'score' object.");

        // Sanity: score has TotalScore + PeakWallT_K (the headline scalars).
        Assert.True(scoreEl.TryGetProperty("TotalScore", out _), "score missing TotalScore");
        Assert.True(scoreEl.TryGetProperty("PeakWallT_K", out var peakEl), "score missing PeakWallT_K");
        double peakK = peakEl.GetDouble();
        Assert.InRange(peakK, 200.0, 6000.0);   // chamber wall T must be in physics-sane range
    }

    [Fact]
    public void Streaming_TwoRequests_ProducesTwoOutputs()
    {
        string exe = LocateExe();
        if (!SubprocessRunner.ProbeExe(exe).ExeExists) return;

        var sb = new StringBuilder();
        sb.AppendLine(SeedJson());
        sb.AppendLine(SeedJson());

        var result = SubprocessRunner.Run(
            exe, args: new[] { "--jsonl" }, stdin: sb.ToString(), timeoutMs: 30_000);

        Assert.True(result.Succeeded, result.DescribeFailure());

        var lines = result.Stdout.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        // Trim possible \r from windows-style line endings.
        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line.Trim());
            Assert.True(doc.RootElement.TryGetProperty("score", out _),
                $"Streaming output missing 'score': {line}");
        }
    }

    [Fact]
    public void OneShot_MalformedJson_ReturnsErrorObject()
    {
        string exe = LocateExe();
        if (!SubprocessRunner.ProbeExe(exe).ExeExists) return;

        var result = SubprocessRunner.Run(
            exe, args: null, stdin: "{ this is not json", timeoutMs: 30_000);

        // The CLI handles malformed input gracefully — exit 0 + an
        // {"error": "..."} object on stdout. This is the documented
        // contract; SubprocessResult.Succeeded is therefore the right
        // assertion (semantic failure would surface as a malformed
        // response below, not a non-zero exit).
        Assert.True(result.Succeeded, result.DescribeFailure());
        using var doc = JsonDocument.Parse(result.Stdout.Trim());
        Assert.True(doc.RootElement.TryGetProperty("error", out var errEl));
        Assert.False(string.IsNullOrWhiteSpace(errEl.GetString()));
    }
}
