// MarineHullFixture_Bluefin21.cs — Bluefin-21-class AUV ground-truth fixture.
//
// Bluefin-21 (Bluefin Robotics / General Dynamics Mission Systems) is a
// medium-depth survey AUV used for mine countermeasure, ISR, and
// oceanographic missions. The class gained wide recognition from the
// MH370 search operation (2014).
//
// Ground-truth data:
//   Dimensions:     L = 4.93 m, D = 0.533 m   (Bluefin Robotics datasheet)
//   Depth rating:   300 m
//   Cruise speed:   1.8 m/s (typical endurance cruise at 4-kt)
//   Design wall:    18 mm Al-6061 — SF ≈ 1.98 at 300 m (W-T formula, t/D = 0.0338)
//
// Physics cross-check (W-T formula: P_cr = 2E(t/D)³/(1−ν²)):
//   E = 68.9 GPa, ν = 0.330 (Al-6061)
//   (t/D)³ = (0.018/0.533)³ = 3.86e-5
//   P_cr   = 2 × 68.9e9 × 3.86e-5 / 0.891 = 5.97 MPa
//   P_hydro at 300 m = 1025 × 9.80665 × 300 = 3.01 MPa
//   SF     = 5.97 / 3.01 = 1.98  ✓ (≥ 1.5 hard gate)
//   Fineness ratio: 4.93 / 0.533 = 9.25  ✓ (within [4, 15])
//
// Tolerance bands per ADR-036 § Marine pillar (Displacement AUV row,
// widened 2026-05-17 via #755):
//   Drag:  ±40 % — see inline rationale on DragTolerance below.
//   Mass:  Min/Max sanity bounds (50.0–1500.0 kg range covers thin-wall
//          + LPBF density dispersion documented in ADR-036 wetted-area
//          entry; not a single percentage tolerance — conservative
//          envelope around any plausible Bluefin-21 build).
//
// Expected drag at 1.8 m/s (Hoerner §6-2 wetted-area model, Re_L ≈ 6.6e6):
//   C_f ≈ 0.00322, C_D_form ≈ 0.00342, S_wet ≈ 6.4 m²
//   F_drag ≈ 0.5 × 1025 × 3.24 × 6.4 × 0.00342 ≈ 36 N
//
// References:
//   Hoerner, S. F. (1965). Fluid-Dynamic Drag. §6.
//   Bluefin Robotics Bluefin-21 Technical Specification.

using Voxelforge.Marine;
using Xunit;

namespace Voxelforge.Marine.Tests.Validation;

public sealed class MarineHullFixture_Bluefin21
{
    private static MarineDesign MakeDesign() => new(
        Kind:                MarineKind.AuvMidBody,
        Length_m:            4.93,
        Diameter_m:          0.533,
        NoseFairingFraction: 0.22,
        TailFairingFraction: 0.28,
        WallThickness_m:     0.018,   // 18 mm Al-6061 — SF ≈ 1.98 at 300 m
        MaterialIndex:       1,        // Al-6061
        DepthRating_m:       300.0);

    private static MarineConditions MakeConditions() => new(
        CruiseSpeed_ms: 1.8,
        MaxDepth_m:     300.0);

    private const double ExpectedDrag_N      = 36.0;
    // ±40 % per ADR-036 § Marine pillar (Displacement AUV row, widened
    // 2026-05-17 via #755). Hoerner §6-2 (1965) bare-cylinder
    // wetted-area model has documented ±35–40 % cluster scatter — even
    // at Bluefin-21's Re_L ≈ 6.6×10⁶ (mid-turbulent regime), the
    // appendage drag (control fins, MCM payload pods, transducers) and
    // surface-roughness sensitivity dominate the residual error.
    // Tightening to ±25 % would require Holtrop-Mennen form-factor
    // decomposition or per-fixture empirical calibration against the
    // Bluefin Robotics datasheet anchor.
    private const double DragTolerance       = 0.40;
    private const double ExpectedBuckSfMin   = 1.5;
    private const double ExpectedHullMassMin = 50.0;    // [kg] conservative lower bound
    private const double ExpectedHullMassMax = 1500.0;  // [kg] conservative upper bound

    [Fact]
    public void GenerateWith_Bluefin21_IsFeasible()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.True(result.IsFeasible,
            $"Expected feasible. Violations: {string.Join(", ", result.Violations)}");
    }

    [Fact]
    public void GenerateWith_Bluefin21_DragWithinToleranceBand()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.InRange(result.DragForce_N,
            ExpectedDrag_N * (1 - DragTolerance),
            ExpectedDrag_N * (1 + DragTolerance));
    }

    [Fact]
    public void GenerateWith_Bluefin21_BucklingSafetyFactorExceedsFloor()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.True(result.BucklingSafetyFactor >= ExpectedBuckSfMin,
            $"SF = {result.BucklingSafetyFactor:F3} must be ≥ {ExpectedBuckSfMin}");
    }

    [Fact]
    public void GenerateWith_Bluefin21_BuoyancyForceIsPositive()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.True(result.BuoyancyForce_N > 0);
    }

    [Fact]
    public void GenerateWith_Bluefin21_IsPositivelyBuoyant()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.True(result.BuoyantWeight_N > 0,
            $"BuoyantWeight = {result.BuoyantWeight_N:F2} N should be positive.");
    }

    [Fact]
    public void GenerateWith_Bluefin21_HullMassInReasonableRange()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.InRange(result.HullMass_kg, ExpectedHullMassMin, ExpectedHullMassMax);
    }

    [Fact]
    public void GenerateWith_Bluefin21_DragCoefficient_IsInSlenderBodyRange()
    {
        var result = MarineOptimization.GenerateWith(MakeDesign(), MakeConditions());
        Assert.InRange(result.DragCoefficient, 0.001, 0.20);
    }

    [Fact]
    public void GenerateWith_Bluefin21_IsDeterministic()
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
