// Sprint33CascadeTests.cs — Physics-correctness cascade Sprint 33.
// Pins behaviour of:
//   • PH-6 Dean-number Nu enhancement for helical channels (Dravid 1971)
//   • PH-7 Haaland friction factor with LPBF relative roughness
// Both correlations live in <see cref="HeatTransfer.CoolantCorrelations"/>;
// integration coverage exercises the wired-through path through
// <see cref="HeatTransfer.RegenCoolingSolver"/>.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class Sprint33CascadeTests
{
    // ─────────────────────────────────────────────────────────────────
    //  PH-6 — Dravid Dean-number Nu enhancement (CoolantCorrelations)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DeanMultiplier_StraightTube_ReturnsUnity()
    {
        // R_curv → ∞ (axial) → ratio = 0 → multiplier = 1.
        double m = CoolantCorrelations.DeanNumberNuMultiplier(
            hydraulicDiameter_m: 3e-3, curvatureRadius_m: double.PositiveInfinity);
        Assert.Equal(1.0, m, precision: 12);
    }

    [Fact]
    public void DeanMultiplier_ZeroCurvatureRadius_ReturnsUnity()
    {
        // Defensive — caller passes 0 to mean "no helix info".
        double m = CoolantCorrelations.DeanNumberNuMultiplier(
            hydraulicDiameter_m: 3e-3, curvatureRadius_m: 0.0);
        Assert.Equal(1.0, m, precision: 12);
    }

    [Fact]
    public void DeanMultiplier_TightCoil_AppliesEnhancement()
    {
        // r_wall = 50 mm at α = 25° → R_curv = r/sin²(25°) ≈ 280 mm; D_curv ≈ 560 mm.
        // D_h = 3 mm → ratio ≈ 0.00536; multiplier ≈ 1 + 3.6·0.995·√0.00536 ≈ 1.26.
        double rCurv = 0.050 / Math.Pow(Math.Sin(25.0 * Math.PI / 180.0), 2.0);
        double m = CoolantCorrelations.DeanNumberNuMultiplier(
            hydraulicDiameter_m: 3e-3, curvatureRadius_m: rCurv);
        Assert.InRange(m, 1.20, 1.32);
    }

    [Theory]
    [InlineData( 5.0)]
    [InlineData(10.0)]
    [InlineData(15.0)]
    [InlineData(25.0)]
    public void DeanMultiplier_MonotonicallyIncreasesWithPitch(double alphaDeg)
    {
        // Tighter helix (larger α) → smaller R_curv → larger multiplier.
        double rWall_m = 0.050;
        double D_h = 3e-3;
        double rPrev = rWall_m / Math.Pow(Math.Sin((alphaDeg - 1) * Math.PI / 180.0), 2.0);
        double rCur  = rWall_m / Math.Pow(Math.Sin( alphaDeg      * Math.PI / 180.0), 2.0);
        double mPrev = CoolantCorrelations.DeanNumberNuMultiplier(D_h, rPrev);
        double mCur  = CoolantCorrelations.DeanNumberNuMultiplier(D_h, rCur);
        Assert.True(mCur > mPrev,
            $"Dean multiplier should increase with α; α={alphaDeg}°: {mCur:F3} ≯ {mPrev:F3} at α-1°.");
    }

    // ─────────────────────────────────────────────────────────────────
    //  PH-7 — Haaland friction factor (CoolantCorrelations)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Haaland_ZeroRoughness_FallsBackToPetukhov()
    {
        // Smooth path must be bit-identical to the legacy one-arg form so
        // synthetic-fixture tests pinned on Petukhov literals don't shift.
        double Re = 1e5;
        double smooth = CoolantCorrelations.FrictionFactor(Re);
        double haalandZero = CoolantCorrelations.FrictionFactor(Re, relativeRoughness: 0.0);
        Assert.Equal(smooth, haalandZero, precision: 15);
    }

    [Fact]
    public void Haaland_LpbfRoughness_Predicts2To4xPetukhov()
    {
        // ε/D = 0.02 (audit centre-of-band default for LPBF channels) at
        // Re = 1e5 gives f ≈ 0.052 vs Petukhov f ≈ 0.0178 → ratio ≈ 2.9×.
        // The audit predicts 2-4× under-prediction; we pin a tighter band.
        double Re = 1e5;
        double smooth = CoolantCorrelations.FrictionFactor(Re);
        double rough  = CoolantCorrelations.FrictionFactor(Re, relativeRoughness: 0.02);
        double ratio = rough / smooth;
        Assert.InRange(ratio, 2.5, 3.5);
    }

    [Theory]
    [InlineData(0.005)]
    [InlineData(0.010)]
    [InlineData(0.020)]
    [InlineData(0.050)]
    public void Haaland_FrictionMonotonicallyIncreasesWithRoughness(double epsOverD)
    {
        double Re = 5e4;
        double fLow  = CoolantCorrelations.FrictionFactor(Re, epsOverD * 0.5);
        double fHigh = CoolantCorrelations.FrictionFactor(Re, epsOverD);
        Assert.True(fHigh > fLow,
            $"f should rise with roughness; ε/D={epsOverD}: {fHigh:F4} ≯ {fLow:F4}.");
    }

    [Fact]
    public void Haaland_RoughTubeBecomesReIndependentAtHighRe()
    {
        // Fully-rough regime: at high Re the 6.9/Re term is dwarfed by
        // (ε/3.7D)^1.11. f at Re=1e6 should be within 5 % of f at Re=1e7
        // for ε/D = 0.05 (clearly fully-rough).
        double f1 = CoolantCorrelations.FrictionFactor(1e6, 0.05);
        double f2 = CoolantCorrelations.FrictionFactor(1e7, 0.05);
        Assert.InRange(f1 / f2, 0.95, 1.05);
    }

    // ─────────────────────────────────────────────────────────────────
    //  End-to-end RegenCoolingSolver — wired-through behaviour
    // ─────────────────────────────────────────────────────────────────

    private static RegenSolverInputs BuildBaseline(
        double helixPitchDeg = 0.0, double lpbfRoughness = 0.0)
    {
        var cond = new OperatingConditions
        {
            Thrust_N = 500, ChamberPressure_Pa = 1000 * 6894.76,
            MixtureRatio = 3.3, CoolantInletTemp_K = 150,
            CoolantInletPressure_Pa = 12e6, WallMaterialIndex = 1,
            PropellantPair = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign { ChannelCount = 20, RibThickness_mm = 0.6 };
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: derived.ThroatRadius_mm,
            contractionRatio: design.ContractionRatio,
            expansionRatio: design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            stationCount: 100);
        var channels = new ChannelSchedule(
            design.ChannelCount, design.RibThickness_mm, design.GasSideWallThickness_mm,
            design.ChannelHeightChamber_mm, design.ChannelHeightThroat_mm, design.ChannelHeightExit_mm);
        var material = WallMaterials.All[cond.WallMaterialIndex];

        return new RegenSolverInputs(
            Contour: contour, Gas: gas, Wall: material, Channels: channels,
            CoolantMassFlow_kgs: derived.FuelMassFlow_kgs,
            CoolantInletTemp_K: cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
            CoolantFluid: CoolantRegistry.Get("CH4"),
            HelixPitchAngle_deg: helixPitchDeg,
            LpbfRelativeRoughness: lpbfRoughness);
    }

    [Fact]
    public void Helical_DeanEnhancement_RaisesPeakHc()
    {
        // PH-6 end-to-end: same chamber as axial baseline, with α=20° helical.
        // The Dravid Nu multiplier (~1.2-1.3 at α=20°, r ≈ 30 mm, D_h ≈ 1 mm)
        // should raise peak coolant-side h_c monotonically vs α=0°.
        var axial = RegenCoolingSolver.Solve(BuildBaseline(helixPitchDeg: 0.0));
        var helix = RegenCoolingSolver.Solve(BuildBaseline(helixPitchDeg: 20.0));

        double maxHcAxial = 0, maxHcHelix = 0;
        foreach (var s in axial.Stations) maxHcAxial = Math.Max(maxHcAxial, s.h_c_Wm2K);
        foreach (var s in helix.Stations) maxHcHelix = Math.Max(maxHcHelix, s.h_c_Wm2K);

        Assert.True(maxHcHelix > maxHcAxial * 1.05,
            $"Helical α=20° should raise peak h_c by > 5 % (Dean enhancement); axial={maxHcAxial:E2}, helix={maxHcHelix:E2}.");
    }

    [Fact]
    public void Lpbf_Roughness_RaisesCoolantPressureDrop()
    {
        // PH-7 end-to-end: ε/D=0.02 at the same geometry should raise
        // total coolant ΔP by ~2-3× vs smooth-tube. Bound conservatively
        // at > 1.5× and < 5× to allow contour-specific Reynolds
        // distribution to shift the multiplier.
        var smooth = RegenCoolingSolver.Solve(BuildBaseline(lpbfRoughness: 0.0));
        var rough  = RegenCoolingSolver.Solve(BuildBaseline(lpbfRoughness: 0.02));

        double smoothFriction = smooth.FrictionLoss_Pa;
        double roughFriction  = rough.FrictionLoss_Pa;
        Assert.True(smoothFriction > 0,
            $"Smooth-tube friction loss must be positive (got {smoothFriction:E2}).");
        double ratio = roughFriction / Math.Max(smoothFriction, 1.0);
        Assert.InRange(ratio, 1.5, 5.0);
    }

    [Fact]
    public void Sprint33_DefaultsRoughnessToZeroForRawSolverInputs()
    {
        // Defensive: synthetic test fixtures that build RegenSolverInputs
        // directly without going through RegenChamberDesign should NOT
        // pick up the LPBF default; the design's 0.02 default is
        // applied at the optimization-entrypoint wire-up only.
        var inp = BuildBaseline();
        Assert.Equal(0.0, inp.LpbfRelativeRoughness, precision: 15);
        Assert.Equal(0.0, inp.HelixPitchAngle_deg, precision: 15);
    }

    [Fact]
    public void Sprint33_DesignDefaultsLpbfRoughnessToTwoPercent()
    {
        // PH-7 calibration: 0.02 (centre of LPBF band per Strauss et al. 2018).
        var design = new RegenChamberDesign();
        Assert.Equal(0.02, design.LpbfRelativeRoughness, precision: 12);
    }
}
