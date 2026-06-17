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
    //  Turbopump geometry CLI
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse `--turbopump` args and dispatch to
    /// `TurbopumpGeometryGenerator`. Calls `TurbopumpSizing.Size` with
    /// a minimally-configured OperatingConditions (thrust + Pc +
    /// cycle) so the pump has head/power/RPM populated, then emits
    /// the chosen side (fuel or ox) as a standalone STL.
    ///
    /// Exit codes: 0 success, 3 arg-parse error, 4 runtime error.
    /// </summary>
    private static int RunTurbopump(string[] args)
    {
        PropellantPair pair = PropellantPair.LOX_CH4;
        double thrust_N = 20_000;
        double pc_Pa = 7e6;
        FeedSystem.EngineCycle cycle = FeedSystem.EngineCycle.GasGenerator;
        string side = "fuel";
        double voxel_mm = 0.4;
        string outStl = "turbopump.stl";

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
                    case "--cycle":
                        if (i + 1 >= args.Length) throw new ArgumentException("--cycle needs a value");
                        if (!Enum.TryParse<FeedSystem.EngineCycle>(args[++i], ignoreCase: true, out var c)
                            || c == FeedSystem.EngineCycle.PressureFed)
                            throw new ArgumentException($"Bad cycle '{args[i]}' (PressureFed has no pump)");
                        cycle = c;
                        break;
                    case "--side":
                        if (i + 1 >= args.Length) throw new ArgumentException("--side needs a value");
                        side = args[++i].ToLowerInvariant();
                        if (side != "fuel" && side != "ox")
                            throw new ArgumentException($"Bad side '{side}'; use fuel or ox");
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
                    default:
                        Console.Error.WriteLine($"Unknown arg '{args[i]}'.");
                        Console.Error.WriteLine(TurbopumpArgs.UsageLine);
                        return 3;
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(TurbopumpArgs.UsageLine);
                return 3;
            }
        }

        try
        {
            using var lib = new Library((float)voxel_mm);

            // Seed a minimal design + conditions via AutoSeeder so
            // every derived value (mass flows, densities) is populated
            // consistently with the regen path.
            var spec = new EngineSpec(
                PropellantPair:     pair,
                Thrust_N:           thrust_N,
                ChamberPressure_Pa: pc_Pa,
                ExpansionRatio:     15.0,
                EngineCycleOverride: cycle);
            var seed = AutoSeeder.Seed(spec);

            var gas = PropellantTables.Lookup(seed.Conditions.PropellantPair,
                seed.Conditions.MixtureRatio, seed.Conditions.ChamberPressure_Pa);
            double totalMdot = thrust_N
                / Math.Max(gas.CStar_ms * seed.Conditions.CStarEfficiency * 1.5, 1e-6);
            double fuelMdot = totalMdot / (1.0 + seed.Conditions.MixtureRatio);
            double oxMdot = totalMdot - fuelMdot;
            var (oxRho, fuelRho) = OrificeModel.InjectionDensities(pair);

            var sized = FeedSystem.TurbopumpSizing.Size(
                cycle:                cycle,
                cond:                 seed.Conditions,
                fuelFlow_kgs:         fuelMdot,
                oxFlow_kgs:           oxMdot,
                fuelDensity_kgm3:     fuelRho,
                oxDensity_kgm3:       oxRho,
                fuelInletPressure_Pa: 0.3e6,
                oxInletPressure_Pa:   0.3e6,
                dischargePressure_Pa: pc_Pa * 1.5);

            var pumpSide = side == "fuel" ? sized.FuelPump : sized.OxPump;
            if (pumpSide is null)
            {
                Console.Error.WriteLine($"Side '{side}' not sized on this cycle.");
                return 4;
            }

            var geom = Turbopump.TurbopumpGeometryGenerator.Generate(pumpSide);
            if (geom is null)
            {
                Console.Error.WriteLine($"Pump geometry generation failed (degenerate pump: RPM={pumpSide.Rpm}, H={pumpSide.HeadRise_m}).");
                return 4;
            }

            Console.WriteLine($"# Turbopump: {pair} @ {thrust_N/1000:F1} kN, cycle={cycle}, side={side}");
            Console.WriteLine($"# {geom.Notes}");
            Console.WriteLine($"# sizing: head={pumpSide.HeadRise_m:F0} m, "
                            + $"RPM={pumpSide.Rpm:F0}, shaft={pumpSide.ShaftPower_W/1000:F1} kW, "
                            + $"NPSHA={pumpSide.NPSHA_m:F1} m / NPSHR={pumpSide.NPSHR_m:F1} m");
            Console.WriteLine($"# geometry: impeller R_tip={geom.ImpellerTipRadius_mm:F1} mm, "
                            + $"casing R_outer={geom.CasingOuterRadius_mm:F1} mm, "
                            + $"total L={geom.TotalLength_mm:F1} mm, mass≈{geom.EstimatedMass_g:F0} g");

            var sw = Stopwatch.StartNew();
            var assembly = Turbopump.TurbopumpGeometryGenerator.BuildImplicit(geom);
            float bboxPad = 5f;
            float rMax = (float)geom.CasingOuterRadius_mm + bboxPad;
            float zMin = -bboxPad;
            float zMax = (float)geom.TotalLength_mm + bboxPad;
            var bounds = new BBox3(
                new System.Numerics.Vector3(-rMax, -rMax, zMin),
                new System.Numerics.Vector3( rMax,  rMax, zMax));
            var vox = new Voxels(assembly, bounds);
            sw.Stop();
            Console.WriteLine($"BENCH turbopump_build_ms = {sw.ElapsedMilliseconds}");

            sw.Restart();
            var mesh = new Mesh(vox);
            sw.Stop();
            Console.WriteLine($"BENCH turbopump_mesh_ms = {sw.ElapsedMilliseconds} "
                            + $"tris = {mesh.nTriangleCount()}");

            sw.Restart();
            mesh.SaveToStlFile(outStl);
            sw.Stop();
            var fi = new FileInfo(outStl);
            Console.WriteLine($"BENCH turbopump_stl_write_ms = {sw.ElapsedMilliseconds} "
                            + $"bytes = {fi.Length}");
            Console.WriteLine($"# STL written to {outStl}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Turbopump build failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 4;
        }
    }
}
