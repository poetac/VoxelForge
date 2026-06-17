// OOB-5 (2026-04-25): --sobol CLI entry point. Wraps SobolSensitivity
// over the SA design vector — pack/unpack via DesignVariableBinder,
// score via RegenChamberOptimization.GenerateWith + Evaluate.

using System;
using System.Globalization;
using System.IO;
using Voxelforge.Combustion;
using Voxelforge.Injector;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class SobolSensitivityCli
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --sobol "
      + "[--design-preset <merlin|rl10|pressure-fed-small|aerospike|pintle>=merlin] "
      + "[--N <int=256>] [--seed <int=42>] [--out <path>]";

    public static int Run(string[] args)
    {
        string presetName = "merlin";
        int N = 256;
        int seed = 42;
        string? outPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--design-preset":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--design-preset missing value"); return 1; }
                    presetName = args[++i];
                    break;
                case "--N":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--N missing value"); return 1; }
                    if (!int.TryParse(args[++i], out N) || N < 16 || N > 100_000)
                    { Console.Error.WriteLine($"--N must be 16..100000, got '{args[i]}'"); return 1; }
                    break;
                case "--seed":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--seed missing value"); return 1; }
                    if (!int.TryParse(args[++i], out seed))
                    { Console.Error.WriteLine($"--seed must be int, got '{args[i]}'"); return 1; }
                    break;
                case "--out":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--out missing value"); return 1; }
                    outPath = args[++i];
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

        CanonicalDesigns.Preset preset;
        try { preset = CanonicalDesigns.Get(presetName); }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }

        Console.WriteLine($"# sobol preset={preset.Name} N={N} seed={seed}");
        Console.WriteLine($"# preset: {preset.Description}");

        // Use the same equilibrium toggle the preset recommends — the SA
        // baseline behaviour the user would actually optimize against.
        PropellantTables.UseEquilibrium = preset.Seed.UseEquilibriumRecommended;

        var cond = preset.Seed.Conditions;
        var baselineDesign = preset.Seed.Design;
        var baselinePattern = baselineDesign.InjectorElementPattern;

        // SA bounds + descriptors. The SA vector spans both
        // RegenChamberDesign and (optionally) the active InjectorPattern.
        var registryTypes = baselinePattern is null
            ? new[] { typeof(RegenChamberDesign) }
            : new[] { typeof(RegenChamberDesign), baselinePattern.GetType() };
        var bounds = DesignVariableRegistry.BoundsForMany(registryTypes);
        var descriptors = DesignVariableRegistry.DescriptorsForMany(registryTypes);
        int D = bounds.Length;
        Console.WriteLine($"# SA dimensions: {D}");

        // Score function: x ∈ [0,1]^D → packed SA vector → unpack →
        // PeakGasSideWallT_K (a continuous physics scalar that's always
        // finite, unlike RegenScoreResult.TotalScore which is +Inf on
        // infeasibility). PeakWallT_K is one of the most physics-meaningful
        // scalars — it's the headline thermal-design driver. Future
        // CLI-flag work could expose a `--target` to select among
        // CoolantPressureDrop_Pa, MinSafetyFactor, etc.
        double Score(double[] x)
        {
            var packed = new double[D];
            for (int i = 0; i < D; i++)
                packed[i] = bounds[i].Min + x[i] * (bounds[i].Max - bounds[i].Min);
            var design = DesignVariableBinder.Unpack(packed, baselineDesign);
            try
            {
                var gen = RegenChamberOptimization.GenerateWith(
                    cond, design, voxelSize_mm: 0.0,
                    skipVoxelGeometry: true, skipMfgAnalysis: true);
                double t = gen.Thermal.PeakGasSideWallT_K;
                // Defensive: clamp to a sane range. Bartz can produce
                // very large numbers under pathological inputs.
                if (double.IsNaN(t) || double.IsInfinity(t)) return 0.0;
                return System.Math.Min(t, 1e6);
            }
            catch
            {
                return 0.0;
            }
        }

        var dimNames = new string[D];
        for (int i = 0; i < D; i++) dimNames[i] = descriptors[i].MemberName;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var indices = SobolSensitivity.Compute(Score, D, N: N, seed: seed, dimNames: dimNames);
        sw.Stop();
        Console.WriteLine($"# evals: {N * (D + 2)}  wall: {sw.Elapsed.TotalSeconds:F1} s");
        Console.WriteLine();

        string table = SobolSensitivity.FormatSortedTable(indices);
        Console.WriteLine(table);
        if (N < 512)
        {
            Console.WriteLine();
            Console.WriteLine($"# WARNING: at N={N} the first-order S_i estimator is noisy");
            Console.WriteLine($"#          (can produce small negative values or values >1.0).");
            Console.WriteLine($"#          Re-run with --N 512 or higher for production indices.");
            Console.WriteLine($"#          ST_i is more robust at low N — prefer it for ranking.");
        }

        if (outPath != null)
        {
            using var sb = new StringWriter(CultureInfo.InvariantCulture);
            sb.WriteLine($"# preset={preset.Name} N={N} seed={seed} D={D}");
            sb.WriteLine($"# evals={N * (D + 2)} wall_s={sw.Elapsed.TotalSeconds:F1}");
            sb.WriteLine(table);
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"# wrote {outPath}");
        }

        return 0;
    }
}
