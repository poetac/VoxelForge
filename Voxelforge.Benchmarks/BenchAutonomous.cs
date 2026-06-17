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
    // ════════════════════════════════════════════════════════════════
    //  Autonomous `spec → engine` CLI
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Autonomous entry point. Takes four high-level spec inputs
    /// (propellant, thrust, Pc, ε) and produces a feasibility-gates-
    /// passing STL without requiring the user to pick any of the 18
    /// SA variables.
    ///
    /// Usage:
    ///   dotnet run --project Voxelforge.Benchmarks -- \
    ///       --autonomous --propellant LOX_CH4 --thrust 20000 \
    ///       --pc 7e6 --eps 15 --out engine.stl [--voxel 0.4] \
    ///       [--preview-only] [--analytical-preview preview.stl]
    ///
    /// Exit codes:
    ///   0 — success, STL written, all feasibility gates passed.
    ///   2 — feasibility gates failed (STL still written for inspection
    ///       unless --strict).
    ///   3 — argument parse error.
    ///   4 — propellant pair not implemented or other runtime error.
    /// </summary>
    private static int RunAutonomous(string[] args)
    {
        AutonomousArgs cli;
        try { cli = AutonomousArgs.Parse(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(AutonomousArgs.UsageLine);
            return 3;
        }

        Console.WriteLine("# Voxelforge — autonomous mode");
        Console.WriteLine($"# Spec: propellant={cli.PropellantPair} thrust={cli.Thrust_N:F0} N "
                        + $"Pc={cli.ChamberPressure_Pa / 1e6:F2} MPa ε={cli.ExpansionRatio:F1}");

        // ── 1. AutoSeed defaults ────────────────────────────────
        AutoSeedResult seed;
        try
        {
            seed = AutoSeeder.Seed(new EngineSpec(
                PropellantPair:      cli.PropellantPair,
                Thrust_N:            cli.Thrust_N,
                ChamberPressure_Pa:  cli.ChamberPressure_Pa,
                ExpansionRatio:      cli.ExpansionRatio,
                ElementTypeOverride: cli.InjectorType));

            // --layout post-tweak: overrides the AutoSeeder's heuristic.
            if (cli.Layout is { } layout && seed.Design.InjectorElementPattern is { } existing)
            {
                var overriddenPattern = existing with { FaceLayout = layout };
                seed = seed with { Design = seed.Design with { InjectorElementPattern = overriddenPattern } };
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AutoSeed failed: {ex.Message}");
            return 4;
        }

        Console.WriteLine("# ── AutoSeed rationale ───────────────────────────");
        foreach (var line in seed.Rationale)
            Console.WriteLine($"#   • {line}");
        if (cli.Layout is { } userLayout)
            Console.WriteLine($"#   • Layout overridden by CLI: {userLayout}");

        // Apply equilibrium flag. Priority order:
        //   1. Explicit CLI override (--equilibrium / --frozen)
        //   2. AutoSeeder recommendation (true at Pc > 10 MPa)
        bool useEquilibrium = cli.EquilibriumOverride ?? seed.UseEquilibriumRecommended;
        PropellantTables.UseEquilibrium = useEquilibrium;
        if (cli.EquilibriumOverride is { } eq)
            Console.WriteLine($"#   • Equilibrium overridden by CLI: {(eq ? "ON" : "OFF")}.");
        Console.WriteLine($"#   • PropellantTables.UseEquilibrium = {useEquilibrium} "
                        + $"(provider: {PropellantTables.EquilibriumCorrectionProvider.Name}).");

        // ── 2. Analytical preview (optional, always fast) ─────
        if (cli.AnalyticalPreviewPath != null)
        {
            try
            {
                var contour = ChamberContourGenerator.Generate(
                    throatRadius_mm:        ThroatRadiusApprox(seed.Conditions, seed.Design),
                    contractionRatio:       seed.Design.ContractionRatio,
                    expansionRatio:         seed.Design.ExpansionRatio,
                    characteristicLength_m: seed.Design.CharacteristicLength_m,
                    thetaN_deg:             seed.Design.BellEntranceAngle_deg,
                    thetaE_deg:             seed.Design.BellExitAngle_deg,
                    bellLengthFraction:     seed.Design.BellLengthFraction,
                    stationCount:           seed.Design.ContourStationCount);

                long pt0 = Stopwatch.GetTimestamp();
                var preview = AnalyticalPreviewMesh.BuildAndWrite(
                    new AnalyticalPreviewOptions(
                        Contour:                         contour,
                        AzimuthalSlices:                 48,
                        IncludeInjectorFlange:           seed.Design.IncludeInjectorFlange,
                        InjectorFlangeThickness_mm:      seed.Design.InjectorFlangeThickness_mm,
                        InjectorFlangeOuterRadiusFactor: seed.Design.InjectorFlangeOuterRadiusFactor,
                        IncludeMountingFlange:           seed.Design.IncludeMountingFlange,
                        MountingFlangeThickness_mm:      seed.Design.MountingFlangeThickness_mm,
                        ChannelCount:                    seed.Design.ChannelCount,
                        RibThickness_mm:                 seed.Design.RibThickness_mm,
                        ChannelHeightAverage_mm:         seed.Design.ChannelHeightThroat_mm,
                        OuterJacketThickness_mm:         seed.Design.OuterJacketThickness_mm),
                    cli.AnalyticalPreviewPath);
                long pt1 = Stopwatch.GetTimestamp();
                double ms = (pt1 - pt0) / (double)Stopwatch.Frequency * 1000.0;
                Console.WriteLine($"# Analytical preview: {preview.TotalTriangleCount} tris, "
                                + $"{ms:F1} ms total, written to {cli.AnalyticalPreviewPath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Analytical preview failed: {ex.Message}");
                // Continue — analytical preview is best-effort.
            }
        }

        // --preview-only path exits before the voxel build.
        if (cli.PreviewOnly)
        {
            Console.WriteLine("# --preview-only set; skipping voxel build + feasibility evaluation.");
            return 0;
        }

        // ── 3. Voxel build + solver + feasibility evaluation ──
        try
        {
            using var lib = new Library((float)cli.VoxelSize_mm);

            long t0 = Stopwatch.GetTimestamp();
            var gen = RegenChamberOptimization.GenerateWithAutoCoarsen(
                seed.Conditions, seed.Design, cli.VoxelSize_mm,
                maxRetries: 3,
                onVoxelSubstituted: (prev, curr, _) =>
                    Console.WriteLine($"# Voxel auto-coarsened {prev:F3} → {curr:F3} mm to fit budget."));
            long t1 = Stopwatch.GetTimestamp();
            double buildMs = (t1 - t0) / (double)Stopwatch.Frequency * 1000.0;
            Console.WriteLine($"# Chamber generated in {buildMs:F0} ms.");

            // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
            var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);
            Console.WriteLine("# ── Evaluation ────────────────────────────────────");
            Console.WriteLine($"#   Peak wall T   {score.PeakWallT_K:F0} K  (margin {score.WallTMargin_K:F0} K)");
            Console.WriteLine($"#   Coolant ΔP    {score.CoolantDP_Pa / 1e6:F2} MPa  ({100 * score.CoolantDP_Fraction:F1} % of Pc)");
            Console.WriteLine($"#   Coolant T-out {score.CoolantTOut_K:F0} K");
            Console.WriteLine($"#   Throat q̇     {score.ThroatHeatFlux_Wm2 / 1e6:F1} MW/m²");
            Console.WriteLine($"#   Mass          {score.Mass_g / 1000.0:F3} kg");
            Console.WriteLine($"#   Min SF        {score.MinSafetyFactor:F2}");

            int violationsCount = score.FeasibilityViolations?.Length ?? 0;
            if (violationsCount == 0)
            {
                Console.WriteLine("# ✅ All feasibility gates pass.");
            }
            else
            {
                Console.WriteLine($"# ⚠ {violationsCount} feasibility gate(s) violated:");
                foreach (var v in score.FeasibilityViolations!)
                    Console.WriteLine($"#     [{v.ConstraintId}] {v.Description}");
            }

            // ── 4. Write STL ────────────────────────────────────
            if (gen.Geometry.Voxels != null)
            {
                var export = ChamberVoxelBuilder.ExportStlProfiled(gen.Geometry.Voxels.AsPicoGK(),cli.OutStlPath);
                Console.WriteLine($"# Wrote {export.TriangleCount} tris, "
                                + $"{export.StlBytes / 1024.0:F0} KB → {cli.OutStlPath}");
            }
            else
            {
                Console.Error.WriteLine("# No voxel output — skipping STL write.");
                return 4;
            }

            // ── 5. A3 — VTK ImageData (.vti) field export (optional)
            if (cli.OutVtiPath != null)
            {
                try
                {
                    var vtiStats = CfdFieldExport.Write(
                        outPath:                 cli.OutVtiPath,
                        contour:                 gen.Contour,
                        channels:                new HeatTransfer.ChannelSchedule(
                            ChannelCount:              seed.Design.ChannelCount,
                            RibThickness_mm:           seed.Design.RibThickness_mm,
                            GasSideWallThickness_mm:   seed.Design.GasSideWallThickness_mm,
                            ChannelHeightAtChamber_mm: seed.Design.ChannelHeightChamber_mm,
                            ChannelHeightAtThroat_mm:  seed.Design.ChannelHeightThroat_mm,
                            ChannelHeightAtExit_mm:    seed.Design.ChannelHeightExit_mm),
                        solver:                  gen.Thermal,
                        outerJacketThickness_mm: seed.Design.OuterJacketThickness_mm);
                    Console.WriteLine($"# VTI: {vtiStats.Nx}×{vtiStats.Ny}×{vtiStats.Nz} grid, "
                                    + $"{vtiStats.SolidVoxelCount} solid / {vtiStats.FluidVoxelCount} fluid voxels, "
                                    + $"{vtiStats.FileBytes / (1024.0 * 1024.0):F1} MB, "
                                    + $"{vtiStats.WriteWallMs:F0} ms → {cli.OutVtiPath}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"VTI export failed: {ex.Message}");
                    // Non-fatal: STL already written.
                }
            }

            // ── 6. A4 — BuildOrientationAdvisor + printer params (optional)
            if (cli.PrintAdvisor || cli.OutParamsPath != null)
            {
                try
                {
                    var channelsForAdvice = new HeatTransfer.ChannelSchedule(
                        ChannelCount:              seed.Design.ChannelCount,
                        RibThickness_mm:           seed.Design.RibThickness_mm,
                        GasSideWallThickness_mm:   seed.Design.GasSideWallThickness_mm,
                        ChannelHeightAtChamber_mm: seed.Design.ChannelHeightChamber_mm,
                        ChannelHeightAtThroat_mm:  seed.Design.ChannelHeightThroat_mm,
                        ChannelHeightAtExit_mm:    seed.Design.ChannelHeightExit_mm);
                    var advisor = BuildOrientationAdvisor.Analyze(
                        gen.Contour, channelsForAdvice,
                        outerJacketThickness_mm: seed.Design.OuterJacketThickness_mm);

                    Console.WriteLine("# ── Build orientation ─────────────────────────────");
                    Console.WriteLine($"#   Recommendation: {advisor.RecommendedBuildOrientation}");
                    Console.WriteLine($"#   {advisor.RationaleText}");
                    Console.WriteLine($"#   Best worst-angle: {advisor.Best.WorstOverhangAngle_deg:F1}°, "
                                    + $"support vol ~{advisor.Best.EstimatedSupportVolume_cm3:F2} cm³");
                    foreach (var w in advisor.Warnings)
                        Console.WriteLine($"#   ⚠ {w}");

                    if (cli.OutParamsPath != null)
                    {
                        var lpbfMat = PrinterParameterPresets.FromWallMaterialIndex(
                            seed.Conditions.WallMaterialIndex);
                        if (lpbfMat == null)
                        {
                            Console.Error.WriteLine(
                                $"# Material index {seed.Conditions.WallMaterialIndex} has no LPBF preset; "
                              + $"skipping --out-params.");
                        }
                        else
                        {
                            var preset = PrinterParameterPresets.Get(cli.Machine, lpbfMat.Value);
                            PrinterParameterPresets.WriteJsonFile(preset, cli.OutParamsPath);
                            Console.WriteLine($"# Printer params: {preset.Machine} × {preset.Material} "
                                            + $"({preset.LaserPower_W:F0} W, {preset.ScanSpeed_mms:F0} mm/s, "
                                            + $"{preset.LayerThickness_mm * 1000:F0} µm layer) → {cli.OutParamsPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Manufacturing advisor failed: {ex.Message}");
                }
            }

            return violationsCount == 0 ? 0 : (cli.Strict ? 2 : 0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Autonomous generation failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 4;
        }
    }

    /// <summary>
    /// Rough throat-radius estimate from spec inputs. Used only when the
    /// analytical-preview path needs a ChamberContour before the voxel
    /// pipeline runs (the voxel path re-computes it from full CEA). The
    /// approximation is C_F × P_c × A_t = F with C_F ≈ 1.5 — good to
    /// ~10 % on R_t, which is plenty for a preview.
    /// </summary>
    private static double ThroatRadiusApprox(OperatingConditions cond, RegenChamberDesign _)
    {
        const double approxCf = 1.5;
        double A_t_m2 = cond.Thrust_N / (approxCf * cond.ChamberPressure_Pa);
        return Math.Sqrt(A_t_m2 / Math.PI) * 1000.0;
    }
}
