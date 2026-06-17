// BenchPareto.cs — Weekly Pareto frontier characterization (#655).
//
// Runs NSGA-II on a canonical rocket preset with two objective pairs:
//   Pair A: (−IdealIspVacuum_s, Mass_g)   — performance vs. mass
//   Pair B: (Cost_USD, Mass_g)             — cost vs. mass
//
// CLI:
//   --bench-pareto --design-preset <merlin|rl10|pressure-fed-small|aerospike|pintle>
//                  [--seed <int=42>] [--population <N=50>]
//                  [--generations <N=100>]
//                  [--out <jsonl>]
//
// Output JSONL — one record per Pareto point per pair per run:
//   { "schema_version":1, ..., "bench_name":"bench-pareto",
//     "preset":"merlin", "pair":"isp_mass",
//     "seed":42, "population":50, "generations":100,
//     "generations_completed":100, "total_evaluations":5000,
//     "elapsed_ms":12345,
//     "objectives":[-312.4, 18500.0], "feasible":true,
//     "vector":[...] }
//
// Skip-on-infeasible note: NSGA-II does not require a feasible seed;
// infeasible individuals are ranked below all feasible ones. The
// Pareto front emitted may still include infeasible individuals if
// no feasible candidates exist — these are flagged via "feasible":false
// and should be excluded by downstream analysis.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Voxelforge.Combustion;
using Voxelforge.Injector;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class BenchPareto
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --bench-pareto "
      + "--design-preset <merlin|rl10|pressure-fed-small|aerospike|pintle> "
      + "[--seed <int=42>] [--population <N=50>] [--generations <N=100>] "
      + "[--out <jsonl>]";

    // ── IObjective implementation ─────────────────────────────────────

    // Threadsafe and deterministic — GenerateWith + Evaluate are pure
    // over immutable records per ADR-011. EngineSpecificBreakdown stores
    // (gen, score) so the objective extractor can project either the
    // performance or the cost/mass axis without a second physics call.
    private sealed class RocketParetoObjective : IObjective
    {
        private readonly OperatingConditions _conditions;
        private readonly RegenChamberDesign _baseline;
        private readonly DesignVariableInfo[] _variables;

        public int DimensionCount => _variables.Length;
        public IReadOnlyList<DesignVariableInfo> Variables => _variables;

        public RocketParetoObjective(OperatingConditions conditions, RegenChamberDesign baseline)
        {
            _conditions = conditions;
            _baseline   = baseline;

            var descriptors = DesignVariableRegistry.DescriptorsForMany(
                typeof(RegenChamberDesign), typeof(InjectorPattern));
            _variables = new DesignVariableInfo[descriptors.Length];
            for (int i = 0; i < descriptors.Length; i++)
                _variables[i] = new DesignVariableInfo(descriptors[i].MemberName, descriptors[i].Min, descriptors[i].Max);
        }

        public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
        {
            if (vector.Length != _variables.Length)
                throw new ArgumentException(
                    $"vector length {vector.Length} != DimensionCount {_variables.Length}");

            var design = RegenChamberOptimization.Unpack(vector, _baseline);

            var preScreen = FeasibilityGate.PreScreen(_conditions, design);
            if (preScreen is not null)
                return new EvaluationResult(
                    Score:                   double.PositiveInfinity,
                    Violations:              new[] { preScreen },
                    EngineSpecificBreakdown: null);

            try
            {
                var gen   = RegenChamberOptimization.GenerateWith(
                    _conditions, design,
                    skipVoxelGeometry: true, skipMfgAnalysis: true);
                var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);

                return new EvaluationResult(
                    Score:                   score.TotalScore,
                    Violations:              score.FeasibilityViolations,
                    EngineSpecificBreakdown: (gen, score));
            }
            catch
            {
                return new EvaluationResult(
                    Score:                   double.PositiveInfinity,
                    Violations:              Array.Empty<FeasibilityViolation>(),
                    EngineSpecificBreakdown: null);
            }
        }
    }

    // ── Objective extractors ──────────────────────────────────────────

    private static double[] ExtractIspMass(EvaluationResult ev)
    {
        if (ev.EngineSpecificBreakdown is not (RegenGenerationResult gen, RegenScoreResult score))
            return [0.0, double.MaxValue];
        return [-gen.Derived.IdealIspVacuum_s, score.Mass_g];
    }

    private static double[] ExtractCostMass(EvaluationResult ev)
    {
        if (ev.EngineSpecificBreakdown is not (RegenGenerationResult _, RegenScoreResult score))
            return [double.MaxValue, double.MaxValue];
        return [score.Cost_USD, score.Mass_g];
    }

    // ── CLI entry point ───────────────────────────────────────────────

    public static int Run(string[] args)
    {
        int    seed        = 42;
        int    population  = 50;
        int    generations = 100;
        string? presetName = null;
        string? outPath    = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--design-preset":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--design-preset missing value"); return 3; }
                    presetName = args[++i];
                    break;
                case "--seed":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--seed missing value"); return 3; }
                    if (!int.TryParse(args[++i], out seed))
                    { Console.Error.WriteLine($"--seed must be int, got '{args[i]}'"); return 3; }
                    break;
                case "--population":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--population missing value"); return 3; }
                    if (!int.TryParse(args[++i], out population) || population < 4 || population % 2 != 0)
                    { Console.Error.WriteLine($"--population must be even ≥ 4, got '{args[i]}'"); return 3; }
                    break;
                case "--generations":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--generations missing value"); return 3; }
                    if (!int.TryParse(args[++i], out generations) || generations < 1)
                    { Console.Error.WriteLine($"--generations must be ≥ 1, got '{args[i]}'"); return 3; }
                    break;
                case "--out":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--out missing value"); return 3; }
                    outPath = args[++i];
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine(UsageLine);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown arg '{args[i]}'");
                    Console.Error.WriteLine(UsageLine);
                    return 3;
            }
        }

        if (presetName is null)
        {
            Console.Error.WriteLine("Missing required --design-preset.");
            Console.Error.WriteLine(UsageLine);
            return 3;
        }

        CanonicalDesigns.Preset preset;
        try { preset = CanonicalDesigns.Get(presetName); }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 3; }

        outPath ??= Path.Combine(AppContext.BaseDirectory, "baselines", "pareto",
            $"bench-pareto-{preset.Name}-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        PropellantTables.UseEquilibrium = preset.Seed.UseEquilibriumRecommended;

        var objective = new RocketParetoObjective(
            preset.Seed.Conditions, preset.Seed.Design);

        Console.WriteLine($"# bench-pareto preset={preset.Name} seed={seed} population={population} generations={generations}");
        Console.WriteLine($"# JSONL: {outPath}");

        // Pair A: (−Isp_vacuum_s, Mass_g)
        RunPair(objective, "isp_mass",  ExtractIspMass,  preset.Name, seed, population, generations, outPath);

        // Pair B: (Cost_USD, Mass_g)
        RunPair(objective, "cost_mass", ExtractCostMass, preset.Name, seed, population, generations, outPath);

        Console.WriteLine();
        Console.WriteLine($"BENCH_MEDIAN  bench=bench-pareto  preset={preset.Name}  pairs=2");
        return 0;
    }

    private static void RunPair(
        RocketParetoObjective objective,
        string pairLabel,
        Func<EvaluationResult, double[]> extractor,
        string presetName,
        int seed,
        int population,
        int generations,
        string outPath)
    {
        Console.WriteLine();
        Console.WriteLine($"# === pair={pairLabel} seed={seed} ===");

        var sw = Stopwatch.StartNew();
        var nsga = new NsgaIIOptimizer(objective, extractor, population, generations, seed);
        var result = nsga.Run(CancellationToken.None);
        sw.Stop();

        Console.WriteLine($"# pair={pairLabel} generations_completed={result.GenerationsCompleted} evals={result.TotalEvaluations} pareto_size={result.ParetoFront.Count} elapsed_ms={sw.ElapsedMilliseconds}");

        var c = CultureInfo.InvariantCulture;
        foreach (var ind in result.ParetoFront)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            JsonlSchema.AppendProvenance(sb, "bench-pareto");
            sb.Append("\"preset\":\"").Append(presetName).Append("\",");
            sb.Append("\"pair\":\"").Append(pairLabel).Append("\",");
            sb.Append("\"seed\":").Append(seed).Append(',');
            sb.Append("\"population\":").Append(population).Append(',');
            sb.Append("\"generations\":").Append(generations).Append(',');
            sb.Append("\"generations_completed\":").Append(result.GenerationsCompleted).Append(',');
            sb.Append("\"total_evaluations\":").Append(result.TotalEvaluations).Append(',');
            sb.Append("\"elapsed_ms\":").Append(sw.ElapsedMilliseconds).Append(',');
            sb.Append("\"feasible\":").Append(ind.IsFeasible ? "true" : "false").Append(',');
            sb.Append("\"objectives\":[");
            if (ind.Objectives is { } objs)
            {
                for (int i = 0; i < objs.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(JsonNumber(objs[i]));
                }
            }
            sb.Append("],");
            sb.Append("\"vector\":[");
            for (int i = 0; i < ind.Vector.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(JsonNumber(ind.Vector[i]));
            }
            sb.Append(']');
            JsonlSchema.AppendRecord(outPath, sb);
        }
    }

    private static string JsonNumber(double x)
        => double.IsFinite(x)
            ? x.ToString("R", CultureInfo.InvariantCulture)
            : "null";
}
