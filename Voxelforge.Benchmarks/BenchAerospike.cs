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
    //  Aerospike / plug-nozzle CLI
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse the `--aerospike` arg block and dispatch to
    /// <see cref="Geometry.AerospikeBuilder.Build"/>. Outputs a single-
    /// piece engine STL. Regen / thermal solver involvement is
    /// layered on by the plug-cooling path.
    ///
    /// Usage:
    ///   dotnet run --project Voxelforge.Benchmarks -c Release -- \
    ///     --aerospike --propellant LOX_CH4 --thrust 20000 --pc 7e6 \
    ///     --eps 15 [--plug 0.30] [--voxel 0.4] [--out aerospike.stl]
    ///
    /// Exit codes: 0 = success, 3 = arg-parse error, 4 = runtime error.
    /// </summary>
    private static int RunAerospike(string[] args)
    {
        // Default spec
        PropellantPair pair = PropellantPair.LOX_CH4;
        double thrust_N = 20_000;
        double pc_Pa = 7e6;
        double eps = 15.0;
        double plugRatio = 0.30;
        double voxel_mm = 0.4;
        string outStl = "aerospike.stl";
        // Plug-cooling additions
        bool includeChannels = false;
        int plugChannelCount = 24;
        double plugChannelWidth_mm = 2.5;
        double plugChannelDepth_mm = 2.0;
        int wallMaterialIndex = 1;
        // Optional injector pattern so CLI users can exercise the
        // aerospike feasibility gates (AEROSPIKE_ELEMENT_CLEARANCE,
        // AEROSPIKE_INJECTOR_FACE_TEMP). When --pattern-elements is
        // supplied, a default Coax pattern of the requested element
        // count is attached to the AerospikeSpec.
        int patternElements = 0;   // 0 = no pattern, legacy behaviour
        // Optional VTI field export for CFD handoff. Null = no .vti
        // write (default behaviour).
        string? outVti = null;

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
                    case "--plug":
                        if (i + 1 >= args.Length) throw new ArgumentException("--plug needs a value");
                        if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out plugRatio))
                            throw new ArgumentException($"Bad plug ratio '{args[i]}'");
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
                    case "--channels":
                        // Enable plug regen cooling.
                        includeChannels = true;
                        break;
                    case "--channel-count":
                        if (i + 1 >= args.Length) throw new ArgumentException("--channel-count needs a value");
                        if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out plugChannelCount))
                            throw new ArgumentException($"Bad channel count '{args[i]}'");
                        includeChannels = true;
                        break;
                    case "--channel-width":
                        if (i + 1 >= args.Length) throw new ArgumentException("--channel-width needs a value");
                        if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out plugChannelWidth_mm))
                            throw new ArgumentException($"Bad channel width '{args[i]}'");
                        includeChannels = true;
                        break;
                    case "--channel-depth":
                        if (i + 1 >= args.Length) throw new ArgumentException("--channel-depth needs a value");
                        if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out plugChannelDepth_mm))
                            throw new ArgumentException($"Bad channel depth '{args[i]}'");
                        includeChannels = true;
                        break;
                    case "--wall-material":
                        if (i + 1 >= args.Length) throw new ArgumentException("--wall-material needs a value");
                        if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out wallMaterialIndex))
                            throw new ArgumentException($"Bad wall material index '{args[i]}'");
                        break;
                    case "--pattern-elements":
                        // Enable the aerospike injector sizing +
                        // face-thermal path. A default Coax pattern
                        // with the specified element count
                        // is attached to the spec; the builder produces
                        // AerospikeBuildResult.InjectorSizing + InjectorFace,
                        // and AerospikeFeasibility.Evaluate fires
                        // AEROSPIKE_ELEMENT_CLEARANCE / AEROSPIKE_INJECTOR_FACE_TEMP
                        // as appropriate.
                        if (i + 1 >= args.Length) throw new ArgumentException("--pattern-elements needs a value");
                        if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out patternElements))
                            throw new ArgumentException($"Bad pattern-elements '{args[i]}'");
                        if (patternElements < 1)
                            throw new ArgumentException($"--pattern-elements must be at least 1; got {patternElements}");
                        break;
                    case "--out-vti":
                        // Sprint 10 Track C (2026-04-22) — write a VTK ImageData
                        // field file alongside the STL, for CFD handoff.
                        if (i + 1 >= args.Length) throw new ArgumentException("--out-vti missing value");
                        outVti = args[++i];
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown arg '{args[i]}'. Usage:");
                        Console.Error.WriteLine(AerospikeArgs.UsageLine);
                        return 3;
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(AerospikeArgs.UsageLine);
                return 3;
            }
        }

        try
        {
            // PicoGK Library initialisation — voxel size locked for the
            // life of this process, same constraint as the regen path.
            using var lib = new Library((float)voxel_mm);

            // Sprint 8 Track B: optional injector pattern from CLI flag.
            Injector.InjectorPattern? pattern = patternElements > 0
                ? Injector.InjectorPattern.DefaultCoax(patternElements)
                : null;

            var spec = new Geometry.AerospikeSpec(
                Thrust_N:             thrust_N,
                ChamberPressure_Pa:   pc_Pa,
                ExpansionRatio:       eps,
                PlugLengthRatio:      plugRatio,
                PropellantPair:       pair,
                IncludeRegenChannels: includeChannels,
                PlugChannelCount:     plugChannelCount,
                PlugChannelWidth_mm:  plugChannelWidth_mm,
                PlugChannelDepth_mm:  plugChannelDepth_mm,
                WallMaterialIndex:    wallMaterialIndex,
                InjectorPattern:      pattern);
            Console.WriteLine($"# Aerospike spec: {pair} @ {thrust_N/1000:F1} kN, "
                            + $"Pc={pc_Pa/1e6:F1} MPa, ε={eps:F0}, plug={plugRatio:F2}, "
                            + $"voxel={voxel_mm:F2} mm");

            var sw = Stopwatch.StartNew();
            var result = Geometry.AerospikeBuilder.Build(spec, voxel_mm);
            sw.Stop();

            Console.WriteLine($"# {result.Description}");
            Console.WriteLine($"# throat: R_inner={result.ThroatInnerRadius_mm:F2} mm, "
                            + $"R_outer={result.ThroatOuterRadius_mm:F2} mm");
            Console.WriteLine($"# plug:   length={result.PlugTruncatedLength_mm:F2} mm, "
                            + $"base R={result.Contour.PlugBaseRadius_mm:F2} mm");
            Console.WriteLine($"# total:  L={result.TotalLength_mm:F1} mm, "
                            + $"D={result.TotalDiameter_mm:F1} mm");
            Console.WriteLine($"# mass:   {result.EstimatedMass_g:F0} g ({result.SolidVolume_mm3/1000:F1} cm³)");
            if (result.Thermal is { } t)
            {
                Console.WriteLine($"# regen:  {spec.PlugChannelCount} channels, "
                                + $"peak wall T = {t.PeakGasSideWallT_K:F0} K @ x = {t.PeakStation_X_mm:F1} mm");
                Console.WriteLine($"# regen:  coolant outlet = {t.CoolantOutletT_K:F0} K, "
                                + $"ΔP = {t.CoolantPressureDrop_Pa/1e6:F2} MPa, "
                                + $"Q_total = {t.TotalHeatLoad_W/1000:F1} kW");
            }
            // Sprint 7 Track A + Sprint 8 Track A: report sized injector
            // pattern + face thermal when --pattern-elements is set.
            if (result.InjectorSizing is { } inj)
            {
                Console.WriteLine($"# inject: {inj.PatternSizing.ElementCount} elements on "
                                + $"R={inj.PitchCircleRadius_mm:F1} mm pitch circle, "
                                + $"arc={inj.ArcSpacing_mm:F2} mm, "
                                + $"OD={inj.ElementOuterDiameter_mm:F2} mm, "
                                + $"clearance {(inj.ClearanceOk ? "OK" : "FAIL")}");
            }
            if (result.InjectorFace is { } face)
            {
                Console.WriteLine($"# face:   T_face = {face.TFace_K:F0} K "
                                + $"(T_aw = {face.TAwCore_K:F0} K, "
                                + $"T_prop = {face.TPropAvg_K:F0} K, "
                                + $"bore coverage = {face.BoreAreaFraction:P1})");
            }
            // Sprint 8 Track B (2026-04-22) — always run the feasibility
            // evaluator so CLEARANCE / INJECTOR_FACE_TEMP gates surface
            // even on pattern-only invocations (previously gated on the
            // Phase-2 regen-channel path).
            {
                var feas = Geometry.AerospikeFeasibility.Evaluate(result, spec.WallMaterialIndex);
                if (!feas.IsFeasible)
                {
                    Console.Error.WriteLine("# FEASIBILITY violations:");
                    foreach (var v in feas.Violations)
                        Console.Error.WriteLine($"#   [{v.ConstraintId}] {v.Description}");
                }
                else
                {
                    Console.WriteLine("# feasibility: PASS (all aerospike gates within limits)");
                }
            }
            Console.WriteLine($"BENCH aerospike_build_ms = {sw.ElapsedMilliseconds}");

            // Mesh + write STL. AerospikeBuilder.Build (not BuildPhysicsOnly)
            // always populates Voxels — Sprint 2a (2026-04-22) made the field
            // nullable on the result record to support the xUnit-safe
            // physics-only entry point, but this code path always goes
            // through Build so the non-null assertion is safe.
            sw.Restart();
            var mesh = new Mesh(result.Voxels!.AsPicoGK());
            sw.Stop();
            Console.WriteLine($"BENCH aerospike_mesh_ms = {sw.ElapsedMilliseconds} "
                            + $"tris = {mesh.nTriangleCount()}");

            sw.Restart();
            mesh.SaveToStlFile(outStl);
            sw.Stop();
            var fi = new FileInfo(outStl);
            Console.WriteLine($"BENCH aerospike_stl_write_ms = {sw.ElapsedMilliseconds} "
                            + $"bytes = {fi.Length}");
            Console.WriteLine($"# STL written to {outStl}");

            // ── Sprint 10 Track C: optional VTK ImageData (.vti) field
            //    export for CFD handoff, mirroring the bell-chamber path.
            if (outVti != null)
            {
                try
                {
                    sw.Restart();
                    var vtiStats = Voxelforge.IO.CfdFieldExport.WriteAerospike(
                        outPath: outVti,
                        contour: result.Contour,
                        thermal: result.Thermal);
                    sw.Stop();
                    Console.WriteLine($"# VTI: {vtiStats.Nx}×{vtiStats.Ny}×{vtiStats.Nz} grid, "
                                    + $"{vtiStats.SolidVoxelCount} solid / {vtiStats.FluidVoxelCount} fluid voxels, "
                                    + $"{vtiStats.FileBytes / (1024.0 * 1024.0):F1} MB, "
                                    + $"{vtiStats.WriteWallMs:F0} ms → {outVti}");
                    Console.WriteLine($"BENCH aerospike_vti_write_ms = {sw.ElapsedMilliseconds}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"VTI export failed: {ex.Message}");
                    // Non-fatal: STL already written.
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Aerospike build failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 4;
        }
    }
}
