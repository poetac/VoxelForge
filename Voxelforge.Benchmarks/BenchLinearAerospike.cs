// BenchLinearAerospike.cs — BB-5b (2026-04-29): --bench-linear-aerospike
// CLI subcommand. Times AerospikeBuilder.BuildLinearPhysicsOnly against
// a canonical X-33 / XRS-2200-class LOX/CH4 linear aerospike spec and
// emits a schema-v1-compliant JSONL record.
//
// Why this exists: the Sprint 26 linear-aerospike topology
// (AerospikeBuilder.BuildLinearPhysicsOnly, AerospikeContour.IsLinear)
// is exercised indirectly from the App side during SA scoring but has no
// standing bench baseline. This subcommand captures the physics-only
// build timing (no voxels — stays PicoGK-free per the Benchmarks design
// principle) and the key sizing scalars.
//
// Design point: 20 kN LOX/CH4, Pc=7 MPa, ε=15, PlugLengthRatio=0.30,
// LinearPlugWidth_mm=60 — matches the canonical aerospike preset
// (CanonicalDesigns.Aerospike) and the AerospikeSpec docstring's
// X-33 / XRS-2200 reference.
//
// CLI: --bench-linear-aerospike [--iterations N] [--thrust N] [--out PATH]
//
// Output: BENCH summary lines + one schema-v1 JSONL row.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Voxelforge.Combustion;
using Voxelforge.Geometry;

namespace Voxelforge.Benchmarks;

internal static class BenchLinearAerospike
{
    public static int Run(string[] args)
    {
        int iterations         = 200;
        double thrust_N        = 20_000.0;
        string? outPath        = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--iterations":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out iterations) || iterations < 1)
                    {
                        Console.Error.WriteLine("--iterations requires a positive integer.");
                        return 3;
                    }
                    break;
                case "--thrust":
                    if (i + 1 >= args.Length || !double.TryParse(args[++i], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out thrust_N) || thrust_N <= 0)
                    {
                        Console.Error.WriteLine("--thrust requires a positive number (N).");
                        return 3;
                    }
                    break;
                case "--out":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--out requires a path argument.");
                        return 3;
                    }
                    outPath = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"Unknown --bench-linear-aerospike argument: {args[i]}");
                    Console.Error.WriteLine("Usage: --bench-linear-aerospike [--iterations N] [--thrust N] [--out path.jsonl]");
                    return 3;
            }
        }

        // X-33 / XRS-2200-class linear aerospike spec.
        // These values match the canonical AerospikeSpec docstring example
        // and CanonicalDesigns.Aerospike's design point.
        const double chamberPressure_Pa = 7e6;
        const double expansionRatio     = 15.0;
        const double plugLengthRatio    = 0.30;
        const double linearPlugWidth_mm = 60.0;

        var spec = new AerospikeSpec(
            Thrust_N:           thrust_N,
            ChamberPressure_Pa: chamberPressure_Pa,
            ExpansionRatio:     expansionRatio,
            PlugLengthRatio:    plugLengthRatio,
            PropellantPair:     PropellantPair.LOX_CH4,
            IsLinear:           true,
            LinearPlugWidth_mm: linearPlugWidth_mm);

        // Warm-up — JIT + table-lookup cost paid before samples.
        AerospikeBuilder.BuildLinearPhysicsOnly(spec);

        var samples = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            AerospikeBuilder.BuildLinearPhysicsOnly(spec);
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        double median = samples[iterations / 2];
        double mean   = samples.Average();
        double min    = samples[0];
        double max    = samples[iterations - 1];

        // Capture a representative result for the scalar report.
        var result = AerospikeBuilder.BuildLinearPhysicsOnly(spec);

        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine($"# bench-linear-aerospike iterations={iterations} thrust_N={thrust_N:F0}");
        Console.WriteLine($"# is_linear={result.Contour.IsLinear} plug_width_mm={linearPlugWidth_mm:F1} expansion_ratio={expansionRatio:F1}");
        Console.WriteLine($"# total_length_mm={result.TotalLength_mm:F1} total_diameter_mm={result.TotalDiameter_mm:F1}");
        Console.WriteLine($"# throat_outer_radius_mm={result.ThroatOuterRadius_mm:F3} estimated_mass_g={result.EstimatedMass_g:F1}");
        Console.WriteLine($"BENCH linear_aerospike_median_ms = {median.ToString("F3", ci)}");
        Console.WriteLine($"BENCH linear_aerospike_mean_ms   = {mean.ToString("F3", ci)}");
        Console.WriteLine($"BENCH linear_aerospike_min_ms    = {min.ToString("F3", ci)}");
        Console.WriteLine($"BENCH linear_aerospike_max_ms    = {max.ToString("F3", ci)}");

        if (outPath != null)
        {
            JsonlSchema.AppendRecord(outPath, "bench-linear-aerospike", sb =>
            {
                sb.Append("\"iterations\":").Append(iterations).Append(',');
                sb.Append("\"thrust_n\":").Append(thrust_N.ToString("F0", ci)).Append(',');
                sb.Append("\"chamber_pressure_pa\":").Append(chamberPressure_Pa.ToString("F0", ci)).Append(',');
                sb.Append("\"expansion_ratio\":").Append(expansionRatio.ToString("F1", ci)).Append(',');
                sb.Append("\"plug_length_ratio\":").Append(plugLengthRatio.ToString("F2", ci)).Append(',');
                sb.Append("\"linear_plug_width_mm\":").Append(linearPlugWidth_mm.ToString("F1", ci)).Append(',');
                sb.Append("\"throat_outer_radius_mm\":").Append(result.ThroatOuterRadius_mm.ToString("F3", ci)).Append(',');
                sb.Append("\"total_length_mm\":").Append(result.TotalLength_mm.ToString("F1", ci)).Append(',');
                sb.Append("\"total_diameter_mm\":").Append(result.TotalDiameter_mm.ToString("F1", ci)).Append(',');
                sb.Append("\"estimated_mass_g\":").Append(result.EstimatedMass_g.ToString("F1", ci)).Append(',');
                sb.Append("\"median_ms\":").Append(median.ToString("F3", ci)).Append(',');
                sb.Append("\"mean_ms\":").Append(mean.ToString("F3", ci)).Append(',');
                sb.Append("\"min_ms\":").Append(min.ToString("F3", ci)).Append(',');
                sb.Append("\"max_ms\":").Append(max.ToString("F3", ci));
            });
            Console.WriteLine($"# JSONL appended: {outPath}");
        }

        return 0;
    }
}
