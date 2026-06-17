// TrackBPerStationWallThicknessTests.cs — coverage for the per-station
// gas-side wall thickness override mechanism (Track B, 2026-04-27).
//
// Helper-method + design-record tests live here, plus the Z1 hot-fix
// closed-loop regression tests (2026-04-28) that pin the thermal-solver
// + analytical-mass paths to the per-station value (B1 audit finding —
// pre-Z1 the override flowed only into StructuralCheck + ProofTestAnalysis,
// silently no-oping the thermal solver and voxel builder). Full-pipeline
// structural integration is exercised via the existing solver-driven tests
// (BaselineDesignRegressionTests etc.) which now run with the per-station
// path active and would catch any regression on the uniform-thickness
// fallback.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;
using Voxelforge.Structure;

namespace Voxelforge.Tests;

public class TrackBPerStationWallThicknessTests
{
    // ─── RegenChamberDesign field defaults ────────────────────────

    [Fact]
    public void RegenChamberDesign_OverrideFields_Default0()
    {
        var d = new RegenChamberDesign();
        Assert.Equal(0.0, d.ChamberWallThicknessOverride_mm);
        Assert.Equal(0.0, d.ThroatWallThicknessOverride_mm);
        Assert.Equal(0.0, d.ExitWallThicknessOverride_mm);
    }

    [Fact]
    public void RegenChamberDesign_OverrideFields_Settable()
    {
        var d = new RegenChamberDesign
        {
            ChamberWallThicknessOverride_mm = 1.5,
            ThroatWallThicknessOverride_mm = 2.0,
            ExitWallThicknessOverride_mm = 5.0,
        };
        Assert.Equal(1.5, d.ChamberWallThicknessOverride_mm);
        Assert.Equal(2.0, d.ThroatWallThicknessOverride_mm);
        Assert.Equal(5.0, d.ExitWallThicknessOverride_mm);
    }

    [Fact]
    public void SaRegistry_PicksUpAllThreeOverrideDims_AtPositions28_29_30()
    {
        // The new SA dims must be discoverable + ordered correctly.
        // OOB-6 / Sprint B-3 (2026-04-30) added dims 31, 32, 33 (acoustic
        // dampers); the Track-B trio at 28-30 is unchanged but the total
        // is now 34.
        var bounds = RegenChamberOptimization.Bounds;
        Assert.Equal(34, bounds.Length);
        // Each override has bounds [0.5, 8.0] (matching the
        // [SaDesignVariable] attribute on the property).
        Assert.Equal((0.5, 8.0), bounds[28]);
        Assert.Equal((0.5, 8.0), bounds[29]);
        Assert.Equal((0.5, 8.0), bounds[30]);
    }

    // ─── BuildGasSideWallProfile_mm helper ────────────────────────

    [Fact]
    public void BuildProfile_AllOverridesZero_ProducesUniformBaseline()
    {
        var profile = StructuralCheck.BuildGasSideWallProfile_mm(
            stationCount: 21, throatIdx: 10, baseline_mm: 1.5,
            chamberOverride_mm: 0, throatOverride_mm: 0, exitOverride_mm: 0);
        Assert.Equal(21, profile.Length);
        foreach (var t in profile) Assert.Equal(1.5, t);
    }

    [Fact]
    public void BuildProfile_OverridesProvided_LinearInterpolation()
    {
        var profile = StructuralCheck.BuildGasSideWallProfile_mm(
            stationCount: 11, throatIdx: 5, baseline_mm: 1.0,
            chamberOverride_mm: 1.0, throatOverride_mm: 2.0, exitOverride_mm: 5.0);

        // i=0 (chamber start): chamber thickness
        Assert.Equal(1.0, profile[0]);
        // i=5 (throat): throat thickness
        Assert.Equal(2.0, profile[5]);
        // i=10 (exit end): exit thickness
        Assert.Equal(5.0, profile[10]);
        // Interpolation: i=2 between chamber (i=0) and throat (i=5):
        //   1.0 + (2/5) * (2.0 - 1.0) = 1.4
        Assert.Equal(1.4, profile[2], precision: 6);
        // i=8 between throat (i=5) and exit (i=10):
        //   2.0 + (3/5) * (5.0 - 2.0) = 3.8
        Assert.Equal(3.8, profile[8], precision: 6);
    }

    [Fact]
    public void BuildProfile_PartialOverride_FallsBackToBaselineOnZero()
    {
        // Only the exit override is set; chamber + throat fall back.
        var profile = StructuralCheck.BuildGasSideWallProfile_mm(
            stationCount: 11, throatIdx: 5, baseline_mm: 1.0,
            chamberOverride_mm: 0, throatOverride_mm: 0, exitOverride_mm: 4.0);

        Assert.Equal(1.0, profile[0]);   // chamber = baseline
        Assert.Equal(1.0, profile[5]);   // throat = baseline
        Assert.Equal(4.0, profile[10]);  // exit = override
        // Smoothly interpolated post-throat: i=8 → 1 + (3/5) * (4-1) = 2.8
        Assert.Equal(2.8, profile[8], precision: 6);
    }

    [Fact]
    public void BuildProfile_DegenerateStationCount_HandlesZeroGracefully()
    {
        var profile = StructuralCheck.BuildGasSideWallProfile_mm(
            stationCount: 0, throatIdx: 0, baseline_mm: 1.0,
            chamberOverride_mm: 0, throatOverride_mm: 0, exitOverride_mm: 0);
        Assert.Empty(profile);
    }

    [Fact]
    public void BuildProfile_ThroatAtIndex0_TreatsAllStationsAsExitSide()
    {
        // Edge case: pathological input where throat is at station 0.
        // Should still produce a valid, finite, monotonic-or-flat profile.
        var profile = StructuralCheck.BuildGasSideWallProfile_mm(
            stationCount: 5, throatIdx: 0, baseline_mm: 1.0,
            chamberOverride_mm: 1.0, throatOverride_mm: 2.0, exitOverride_mm: 5.0);
        Assert.Equal(5, profile.Length);
        Assert.Equal(2.0, profile[0]);   // throat at i=0
        Assert.Equal(5.0, profile[4]);   // exit at i=N-1
        foreach (var t in profile)
            Assert.True(double.IsFinite(t) && t > 0,
                $"degenerate profile entry: {t}");
    }

    // ─── Z1 hot-fix: closed-loop regression (2026-04-28) ───────────
    //
    // Pre-Z1 the per-station override flowed into StructuralCheck +
    // ProofTestAnalysis ONLY — the thermal solver and voxel builder kept
    // reading inp.Channels.GasSideWallThickness_mm (uniform), so the
    // override was a silent no-op for T_wg / mass / STL geometry. These
    // tests pin the closed-loop fix.

    [Fact]
    public void FindThroatStationIndex_ContourOverload_AgreesWithSolverOverload()
    {
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: 25.0,
            contractionRatio: 5.5,
            expansionRatio:   25.0,
            characteristicLength_m: 1.10,
            thetaN_deg: 35.0,
            thetaE_deg: 12.0,
            bellLengthFraction: 0.80,
            stationCount: 80);
        int idxFromContour = StructuralCheck.FindThroatStationIndex(contour);
        // Sanity: the contour-overload landed on the actual minimum-R
        // station (i.e., the convention matches what solver-driven code
        // expects).
        double minR = double.MaxValue;
        int expected = -1;
        for (int i = 0; i < contour.Stations.Length; i++)
        {
            if (contour.Stations[i].R_mm < minR)
            {
                minR = contour.Stations[i].R_mm;
                expected = i;
            }
        }
        Assert.Equal(expected, idxFromContour);
    }

    [Fact]
    public void RegenSolver_ThroatWallOverride_ShiftsThroatTwg()
    {
        // Build two solver runs at the SAME conditions but with an exaggerated
        // 4× larger wall thickness at the throat (uniform 1.0 mm → 4.0 mm at
        // throat, smoothly interpolating to 1.0 mm at chamber/exit). With the
        // per-station path wired the throat T_wg should drop materially
        // (more wall = more series-resistance to coolant = lower q at
        // converged steady state, but T_wg = T_aw_eff − q/h_g rises).
        // Directional sanity: thicker wall ⇒ higher T_wg at the same h_g/h_c.
        var (uniform, override4mm) = SolveTwoCases(throatOverride_mm: 4.0);

        // Locate the throat station (min-R) in either run — same contour,
        // same index.
        int throatIdx = StructuralCheck.FindThroatStationIndex(uniform);
        double T_wg_uniform   = uniform.Stations[throatIdx].GasSideWallTemp_K;
        double T_wg_override  = override4mm.Stations[throatIdx].GasSideWallTemp_K;

        // Sanity: temps are plausible.
        Assert.True(T_wg_uniform   > 500 && T_wg_uniform   < 2500, $"baseline T_wg out of band: {T_wg_uniform}");
        Assert.True(T_wg_override  > 500 && T_wg_override  < 2500, $"override T_wg out of band: {T_wg_override}");

        // Closed-loop check: thicker wall MUST shift the converged T_wg.
        // 4× wall is large enough that the shift exceeds the 1.5 K
        // convergence tolerance; we require ≥ 5 K to filter rounding noise
        // while staying robust to alternative material conductivities.
        double delta = System.Math.Abs(T_wg_override - T_wg_uniform);
        Assert.True(delta > 5.0,
            $"Thermal solver appears to ignore ThroatWallThicknessOverride: |ΔT_wg| = {delta:F2} K "
            + $"({T_wg_uniform:F1} K uniform vs {T_wg_override:F1} K with 4mm override) — "
            + "the wallProfile is not flowing into RegenCoolingSolver.");
    }

    [Fact]
    public void RegenSolver_NullProfile_BitIdenticalToUniformBaselineArray()
    {
        // Defensive: passing a uniform array of `baseline_mm` MUST yield
        // identical T_wg to passing null (back-compat invariant).
        var (cond, contour, channels, gas, mat, mDotCool) = MakeFixture();
        var inputsNull = new RegenSolverInputs(
            Contour: contour, Gas: gas, Wall: mat, Channels: channels,
            CoolantMassFlow_kgs: mDotCool,
            CoolantInletTemp_K: cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
            CoolantFluid: MethaneFluid.Instance,
            GasSideWallProfile_mm: null);
        var uniformArray = new double[contour.Stations.Length];
        for (int i = 0; i < uniformArray.Length; i++) uniformArray[i] = channels.GasSideWallThickness_mm;
        var inputsUniform = inputsNull with { GasSideWallProfile_mm = uniformArray };

        var outNull = RegenCoolingSolver.Solve(inputsNull);
        var outUniform = RegenCoolingSolver.Solve(inputsUniform);

        Assert.Equal(outNull.PeakGasSideWallT_K, outUniform.PeakGasSideWallT_K, precision: 6);
        Assert.Equal(outNull.CoolantOutletT_K,   outUniform.CoolantOutletT_K,   precision: 6);
        Assert.Equal(outNull.CoolantPressureDrop_Pa, outUniform.CoolantPressureDrop_Pa, precision: 3);
    }

    [Fact]
    public void BuildAnalytical_ExitWallOverride_ShiftsTotalMass()
    {
        // Pure-math path (no PicoGK). With Exit override 1.0 → 4.0 mm
        // (3 mm extra wall over the divergent half of the bell), TotalMass_g
        // must increase materially. Pre-Z1 BuildAnalytical read uniform
        // ch.GasSideWallThickness_mm so the override was a silent no-op
        // even on the analytical mass path.
        var (cond, contour, channels, gas, mat, _) = MakeFixture();
        var matForMass = WallMaterials.GRCop42;

        // Profile-aware mass: exit = 4.0 mm.
        int throatIdx = StructuralCheck.FindThroatStationIndex(contour);
        double[] wallProfile = StructuralCheck.BuildGasSideWallProfile_mm(
            stationCount: contour.Stations.Length,
            throatIdx:    throatIdx,
            baseline_mm:        channels.GasSideWallThickness_mm,
            chamberOverride_mm: 0,
            throatOverride_mm:  0,
            exitOverride_mm:    4.0);

        var optsBaseline = new ChamberBuildOptions(
            Contour: contour, Channels: channels, MaterialForMass: matForMass);
        var optsThick = new ChamberBuildOptions(
            Contour: contour, Channels: channels, MaterialForMass: matForMass,
            GasSideWallProfile_mm: wallProfile);

        var massBaseline = ChamberVoxelBuilder.BuildAnalytical(optsBaseline);
        var massThick    = ChamberVoxelBuilder.BuildAnalytical(optsThick);

        Assert.True(massBaseline.TotalMass_g > 0, $"baseline mass = {massBaseline.TotalMass_g}");
        Assert.True(massThick.TotalMass_g > massBaseline.TotalMass_g + 1.0,
            $"BuildAnalytical mass appears to ignore ExitWallThicknessOverride: "
            + $"baseline={massBaseline.TotalMass_g:F2}g vs thick-exit={massThick.TotalMass_g:F2}g — "
            + "the wallProfile is not flowing into BuildAnalytical.");
    }

    // Voxel STL bbox regression (third test from the Z1 plan): SKIPPED here
    // by design. PicoGK.Library can't be instantiated inside an xUnit test
    // (ADR-005); voxel-side coverage of the wallProfile flow is captured by
    // the bench-baseline refresh (Z1.4) and any regression on
    // ChamberVoxelBuilder.Build's outer-jacket / TPMS / smoothen sites
    // would surface as a mass / peak-T shift across the four composite-
    // wall canonical presets.

    // ─── Fixture helpers ──────────────────────────────────────────

    private static (OperatingConditions cond, ChamberContour contour, ChannelSchedule channels,
                    PropellantState gas, WallMaterial mat, double mDotCool)
        MakeFixture()
    {
        var cond = new OperatingConditions
        {
            Thrust_N             = 50_000,
            ChamberPressure_Pa   = 7e6,
            MixtureRatio         = 3.5,
            CoolantInletTemp_K   = 130,
            CoolantInletPressure_Pa = 14e6,
            WallMaterialIndex    = 0,                    // GRCop-42
            PropellantPair       = PropellantPair.LOX_CH4,
            BartzScalingFactor   = 1.0,
        };
        var design = new RegenChamberDesign();
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm:        derived.ThroatRadius_mm,
            contractionRatio:       design.ContractionRatio,
            expansionRatio:         design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            thetaN_deg:             design.BellEntranceAngle_deg,
            thetaE_deg:             design.BellExitAngle_deg,
            bellLengthFraction:     design.BellLengthFraction,
            stationCount:           80);
        var channels = new ChannelSchedule(
            ChannelCount:              design.ChannelCount,
            RibThickness_mm:           design.RibThickness_mm,
            GasSideWallThickness_mm:   1.0,    // uniform 1 mm baseline
            ChannelHeightAtChamber_mm: design.ChannelHeightChamber_mm,
            ChannelHeightAtThroat_mm:  design.ChannelHeightThroat_mm,
            ChannelHeightAtExit_mm:    design.ChannelHeightExit_mm);
        var mat = WallMaterials.All[cond.WallMaterialIndex];
        double mDotCool = derived.FuelMassFlow_kgs * 0.95;
        return (cond, contour, channels, gas, mat, mDotCool);
    }

    private static (RegenSolverOutputs uniform, RegenSolverOutputs throatThick)
        SolveTwoCases(double throatOverride_mm)
    {
        var (cond, contour, channels, gas, mat, mDotCool) = MakeFixture();

        // Uniform baseline: profile = null → solver uses ch.GasSideWallThickness_mm everywhere.
        var inputsBaseline = new RegenSolverInputs(
            Contour: contour, Gas: gas, Wall: mat, Channels: channels,
            CoolantMassFlow_kgs: mDotCool,
            CoolantInletTemp_K: cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
            CoolantFluid: MethaneFluid.Instance,
            GasSideWallProfile_mm: null);

        // Throat-thickened: chamber + exit at baseline, throat = override.
        int throatIdx = StructuralCheck.FindThroatStationIndex(contour);
        double[] wallProfile = StructuralCheck.BuildGasSideWallProfile_mm(
            stationCount: contour.Stations.Length,
            throatIdx:    throatIdx,
            baseline_mm:  channels.GasSideWallThickness_mm,
            chamberOverride_mm: 0,
            throatOverride_mm:  throatOverride_mm,
            exitOverride_mm:    0);
        var inputsThick = inputsBaseline with { GasSideWallProfile_mm = wallProfile };

        var uniformOut = RegenCoolingSolver.Solve(inputsBaseline);
        var thickOut   = RegenCoolingSolver.Solve(inputsThick);
        return (uniformOut, thickOut);
    }
}
