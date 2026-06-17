// MarineHullFixture_REMUS600.cs — REMUS-600 AUV ground-truth fixture.
//
// REMUS-600 (Hydroid / Kongsberg) is a medium-depth survey AUV used for
// mine countermeasure, ISR, and oceanographic missions.
//
// Ground-truth data:
//   Dimensions:     L = 3.25 m, D = 0.324 m (12.75 in)   (Kongsberg datasheet)
//   Depth rating:   600 m
//   Cruise speed:   2.0 m/s (typical endurance cruise)
//   Design wall:    15 mm Al-6061 — SF ≈ 2.55 at 600 m (W-T formula, t/D = 0.0463)
//
// Physics cross-check (W-T formula: P_cr = 2E(t/D)³/(1−ν²)):
//   E = 68.9 GPa, ν = 0.330 (Al-6061)
//   (t/D)³ = (0.015/0.324)³ = 9.94e-5
//   P_cr   = 2 × 68.9e9 × 9.94e-5 / 0.891 = 15.4 MPa
//   P_hydro at 600 m = 1025 × 9.80665 × 600 = 6.03 MPa
//   SF     = 15.4 / 6.03 = 2.55  ✓ (≥ 1.5 hard gate)
//   Fineness ratio: 3.25 / 0.324 = 10.0  ✓ (within [4, 15])
//
// Tolerance bands per ADR-036 § Marine pillar (Displacement AUV row,
// widened 2026-05-17 via #755):
//   Drag:  ±40 % — see inline rationale on DragTolerance below.
//   Mass:  Min/Max sanity bounds (20.0–400.0 kg range covers thin-wall
//          + LPBF density dispersion documented in ADR-036 wetted-area
//          entry; not a single percentage tolerance — conservative
//          envelope around any plausible REMUS-600 build).
//
// Expected drag at 2.0 m/s (Hoerner §6-2 wetted-area model, Re_L ≈ 4.8e6):
//   C_f ≈ 0.00339, C_D_form ≈ 0.00357, S_wet ≈ 2.62 m²
//   F_drag ≈ 0.5 × 1025 × 4.0 × 2.62 × 0.00357 ≈ 19 N
//
// References:
//   Hoerner, S. F. (1965). Fluid-Dynamic Drag. §6.
//   Kongsberg Maritime REMUS-600 Technical Specification.

using Voxelforge.Marine;
using Xunit;

namespace Voxelforge.Marine.Tests.Validation;

public sealed class MarineHullFixture_REMUS600
{
    private static MarineDesign MakeDesign() => new(
        Kind:                MarineKind.AuvMidBody,
        Length_m:            3.25,
        Diameter_m:          0.324,
        NoseFairingFraction: 0.20,
        TailFairingFraction: 0.25,
        WallThickness_m:     0.015,   // 15 mm Al-6061 — SF ≈ 2.55 at 600 m
        MaterialIndex:       1,        // Al-6061
        DepthRating_m:       600.0);

    private static MarineConditions MakeConditions() => new(
        CruiseSpeed_ms: 2.0,
        MaxDepth_m:     600.0);

    private const double ExpectedDrag_N      = 19.0;
    // ±40 % per ADR-036 § Marine pillar (Displacement AUV row, widened
    // 2026-05-17 via #755). Hoerner §6-2 (1965) bare-cylinder
    // wetted-area model has documented ±35–40 % cluster scatter at
    // REMUS-600's Re_L ≈ 4.8×10⁶ — laminar→turbulent transition
    // position on the nose fairing, surface roughness, and appendage
    // drag (control fins, transducer pods) are not captured. Tightening
    // to ±25 % would require Holtrop-Mennen form-factor decomposition
    // or empirical cluster calibration against the Kongsberg
    // REMUS-600 datasheet anchor.
    private const double DragTolerance       = 0.40;
    private const double ExpectedBuckSfMin   = 1.5;
    private const double ExpectedHullMassMin = 20.0;   // [kg] conservative lower bound
    private const double ExpectedHullMassMax = 400.0;  // [kg] conservative upper bound

    [Fact]
    public void GenerateWith_REMUS600_IsFeasible()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.True(result.IsFeasible,
            $"Expected feasible. Violations: {string.Join(", ", result.Violations)}");
    }

    [Fact]
    public void GenerateWith_REMUS600_DragWithinToleranceBand()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.InRange(result.DragForce_N,
            ExpectedDrag_N * (1 - DragTolerance),
            ExpectedDrag_N * (1 + DragTolerance));
    }

    [Fact]
    public void GenerateWith_REMUS600_BucklingSafetyFactorExceedsFloor()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.True(result.BucklingSafetyFactor >= ExpectedBuckSfMin,
            $"SF = {result.BucklingSafetyFactor:F3} must be ≥ {ExpectedBuckSfMin}");
    }

    [Fact]
    public void GenerateWith_REMUS600_BuoyancyForceIsPositive()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.True(result.BuoyancyForce_N > 0);
    }

    [Fact]
    public void GenerateWith_REMUS600_IsPositivelyBuoyant()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.True(result.BuoyantWeight_N > 0,
            $"BuoyantWeight = {result.BuoyantWeight_N:F2} N should be positive.");
    }

    [Fact]
    public void GenerateWith_REMUS600_HullMassInReasonableRange()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.InRange(result.HullMass_kg, ExpectedHullMassMin, ExpectedHullMassMax);
    }

    [Fact]
    public void GenerateWith_REMUS600_DragCoefficient_IsInSlenderBodyRange()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.InRange(result.DragCoefficient, 0.001, 0.20);
    }

    [Fact]
    public void GenerateWith_REMUS600_IsDeterministic()
    {
        var design = MakeDesign();
        var cond   = MakeConditions();
        var r1     = MarineOptimization.GenerateWith(design, cond);
        var r2     = MarineOptimization.GenerateWith(design, cond);
        Assert.Equal(r1.DragForce_N,                 r2.DragForce_N);
        Assert.Equal(r1.BuoyancyForce_N,             r2.BuoyancyForce_N);
        Assert.Equal(r1.CriticalBucklingPressure_Pa, r2.CriticalBucklingPressure_Pa);
        Assert.Equal(r1.HullMass_kg,                 r2.HullMass_kg);
    }
}
