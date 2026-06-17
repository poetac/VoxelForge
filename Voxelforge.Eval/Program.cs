// voxelforge-eval — T2.2 subprocess oracle (2026-04-25)
//
// Reads a design JSON on stdin, writes a score JSON on stdout. Designed
// for high-throughput external-optimizer integration (BoTorch / pymoo /
// scikit-learn from Python; pymoo from Julia; etc.) without bringing up
// the WinForms host.
//
// Part of the IObjective decoupling work (2026-04-28): scoring
// now routes through `RegenObjective.ScoreDesign(...)` so the eval
// shares the IObjective abstraction's `EvaluationResult` envelope with
// the SA optimizer + future objectives. Output JSON shape is unchanged
// — the inner score body remains the engine-specific
// `RegenScoreResult` for back-compat with existing consumers. Adding a
// new engine family (ramjet, turbojet, gas turbine) just means
// dispatching to a different `IObjective`/`ScoreDesign` here; the
// stdin / stdout contract stays the same shape per family.
//
// Modes
// ─────
// One-shot (default): one JSON object on stdin → one JSON object on
//   stdout → exit. Suitable for single-eval pipelines.
//
// Streaming (--jsonl): one JSON object per stdin line → one per stdout
//   line → exit on EOF. Suitable for Python's `subprocess.Popen` +
//   keep-alive loop pattern (1 process, N evaluations).
//
// Input contract
// ──────────────
// Each input record is a JSON object with two top-level fields:
//   {
//     "Conditions": <OperatingConditions>,
//     "Design":     <RegenChamberDesign>
//   }
// Both are the same shapes that DesignPersistence.Save writes, minus
// the Schema + Results envelope.
//
// Output contract
// ───────────────
// Each output record is the JSON-serialized RegenScoreResult plus a
// minimal per-record id field (the request's id, echoed back if
// present, otherwise omitted) so callers can correlate batched I/O.
// On error, the output object has shape:
//   { "id": ..., "error": "<message>", "errorClass": "<exception type>" }
//
// Exit codes
// ──────────
// 0 — clean exit
// 1 — usage / arg-parse error
// 2 — fatal initialization error (couldn't init PicoGK Library, etc.)

using System.Text.Json;
using PicoGK;
using Voxelforge.IO;
using Voxelforge.Optimization;

namespace Voxelforge.Eval;

public static class Program
{
    public const string UsageLine =
        "Usage: voxelforge-eval [--jsonl] [--voxel <mm=0.4>]\n"
      + "  Reads design JSON on stdin, writes RegenScoreResult JSON on stdout.\n"
      + "  --jsonl  : streaming mode (one JSON object per line, EOF to exit)\n"
      + "  --voxel  : voxel size for the Library init (skipped in physics-only path; default 0.4)";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // RegenScoreResult.TotalScore is +Infinity for infeasible designs;
        // System.Text.Json refuses to write IEEE 754 specials by default.
        // The "Infinity"/"NaN" string convention is the IETF
        // recommendation (RFC 8259 §6 — implementations MAY emit) and
        // the format Python's `json` module accepts via allow_nan=True
        // (its default). Bayesian-optimization callers (BoTorch, etc.)
        // typically map +Inf to a sentinel "infeasible" flag.
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static int Main(string[] args)
    {
        bool jsonl = false;
        double voxelSize = 0.4;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--jsonl":
                    jsonl = true;
                    break;
                case "--voxel":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--voxel missing value"); return 1; }
                    if (!double.TryParse(args[++i], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out voxelSize)
                        || voxelSize <= 0.0 || voxelSize > 10.0)
                    {
                        Console.Error.WriteLine($"--voxel must be 0.0+ and <= 10.0 mm, got '{args[i]}'");
                        return 1;
                    }
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine(UsageLine);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown arg '{args[i]}'");
                    Console.Error.WriteLine(UsageLine);
                    return 1;
            }
        }

        // PicoGK Library is process-global — initialize once, reuse across
        // all evals in this process. The voxel size is used only when
        // GenerateWith skipVoxelGeometry is false, but we initialize the
        // Library regardless because RegenChamberOptimization.GenerateWith
        // calls PicoGK.Library.Log on the memory-projection-warning path.
        Library lib;
        try
        {
            lib = new Library((float)voxelSize);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to init PicoGK Library: {ex.Message}");
            return 2;
        }

        try
        {
            return jsonl ? RunStreaming() : RunOneShot();
        }
        finally
        {
            // PicoGK 2.0 writes "Disposing Library\nDone Disposing Library" to
            // Console.Out on dispose. Suppress it so the JSON/JSONL output on
            // stdout stays parseable by callers (VoxelforgeEvalSubprocessTests).
            var savedOut = Console.Out;
            Console.SetOut(TextWriter.Null);
            try { lib.Dispose(); }
            finally { Console.SetOut(savedOut); }
        }
    }

    private static int RunOneShot()
    {
        string input = Console.In.ReadToEnd();
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("No JSON read from stdin (empty input).");
            return 0;   // empty input is not an error
        }
        ProcessOne(input);
        return 0;
    }

    private static int RunStreaming()
    {
        string? line;
        while ((line = Console.In.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            ProcessOne(line);
        }
        return 0;
    }

    private static void ProcessOne(string json)
    {
        string? id = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idEl))
                id = idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : idEl.ToString();

            if (!root.TryGetProperty("Conditions", out var condEl))
                throw new InvalidOperationException("Missing 'Conditions' field.");
            if (!root.TryGetProperty("Design", out var designEl))
                throw new InvalidOperationException("Missing 'Design' field.");

            var cond = JsonSerializer.Deserialize<OperatingConditions>(condEl.GetRawText(), JsonOpts)
                       ?? throw new InvalidOperationException("Conditions deserialised to null.");
            var design = JsonSerializer.Deserialize<RegenChamberDesign>(designEl.GetRawText(), JsonOpts)
                         ?? throw new InvalidOperationException("Design deserialised to null.");

            // Physics-only path — no voxelization, no manufacturing analysis.
            // Suitable for Bayesian / NSGA-II / surrogate optimization where
            // the score function is the only thing the external loop needs.
            //
            // Routes through RegenObjective.ScoreDesign so this CLI shares
            // the IObjective EvaluationResult envelope with the SA
            // optimizer's evaluator path. The output JSON's `score` body
            // is the engine-specific RegenScoreResult preserved
            // unchanged for back-compat.
            // #551: ScoreDesign now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
            var eval = RegenObjective.ScoreDesign(
                cond, design, RegenChamberOptimization.Profiles[0],
                skipVoxelGeometry: true, skipMfgAnalysis: true);

            // Wrap RegenScoreResult with the optional `id` echo so the
            // caller can correlate batched I/O.
            var output = new
            {
                id,
                score = eval.EngineSpecificBreakdown,
            };
            Console.Out.WriteLine(JsonSerializer.Serialize(output, JsonOpts));
        }
        catch (Exception ex)
        {
            var err = new
            {
                id,
                error = ex.Message,
                errorClass = ex.GetType().Name,
            };
            Console.Out.WriteLine(JsonSerializer.Serialize(err, JsonOpts));
        }
    }
}
