// MarineHullFixture_PlaningYacht11m.cs — Sprint M.W3 acceptance fixture for
// the planing-hull pipeline.
//
// Validation reference: representative recreational planing yacht (LWL ≈ 10 m,
// LOA ≈ 11 m, B = 3.0 m, β = 18°, Δ = 5 000 kg) at 25 kt design cruise.
// Cluster anchor for the recreational hard-chine planing yacht class
// (Bertram & Meyer 2003 monograph; Faltinsen 2005 Hydrodynamics of High-
// Speed Marine Vehicles §4.2).
//
//   Inputs:  Length=11 m, Beam=3.0 m, β=18°, Δ=5 000 kg, LCG=0.50,
//            Freeboard=0.6 m, Speed=12.86 m/s (25 kt).
//   Targets: Trim ≈ 3.5° (cluster low edge), λ ≈ 2.0, R_total ≈ 6 700 N.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Planing-hull (Savitsky) variant under ADR-036 § Marine pillar
// (±30 % resistance, ±15 % wetted area). The ±30 % band absorbs Savitsky's
// 1960s empirical-fit residual scatter across published planing-hull data —
// the cluster anchor itself spans ~30 % across hull-form variations. ADR-036
// flags this row as THIN ("Savitsky is 1960s; modern planning-hull variance
// not quantified") — modern test data (Faltinsen 2005 §4.2; Bertram & Meyer
// 2003) supports the band but doesn't tighten it. Trim ±2° absolute (not
// fractional) per ADR-036's planing row; λ wetted-length/beam ±50 % wider
// than ADR-036's wetted-area ±15 % because λ propagates through both
// resistance and trim solvers.

using Voxelforge.Marine;
using Voxelforge.Marine.IO;
using Xunit;

namespace Voxelforge.Marine.Tests.Validation;

public sealed class MarineHullFixture_PlaningYacht11m
{
    private const double TargetResistance_N = 6700.0;
    private const double TargetTrim_deg     = 3.5;
    private const double TargetLambda       = 2.0;

    // ADR-029 D4 (generalised) tolerance contract.
    private const double ResistanceToleranceFraction = 0.30;  // ±30 %
    private const double TrimToleranceAbsolute_deg   = 2.0;   // ±2°
    private const double LambdaToleranceFraction     = 0.50;  // λ has wide cluster scatter

    private static MarineDesign PlaningYachtDesign() => new(
        Kind:                MarineKind.SurfaceHull,
        Length_m:           11.0,
        // AUV-positional fields ignored by the planing branch.
        Diameter_m:          1.0,
        NoseFairingFraction: 0.25,
        TailFairingFraction: 0.25,
        WallThickness_m:     0.005,
        MaterialIndex:       0,
        DepthRating_m:       1.0,
        HullFamily:          HullFamily.Planing)
    {
        BeamMidship_m          = 3.0,
        DeadriseAngle_deg      = 18.0,
        MassDisplacement_kg    = 5000.0,
        FreeboardHeight_m      = 0.6,
        LongitudinalCgFraction = 0.50,
    };

    private static MarineConditions PlaningConditions() => new(
        CruiseSpeed_ms: 12.86,        // 25 kt
        MaxDepth_m:      0.0);

    [Fact]
    public void PlaningYacht_TotalResistance_WithinThirtyPercent()
    {
        var result = MarineOptimization.GenerateWith(PlaningYachtDesign(), PlaningConditions());
        double low  = TargetResistance_N * (1.0 - ResistanceToleranceFraction);
        double high = TargetResistance_N * (1.0 + ResistanceToleranceFraction);
        Assert.InRange(result.DragForce_N, low, high);
    }

    [Fact]
    public void PlaningYacht_TrimAngle_WithinTwoDegrees()
    {
        var result = MarineOptimization.GenerateWith(PlaningYachtDesign(), PlaningConditions());
        Assert.InRange(result.TrimAngle_deg,
            TargetTrim_deg - TrimToleranceAbsolute_deg,
            TargetTrim_deg + TrimToleranceAbsolute_deg);
    }

    [Fact]
    public void PlaningYacht_WettedLengthToBeam_WithinFiftyPercent()
    {
        var result = MarineOptimization.GenerateWith(PlaningYachtDesign(), PlaningConditions());
        Assert.InRange(result.WettedLengthToBeamRatio,
            TargetLambda * (1.0 - LambdaToleranceFraction),
            TargetLambda * (1.0 + LambdaToleranceFraction));
    }

    [Fact]
    public void PlaningYacht_SpeedCoefficientPositive()
    {
        var result = MarineOptimization.GenerateWith(PlaningYachtDesign(), PlaningConditions());
        Assert.True(result.SpeedCoefficient > 0,
            $"Expected positive C_v; got {result.SpeedCoefficient}");
    }

    [Fact]
    public void PlaningYacht_WettedSurfaceArea_PositiveAndReasonable()
    {
        var result = MarineOptimization.GenerateWith(PlaningYachtDesign(), PlaningConditions());
        // S_w = λ·b²/cos(β) for a cluster yacht should land in the 5–50 m² band.
        Assert.InRange(result.WettedSurfaceArea_m2, 5.0, 50.0);
    }

    [Fact]
    public void PlaningYacht_AuvFieldsAreNaN()
    {
        // SurfaceHull leaves the AUV-specific result fields at NaN.
        var result = MarineOptimization.GenerateWith(PlaningYachtDesign(), PlaningConditions());
        Assert.True(double.IsNaN(result.BuoyancyForce_N));
        Assert.True(double.IsNaN(result.DisplacedVolume_m3));
        Assert.True(double.IsNaN(result.BuoyantWeight_N));
        Assert.True(double.IsNaN(result.CriticalBucklingPressure_Pa));
        Assert.True(double.IsNaN(result.BucklingSafetyFactor));
    }

    [Fact]
    public void PlaningYacht_HullMassEqualsMassDisplacement()
    {
        // For SurfaceHull, HullMass_kg holds the vessel mass-displacement
        // (the natural mass quantity at the surface) — no separate shell-
        // mass derivation at this fidelity.
        var result = MarineOptimization.GenerateWith(PlaningYachtDesign(), PlaningConditions());
        Assert.Equal(5000.0, result.HullMass_kg, precision: 6);
    }

    [Fact]
    public void PlaningYacht_IsFeasible()
    {
        var result = MarineOptimization.GenerateWith(PlaningYachtDesign(), PlaningConditions());
        Assert.True(result.IsFeasible,
            $"Baseline 11 m planing yacht should pass; saw {result.Violations.Count} violations.");
    }

    [Fact]
    public void PlaningYacht_Deterministic()
    {
        var r1 = MarineOptimization.GenerateWith(PlaningYachtDesign(), PlaningConditions());
        var r2 = MarineOptimization.GenerateWith(PlaningYachtDesign(), PlaningConditions());
        Assert.Equal(r1.DragForce_N,  r2.DragForce_N);
        Assert.Equal(r1.TrimAngle_deg, r2.TrimAngle_deg);
    }

    [Fact]
    public void PlaningYacht_RoundTripsThroughPersistence()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"vxf_marine_planing_fixture_{System.IO.Path.GetRandomFileName()}.json");
        try
        {
            MarineDesignPersistence.SaveJson(PlaningYachtDesign(), PlaningConditions(), path);
            var (loaded, cond) = MarineDesignPersistence.LoadJson(path);
            Assert.Equal(MarineKind.SurfaceHull, loaded.Kind);
            Assert.Equal(HullFamily.Planing, loaded.HullFamily);
            Assert.Equal(3.0, loaded.BeamMidship_m, precision: 6);
            // Re-evaluate after round-trip — must match original.
            var fresh    = MarineOptimization.GenerateWith(PlaningYachtDesign(), PlaningConditions());
            var roundtrip = MarineOptimization.GenerateWith(loaded, cond);
            Assert.Equal(fresh.DragForce_N, roundtrip.DragForce_N, precision: 6);
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
}
