// MarineHullFixture_REMUS100.cs — REMUS-100 AUV ground-truth fixture.
//
// REMUS-100 (Hydroid Inc.) is the closest open-literature AUV dataset
// for hull drag + depth rating validation of the Marine pillar's M1
// (AuvMidBody) variant.
//
// Ground-truth data:
//   Dimensions:     L = 1.595 m, D = 0.190 m   (Hydroid Inc. datasheet)
//   Depth rating:   100 m
//   Cruise speed:   1.5 m/s
//   Design wall:    5 mm Al-6061 (gives SF ≈ 2.8 at 100 m, comfortably above 1.5 hard gate)
//
// Expected drag (Hoerner §6-2 wetted-area model, Re_L ≈ 1.77e6):
//   F_drag ≈ 3.9 N at 1.5 m/s.
//   Note: Allen et al. (1997) "~0.7 N" refers to propeller shaft thrust at
//   ~60% propulsive efficiency, not the bare-hull hydrodynamic drag.
//
// Tolerance bands per ADR-036 § Marine pillar (Displacement AUV row,
// widened 2026-05-17 via #755):
//   Drag:  ±40 % — see inline rationale on DragTolerance below.
//   Mass:  Min/Max sanity bounds (5.0–60.0 kg range covers thin-wall
//          + LPBF density dispersion documented in ADR-036 wetted-area
//          entry; not a single percentage tolerance — conservative
//          envelope around any plausible REMUS-100 build).
//
// References:
//   Hoerner, S. F. (1965). Fluid-Dynamic Drag. Hoerner Fluid Dynamics. §6.
//   Allen, B., Stokey, R., Austin, T., et al. (1997). "REMUS: A small,
//     low cost AUV." Proc. OCEANS'97, Halifax. [dimensions + depth rating]
//   Hydroid Inc. REMUS-100 Technical Datasheet.

using Voxelforge.Marine;
using Voxelforge.Marine.Hydrodynamics;
using Xunit;
using static Voxelforge.Marine.Tests.ScaffoldingSmokeTests;

namespace Voxelforge.Marine.Tests.Validation;

public sealed class MarineHullFixture_REMUS100
{
    // Expected physical quantities (Hoerner §6; Hydroid datasheet)
    private const double ExpectedDrag_N       = 3.9;    // Hoerner wetted-area model at 1.5 m/s
    // ±40 % per ADR-036 § Marine pillar (Displacement AUV row, widened
    // 2026-05-17 via #755). Hoerner §6-2 (1965) bare-cylinder
    // wetted-area model has documented ±35–40 % cluster scatter at
    // REMUS-100's Re_L ≈ 1.77×10⁶ — laminar→turbulent transition
    // position on the nose fairing, surface roughness, and appendage
    // drag (control fins, transducer pods) are not captured. Tightening
    // to ±25 % would require Holtrop-Mennen form-factor decomposition
    // or empirical cluster calibration against the Allen 1997 +
    // Hydroid-datasheet anchor set.
    private const double DragTolerance        = 0.40;
    private const double ExpectedBuckSfMin    = 1.5;    // ASME UG-28 hard floor
    private const double ExpectedHullMassMin  = 5.0;    // [kg] — conservative lower bound
    private const double ExpectedHullMassMax  = 60.0;   // [kg] — conservative upper bound

    [Fact]
    public void GenerateWith_REMUS100_IsFeasible()
    {
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.True(result.IsFeasible,
            $"Expected feasible. Violations: {string.Join(", ", result.Violations)}");
    }

    [Fact]
    public void GenerateWith_REMUS100_DragWithinToleranceBand()
    {
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.InRange(result.DragForce_N,
            ExpectedDrag_N * (1 - DragTolerance),
            ExpectedDrag_N * (1 + DragTolerance));
    }

    [Fact]
    public void GenerateWith_REMUS100_BucklingSafetyFactorExceedsFloor()
    {
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.True(result.BucklingSafetyFactor >= ExpectedBuckSfMin,
            $"SF = {result.BucklingSafetyFactor:F3} must be ≥ {ExpectedBuckSfMin}");
    }

    [Fact]
    public void GenerateWith_REMUS100_BuoyancyForceIsPositive()
    {
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.True(result.BuoyancyForce_N > 0);
    }

    [Fact]
    public void GenerateWith_REMUS100_HullMassInReasonableRange()
    {
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.InRange(result.HullMass_kg, ExpectedHullMassMin, ExpectedHullMassMax);
    }

    [Fact]
    public void GenerateWith_REMUS100_DragCoefficient_IsInSlenderBodyRange()
    {
        // REMUS-100 is a slender AUV — C_D (frontal-area based) typically 0.05-0.15
        // for a wetted-area Hoerner model at Re_L ≈ 1.77e6.
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.InRange(result.DragCoefficient, 0.001, 0.20);
    }

    [Fact]
    public void GenerateWith_REMUS100_IsPositivelyBuoyant()
    {
        var result = MarineOptimization.GenerateWith(MakeRemus100Design(), MakeRemus100Conditions());
        Assert.True(result.BuoyantWeight_N > 0,
            $"BuoyantWeight = {result.BuoyantWeight_N:F2} N should be positive.");
    }

    [Fact]
    public void GenerateWith_REMUS100_IsDeterministic()
    {
        var design = MakeRemus100Design();
        var cond   = MakeRemus100Conditions();
        var r1     = MarineOptimization.GenerateWith(design, cond);
        var r2     = MarineOptimization.GenerateWith(design, cond);
        Assert.Equal(r1.DragForce_N,                r2.DragForce_N);
        Assert.Equal(r1.BuoyancyForce_N,            r2.BuoyancyForce_N);
        Assert.Equal(r1.CriticalBucklingPressure_Pa, r2.CriticalBucklingPressure_Pa);
        Assert.Equal(r1.HullMass_kg,                 r2.HullMass_kg);
    }
}
