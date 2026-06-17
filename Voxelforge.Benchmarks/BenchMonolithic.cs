using System.Diagnostics;
using System.Globalization;
using System.Text;
using PicoGK;
using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;
using Voxelforge.IO;
using Voxelforge.Manufacturing;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

public static partial class Program
{
    // ─────────────────────────────────────────────────────────────────
    //  Monolithic engine capstone
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse `--monolithic` args and dispatch to
    /// `MonolithicEngineBuilder.Build`. Writes a single STL that
    /// fuses chamber + pumps + manifold + preburner into one body.
    ///
    /// Exit codes: 0 success, 3 arg-parse error, 4 runtime error.
    /// </summary>
    private static int RunMonolithic(string[] args)
    {
        PropellantPair pair = PropellantPair.LOX_CH4;
        double thrust_N = 20_000;
        double pc_Pa = 7e6;
        double eps = 15.0;
        FeedSystem.EngineCycle cycle = FeedSystem.EngineCycle.GasGenerator;
        double voxel_mm = 0.4;
        string outStl = "engine-monolithic.stl";
        // Polish knobs.
        double filletRadius_mm = Geometry.MonolithicEngineBuilder.DefaultBendFilletRadius_mm;
        bool includeFlanges = true;
        bool includePreburner = true;
        // Route the chamber through the aerospike pipeline instead of
        // the regen bell builder.
        bool useAerospike = false;

        for (int i = 0; i < args.Length; i++)
        {
            try
            {
                switch (args[i])
                {
                    case "--propellant":
                        if (i + 1 >= args.Length) throw new ArgumentException("--propellant needs a value");
                        if (!Enum.TryParse<PropellantPair>(args[++i], ignoreCase: true, out var p))
                            throw new ArgumentException($"Unknown propellant '{args[i]}'");
                        pair = p;
                        break;
                    case "--thrust":
                        if (i + 1 >= args.Length) throw new ArgumentException("--thrust needs a value");
                        if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out thrust_N))
                            throw new ArgumentException($"Bad thrust '{args[i]}'");
                        break;
                    case "--pc":
                        if (i + 1 >= args.Length) throw new ArgumentException("--pc needs a value");
                        if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out pc_Pa))
                            throw new ArgumentException($"Bad pc '{args[i]}'");
                        break;
                    case "--eps":
                        if (i + 1 >= args.Length) throw new ArgumentException("--eps needs a value");
                        if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out eps))
                            throw new ArgumentException($"Bad eps '{args[i]}'");
                        break;
                    case "--cycle":
                        if (i + 1 >= args.Length) throw new ArgumentException("--cycle needs a value");
                        if (!Enum.TryParse<FeedSystem.EngineCycle>(args[++i], ignoreCase: true, out var c))
                            throw new ArgumentException($"Bad cycle '{args[i]}'");
                        cycle = c;
                        break;
                    case "--voxel":
                        if (i + 1 >= args.Length) throw new ArgumentException("--voxel needs a value");
                        if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out voxel_mm))
                            throw new ArgumentException($"Bad voxel '{args[i]}'");
                        break;
                    case "--out":
                        if (i + 1 >= args.Length) throw new ArgumentException("--out needs a value");
                        outStl = args[++i];
                        break;
                    // Polish knobs.
                    case "--fillet":
                        if (i + 1 >= args.Length) throw new ArgumentException("--fillet needs a value");
                        if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out filletRadius_mm))
                            throw new ArgumentException($"Bad fillet '{args[i]}'");
                        break;
                    case "--no-flanges":
                        includeFlanges = false;
                        break;
                    case "--no-preburner":
                        includePreburner = false;
                        break;
                    case "--aerospike":
                        // Route the chamber through AerospikeBuilder
                        // instead of the regen bell. Feed manifold +
                        // turbopump composition is identical to the
                        // regen path; chamber body is an aerospike
                        // (annular throat + plug).
                        useAerospike = true;
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown arg '{args[i]}'.");
                        Console.Error.WriteLine(MonolithicArgs.UsageLine);
                        return 3;
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(MonolithicArgs.UsageLine);
                return 3;
            }
        }

        try
        {
            using var lib = new Library((float)voxel_mm);

            var spec = new EngineSpec(
                PropellantPair:      pair,
                Thrust_N:            thrust_N,
                ChamberPressure_Pa:  pc_Pa,
                ExpansionRatio:      eps,
                EngineCycleOverride: cycle);

            string chamberType = useAerospike ? "aerospike" : "regen bell";
            Console.WriteLine($"# Monolithic engine: {pair} @ {thrust_N/1000:F1} kN, "
                            + $"Pc={pc_Pa/1e6:F1} MPa, ε={eps:F0}, cycle={cycle}, "
                            + $"chamber={chamberType}, voxel={voxel_mm:F2} mm");

            // Thrust-aware voxel-floor advisory (CLAUDE.md: ≥ 10 kN should
            // use ≥ 0.8 mm voxel for exploration; < 10 kN can use 0.4 mm).
            // Pure heads-up — does not block. Pattern-mode SDF (Sprint 30)
            // unblocked the 20 kN @ 0.4 mm case but build time is still
            // ~15 min and peak RAM ~19 GB at that scale.
            double voxelFloor_mm = thrust_N >= 10_000 ? 0.8 : 0.4;
            if (voxel_mm < voxelFloor_mm)
            {
                Console.WriteLine(
                    $"# advisory: voxel {voxel_mm:F2} mm is below the "
                  + $"recommended floor of {voxelFloor_mm:F2} mm at "
                  + $"{thrust_N/1000:F0} kN. Build will be slow + RAM-heavy "
                  + $"(at 20 kN / 0.4 mm: ~15 min, ~19 GB peak). Use "
                  + $"--voxel {voxelFloor_mm:F1} for exploration; tighten "
                  + $"only when capturing the final STL.");
            }

            var sw = Stopwatch.StartNew();
            // Sprint 9 Track A: --aerospike flag dispatches to BuildAerospike.
            var result = useAerospike
                ? Geometry.MonolithicEngineBuilder.BuildAerospike(
                    spec, voxel_mm,
                    bendFilletRadius_mm: filletRadius_mm,
                    includePumpMountFlanges: includeFlanges,
                    includePreburnerBody: includePreburner)
                : Geometry.MonolithicEngineBuilder.Build(
                    spec, voxel_mm,
                    bendFilletRadius_mm: filletRadius_mm,
                    includePumpMountFlanges: includeFlanges,
                    includePreburnerBody: includePreburner);
            sw.Stop();

            Console.WriteLine($"# {result.Description}");
            Console.WriteLine($"# bodies fused: {result.ComponentBodyCount} (1 chamber + "
                            + $"{(result.FuelPumpGeometry is not null ? 1 : 0)} fuel pump + "
                            + $"{(result.OxPumpGeometry is not null ? 1 : 0)} ox pump + "
                            + $"{(result.FuelPumpFlangeGeometry is not null ? 1 : 0)} fuel flange + "
                            + $"{(result.OxPumpFlangeGeometry is not null ? 1 : 0)} ox flange + "
                            + $"{(result.PreburnerGeometry is not null ? 1 : 0)} preburner body + "
                            + $"{result.ManifoldLayout.Tubes.Count} manifold tubes)");
            if (result.PreburnerGeometry is { } pg)
                Console.WriteLine($"# preburner body: L={pg.TotalLength_mm:F1} mm, "
                                + $"OD={2*pg.OuterRadius_mm:F1} mm, mass≈{pg.EstimatedMass_g:F0} g");
            if (result.FuelPumpFlangeGeometry is { } ff)
                Console.WriteLine($"# pump flanges: OD={2*ff.OuterRadius_mm:F1} mm, "
                                + $"{ff.BoltCount} × Ø{ff.BoltHoleDiameter_mm:F1} mm bolts");
            Console.WriteLine($"# manifold: {result.ManifoldLayout.Notes}");
            if (result.FuelPumpGeometry is { } fg)
                Console.WriteLine($"# fuel pump: R_tip={fg.ImpellerTipRadius_mm:F1} mm, "
                                + $"L={fg.TotalLength_mm:F1} mm, mass≈{fg.EstimatedMass_g:F0} g");
            if (result.ChamberResult.Preburner is { } pre)
                Console.WriteLine($"# preburner (fuel-rich): MR={pre.MixtureRatio:F2}, Pc={pre.ChamberPressure_Pa/1e6:F1} MPa, "
                                + $"T_warm={pre.WarmGasTemperature_K:F0} K ({pre.Cycle})");
            if (result.ChamberResult.OxidizerPreburner is { } oxPre)
                Console.WriteLine($"# preburner (ox-rich):   MR={oxPre.MixtureRatio:F2}, Pc={oxPre.ChamberPressure_Pa/1e6:F1} MPa, "
                                + $"T_warm={oxPre.WarmGasTemperature_K:F0} K ({oxPre.Cycle})");
            if (result.BodyIntersectionGate is { } bg)
            {
                if (bg.IsFeasible)
                    Console.WriteLine("# body-intersection gate: PASS (no tube passes through a non-endpoint body)");
                else
                {
                    Console.WriteLine($"# body-intersection gate: FAIL ({bg.Violations.Length} violation(s))");
                    foreach (var v in bg.Violations)
                        Console.WriteLine($"#   ▸ {v.ConstraintId}: {v.Description}");
                }
            }
            Console.WriteLine($"BENCH monolithic_build_ms = {sw.ElapsedMilliseconds}");

            sw.Restart();
            var mesh = new Mesh(result.EngineVoxels);
            sw.Stop();
            Console.WriteLine($"BENCH monolithic_mesh_ms = {sw.ElapsedMilliseconds} "
                            + $"tris = {mesh.nTriangleCount()}");

            sw.Restart();
            mesh.SaveToStlFile(outStl);
            sw.Stop();
            var fi = new FileInfo(outStl);
            Console.WriteLine($"BENCH monolithic_stl_write_ms = {sw.ElapsedMilliseconds} "
                            + $"bytes = {fi.Length}");
            Console.WriteLine($"# STL written to {outStl}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Monolithic engine build failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 4;
        }
    }
}
