// AerostructuresFixture_Cessna172WingSpar.cs — Sprint A.75 Phase 3
// published-anchor cluster-validation fixture for the Aerostructures
// pillar.
//
// Anchors the Wave-1 cantilevered-Euler-Bernoulli wing-spar model to
// the **Cessna 172 Skyhawk wing spar** — the canonical light general-
// aviation aircraft wing-spar cluster (Wave-1 WingSparDesign.cs header
// anchor; FAR Part 23 normal-category certification basis; Cessna 172
// Pilot's Operating Handbook + Type Certificate Data Sheet 3A12; Niu
// 1988 "Airframe Structural Design" §6 wing-spar primary-structure).
//
// Cluster anchors:
//   - HalfSpan ≈ 5.5 m (full span 11.0 m wing)
//   - MTOW ≈ 1100 kg (Cessna 172N normal-category gross weight)
//   - Load factor n = 3.8 g (FAR Part 23 normal-category limit)
//   - Wing area ≈ 16 m² (single-rectangular plan-form approximation)
//   - Spar topology: HollowRectangularBox (extruded-aluminum box spar,
//     classic Cessna 172 design)
//   - Material: Aluminum 7075-T6 (σ_y = 503 MPa; cluster-mid for GA
//     wing-spar primary structure)
//   - Operating-point lift: 981 N/m at 1g (= MTOW × g / (2 · half-span)
//     for symmetric loading)
//
// Phase-3 coverage backfill on the Aerostructures pillar — Cohort 4
// continuation after A.74 Tankage. The Wave-1 Euler-Bernoulli model
// captures bending moment + stress + safety factor exactly for a
// constant-section cantilever under UDL; per-station taper + skin
// contribution to bending stiffness + semi-monocoque structure are
// deferred to AS.W2+ (AS.W2 adds elliptical-lift correction, the
// induced-drag-optimal Prandtl distribution).
//
// Per ADR-036 D3.2, each [Fact] carries a rationale comment with
// either a closed-form derivation or a cluster-anchor citation.
// The fixture's tip-deflection prediction (≈ 214 mm at 3.8g) is
// substantially larger than real Cessna 172 in-flight wing-tip
// travel because the Wave-1 model neglects skin + ribs + spar taper
// contributions to bending stiffness — documented as a Wave-1
// simplification rather than a model bug. Test bands describe what
// the Wave-1 model predicts at the design point.
//
// Q3 multi-component physics-calibration watchpoint does NOT apply
// — the lumped Euler-Bernoulli beam captures the single-stage
// bending physics exactly; there is no second-component analogue.

using Voxelforge.Aerostructures;
using Xunit;

namespace Voxelforge.Tests.Aerostructures;

public sealed class AerostructuresFixture_Cessna172WingSpar
{
    // ── Closed-form Euler-Bernoulli section fingerprints ───────────────

    [Fact]
    public void Cessna172_DesignPoint_SectionAreaMatchesClosedForm()
    {
        // A = b·h − (b−2t)·(h−2t) for hollow rectangular box. At
        // h=0.25, b=0.08, t=0.006: A = 0.02 − 0.01618 = 0.00382 m².
        var d = Cessna172WingSpar();
        var r = WingSparSolver.Solve(d);
        double outer = d.OuterWidth_m * d.OuterHeight_m;
        double inner = (d.OuterWidth_m  - 2.0 * d.WallThickness_m)
                     * (d.OuterHeight_m - 2.0 * d.WallThickness_m);
        Assert.Equal(outer - inner, r.SectionArea_m2, precision: 9);
    }

    [Fact]
    public void Cessna172_DesignPoint_SectionModulusIsSecondMomentOverHalfHeight()
    {
        // S = I_xx / c where c = h/2 (extreme-fibre distance). Closed-
        // form identity for any cross-section under pure bending.
        var d = Cessna172WingSpar();
        var r = WingSparSolver.Solve(d);
        Assert.Equal(r.SecondMomentOfArea_m4 / (d.OuterHeight_m / 2.0),
                     r.SectionModulus_m3,
                     precision: 12);
    }

    [Fact]
    public void Cessna172_DesignPoint_MaxBendingMomentMatchesClosedForm()
    {
        // M_max = n · w · L² / 2 at the root of a cantilever under UDL.
        // At n=3.8, w=981 N/m, L=5.5: M_max = 3.8 · 981 · 30.25 / 2
        // ≈ 56 381 N·m.
        var d = Cessna172WingSpar();
        var r = WingSparSolver.Solve(d);
        double expected = d.LoadFactor * d.DistributedLift_Nm
                        * d.HalfSpan_m * d.HalfSpan_m / 2.0;
        Assert.Equal(expected, r.MaximumBendingMoment_Nm, precision: 3);
    }

    [Fact]
    public void Cessna172_DesignPoint_MaxBendingStressIsMomentOverModulus()
    {
        // σ_max = M_max / S. Closed-form identity.
        var r = WingSparSolver.Solve(Cessna172WingSpar());
        Assert.Equal(r.MaximumBendingMoment_Nm / r.SectionModulus_m3,
                     r.MaximumBendingStress_Pa,
                     precision: 6);
    }

    [Fact]
    public void Cessna172_DesignPoint_TipDeflectionMatchesClosedForm()
    {
        // δ_tip = n · w · L⁴ / (8 · E · I) for a cantilever under UDL.
        // Al-7075 E = 71.7e9. At n=3.8, w=981, L=5.5, I ≈ 2.78e-5:
        // δ ≈ 3.8 · 981 · 915.06 / (8 · 71.7e9 · 2.78e-5) ≈ 0.214 m.
        var d = Cessna172WingSpar();
        var r = WingSparSolver.Solve(d);
        var matProps = SparMaterialRegistry.For(d.Material);
        double L4 = d.HalfSpan_m * d.HalfSpan_m * d.HalfSpan_m * d.HalfSpan_m;
        double expected = d.LoadFactor * d.DistributedLift_Nm * L4
                        / (8.0 * matProps.YoungsModulus_Pa * r.SecondMomentOfArea_m4);
        Assert.Equal(expected, r.TipDeflection_m, precision: 6);
    }

    [Fact]
    public void Cessna172_DesignPoint_SafetyFactorIsYieldOverMaxStress()
    {
        // SF = σ_yield / σ_max. Closed-form identity. Al-7075
        // σ_y = 503 MPa.
        var d = Cessna172WingSpar();
        var r = WingSparSolver.Solve(d);
        var matProps = SparMaterialRegistry.For(d.Material);
        Assert.Equal(matProps.YieldStrength_Pa / r.MaximumBendingStress_Pa,
                     r.SafetyFactor,
                     precision: 6);
    }

    [Fact]
    public void Cessna172_DesignPoint_SparMassMatchesClosedForm()
    {
        // m = ρ · A · L (single half-span). Al-7075 ρ = 2810 kg/m³.
        // At A=0.00382, L=5.5: m ≈ 2810 · 0.00382 · 5.5 ≈ 59 kg.
        var d = Cessna172WingSpar();
        var r = WingSparSolver.Solve(d);
        var matProps = SparMaterialRegistry.For(d.Material);
        Assert.Equal(matProps.Density_kgm3 * r.SectionArea_m2 * d.HalfSpan_m,
                     r.SparMass_kg,
                     precision: 6);
    }

    // ── Cluster-anchor band fingerprints ───────────────────────────────

    [Fact]
    public void Cessna172_DesignPoint_SafetyFactorAboveFarPart23Limit()
    {
        // FAR Part 23 normal-category requires SF ≥ 1.5 ultimate-to-limit
        // for primary structure. The Wave-1 cluster prediction at the
        // Cessna 172 design point lands SF ≈ 1.98 — comfortably above
        // the floor.
        var r = WingSparSolver.Solve(Cessna172WingSpar());
        Assert.True(r.SafetyFactor > 1.5,
            $"SF ({r.SafetyFactor:F2}) must exceed FAR Part 23 1.5 floor.");
    }

    [Fact]
    public void Cessna172_DesignPoint_MaxBendingStressBelowYield()
    {
        // σ_max must be below σ_yield at limit load. Equivalent to SF > 1.
        var d = Cessna172WingSpar();
        var r = WingSparSolver.Solve(d);
        var matProps = SparMaterialRegistry.For(d.Material);
        Assert.True(r.MaximumBendingStress_Pa < matProps.YieldStrength_Pa,
            $"σ_max ({r.MaximumBendingStress_Pa / 1e6:F1} MPa) must be < "
          + $"σ_y ({matProps.YieldStrength_Pa / 1e6:F1} MPa) for safe operation.");
    }

    [Fact]
    public void Cessna172_DesignPoint_MaxBendingStressInGaSparClusterBand()
    {
        // Aerospace GA-wing-spar cluster: σ_max ∈ [100, 400] MPa at
        // limit load (Niu 1988 §6 wing-spar primary-structure cluster).
        // Cessna 172 design point lands ~ 254 MPa.
        var r = WingSparSolver.Solve(Cessna172WingSpar());
        Assert.InRange(r.MaximumBendingStress_Pa, 100e6, 400e6);
    }

    [Fact]
    public void Cessna172_DesignPoint_SparMassInGaClusterBand()
    {
        // GA wing-spar mass per half-span cluster: 30-100 kg for the
        // Cessna 172-class (Cessna TCDS 3A12 + Niu 1988 cluster).
        // Wave-1 prediction ≈ 59 kg per half-span.
        var r = WingSparSolver.Solve(Cessna172WingSpar());
        Assert.InRange(r.SparMass_kg, 30.0, 100.0);
    }

    [Fact]
    public void Cessna172_DesignPoint_TipDeflectionPositive()
    {
        // Tip deflection must be positive (downward of static load
        // reverses sign convention; upward lift gives positive δ_tip
        // in the model's sign convention). The Wave-1 model over-
        // predicts deflection because it neglects skin + ribs + spar
        // taper — real Cessna 172 wing-tip travel is < 100 mm at 3.8 g.
        // Test asserts positivity, not the absolute magnitude.
        var r = WingSparSolver.Solve(Cessna172WingSpar());
        Assert.True(r.TipDeflection_m > 0,
            $"Tip deflection ({r.TipDeflection_m * 1000:F1} mm) must be positive.");
    }

    // ── Categorical + operating-envelope fingerprints ──────────────────

    [Fact]
    public void Cessna172_UsesAluminum7075Material()
    {
        // Al-7075-T6 is the workhorse GA wing-spar alloy (high σ_y/ρ
        // ratio, well-anchored cluster). Categorical fingerprint.
        Assert.Equal(SparMaterial.Aluminum7075, Cessna172WingSpar().Material);
    }

    [Fact]
    public void Cessna172_UsesHollowRectangularBoxSection()
    {
        // Cessna 172 uses an extruded-aluminum box spar — the dominant
        // single-spar GA-aircraft topology per Wave-1 SparSectionType
        // header. Categorical fingerprint.
        Assert.Equal(SparSectionType.HollowRectangularBox,
                     Cessna172WingSpar().SectionType);
    }

    [Fact]
    public void Cessna172_DoublingLoadFactor_DoublesBendingMoment()
    {
        // M_max ∝ n linearly at fixed w, L. Doubling load factor from
        // 3.8 to 7.6 g doubles M_max. Linear-scaling fingerprint.
        var nominal  = WingSparSolver.Solve(Cessna172WingSpar());
        var doubleLF = WingSparSolver.Solve(
            Cessna172WingSpar() with { LoadFactor = 7.6 });
        Assert.Equal(nominal.MaximumBendingMoment_Nm * 2.0,
                     doubleLF.MaximumBendingMoment_Nm,
                     precision: 3);
    }

    [Fact]
    public void Cessna172_Wave2EllipticalLift_ReducesMaxBendingMoment()
    {
        // Sprint AS.W2 elliptical-lift correction: Prandtl's optimal
        // distribution w(y) = w₀ · √(1 − (y/L)²) integrates to π/4 ·
        // w₀ · L vs uniform w · L. Total lift held constant → effective
        // moment at the root reduces by the centroid-shift factor
        // (elliptical lift centroid is closer to root than midspan).
        // The qualitative direction is M_max_elliptical < M_max_uniform.
        var uniform    = WingSparSolver.Solve(Cessna172WingSpar());
        var elliptical = WingSparSolver.Solve(
            Cessna172WingSpar() with { UseEllipticalLift = true });
        Assert.True(elliptical.MaximumBendingMoment_Nm
                  < uniform.MaximumBendingMoment_Nm,
            $"Elliptical-lift M_max ({elliptical.MaximumBendingMoment_Nm:F0} N·m) "
          + $"must be < uniform-lift M_max ({uniform.MaximumBendingMoment_Nm:F0} N·m) "
          + "at fixed total lift (centroid shift toward root).");
    }

    [Fact]
    public void Cessna172_CarbonFibreVariant_LowerMassThanAluminum()
    {
        // For identical geometry, ρ_CF (1600) < ρ_Al-7075 (2810) → CF
        // variant is ~ 43 % lighter per half-span. Cross-material parity
        // fingerprint.
        var aluminum = WingSparSolver.Solve(Cessna172WingSpar());
        var carbon   = WingSparSolver.Solve(
            Cessna172WingSpar() with { Material = SparMaterial.CarbonFibreComposite });
        Assert.True(carbon.SparMass_kg < aluminum.SparMass_kg,
            $"CF spar mass ({carbon.SparMass_kg:F1} kg) must be < "
          + $"Al spar mass ({aluminum.SparMass_kg:F1} kg) at identical geometry.");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // Cessna 172 Skyhawk wing spar — Wave-1 header cluster anchor (FAR
    // Part 23 normal-category 3.8 g; Cessna 172N Pilot's Operating
    // Handbook + Type Certificate Data Sheet 3A12; Niu 1988 Airframe
    // Structural Design §6 wing-spar cluster).
    //   - SectionType: HollowRectangularBox (extruded-Al box spar)
    //   - Material: Aluminum 7075-T6
    //   - HalfSpan: 5.5 m (full span 11.0 m)
    //   - OuterHeight: 0.25 m chord-normal
    //   - OuterWidth: 0.08 m chord-direction
    //   - WallThickness: 6 mm
    //   - DistributedLift: 981 N/m at 1 g (= 1100 kg × 9.81 / (2 × 5.5))
    //   - LoadFactor: 3.8 (FAR Part 23 normal-category limit)
    private static WingSparDesign Cessna172WingSpar() => new(
        SectionType:           SparSectionType.HollowRectangularBox,
        Material:              SparMaterial.Aluminum7075,
        HalfSpan_m:            5.5,
        OuterHeight_m:         0.25,
        OuterWidth_m:          0.08,
        WallThickness_m:       0.006,
        DistributedLift_Nm:    981.0,
        LoadFactor:            3.8);
}
