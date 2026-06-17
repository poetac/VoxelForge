// GitFeasibilityTests.cs — Sprint EP.W2.GIT gate tests.
// Covers all 5 GIT gates (3 Hard + 2 Advisory) plus cross-kind isolation.

using System;
using System.Linq;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Feasibility;

public sealed class GitFeasibilityTests
{
    private static ElectricPropulsionEngineDesign NstarBaseline() => new(
        Kind:                    ElectricPropulsionEngineKind.GriddedIon,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        BeamVoltage_V               = 1100.0,
        BeamCurrent_A               =    1.76,
        ScreenGridRadius_mm         =  145.0,
        AccelGridGap_mm             =    0.6,
        NeutralizerCathodeCurrent_A =    1.76,
    };

    private static ResistojetConditions DefaultConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:   2500.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    private static bool HasViolation(System.Collections.Generic.IReadOnlyList<FeasibilityViolation> v, string id)
        => v.Any(x => x.ConstraintId == id);

    // ── Baseline feasibility ────────────────────────────────────────────

    [Fact]
    public void Baseline_NstarLikeDesign_IsFeasible()
    {
        var r = ElectricPropulsionOptimization.GenerateWith(NstarBaseline(), DefaultConditions());
        Assert.True(r.IsFeasible,
            $"NSTAR baseline should pass; saw {r.Violations.Count} violations: "
          + string.Join(", ", r.Violations.Select(v => v.ConstraintId)));
    }

    // ── GIT_BEAM_VOLTAGE_OUT_OF_BAND ────────────────────────────────────

    [Fact]
    public void BeamVoltageOutOfBand_LowEdge_FiresHardGate()
    {
        // 100 V is below the ADR-038 floor of 200 V.
        var bad = NstarBaseline() with { BeamVoltage_V = 100.0 };
        var r = ElectricPropulsionOptimization.GenerateWith(bad, DefaultConditions());
        Assert.False(r.IsFeasible);
        Assert.True(HasViolation(r.Violations, "GIT_BEAM_VOLTAGE_OUT_OF_BAND"));
    }

    [Fact]
    public void BeamVoltageOutOfBand_HighEdge_FiresHardGate()
    {
        // 15 000 V is above the ADR-038 ceiling of 12 000 V (covers
        // NEXIS/HiPEP + NEP-concept headroom; above that, sputtering
        // / grid impingement is the binding physics).
        var bad = NstarBaseline() with { BeamVoltage_V = 15000.0 };
        var r = ElectricPropulsionOptimization.GenerateWith(bad, DefaultConditions());
        Assert.True(HasViolation(r.Violations, "GIT_BEAM_VOLTAGE_OUT_OF_BAND"));
    }

    [Fact]
    public void BeamVoltageOutOfBand_DoesNotFire_OnHiPEPClass()
    {
        // Regression guard for ADR-038 D2 widening — 8 000 V (HiPEP /
        // NEXIS HV-GIT cluster) must sit inside the new band.
        var design = NstarBaseline() with { BeamVoltage_V = 8000.0 };
        var r = ElectricPropulsionOptimization.GenerateWith(design, DefaultConditions());
        Assert.False(HasViolation(r.Violations, "GIT_BEAM_VOLTAGE_OUT_OF_BAND"));
    }

    // ── GIT_PERVEANCE_LIMIT_EXCEEDED ────────────────────────────────────

    [Fact]
    public void PerveanceLimitExceeded_TinyGridSmallGap_FiresHardGate()
    {
        // Pick a geometry whose CL limit is way below the requested current.
        // V_b=500, r=20 mm, gap=3 mm → CL limit ≈ ~0.1 A; request 5 A.
        var bad = NstarBaseline() with
        {
            BeamVoltage_V               =  500.0,
            BeamCurrent_A               =    2.0,
            ScreenGridRadius_mm         =   20.0,
            AccelGridGap_mm             =    3.0,
            NeutralizerCathodeCurrent_A =    2.0,
        };
        var r = ElectricPropulsionOptimization.GenerateWith(bad, DefaultConditions());
        Assert.True(HasViolation(r.Violations, "GIT_PERVEANCE_LIMIT_EXCEEDED"));
    }

    // ── GIT_NEUTRALIZER_CURRENT_MISMATCH ────────────────────────────────

    [Fact]
    public void NeutralizerCurrentMismatch_LargeOffset_FiresHardGate()
    {
        // Beam current 1.76 A; neutraliser at 0.5 A (~70 % low) → fires.
        var bad = NstarBaseline() with { NeutralizerCathodeCurrent_A = 0.5 };
        var r = ElectricPropulsionOptimization.GenerateWith(bad, DefaultConditions());
        Assert.True(HasViolation(r.Violations, "GIT_NEUTRALIZER_CURRENT_MISMATCH"));
    }

    [Fact]
    public void NeutralizerCurrentMismatch_WithinTolerance_DoesNotFire()
    {
        // Beam 1.76 A; neutraliser 1.85 A → ~5 % offset, under 10 % tolerance.
        var design = NstarBaseline() with { NeutralizerCathodeCurrent_A = 1.85 };
        var r = ElectricPropulsionOptimization.GenerateWith(design, DefaultConditions());
        Assert.False(HasViolation(r.Violations, "GIT_NEUTRALIZER_CURRENT_MISMATCH"));
    }

    // ── GIT_GRID_LIFETIME_BELOW_FLOOR (advisory) ────────────────────────

    [Fact]
    public void GridLifetimeBelowFloor_TinyGapHighCurrent_FiresAdvisory()
    {
        // Pick a small gap + high current to drive proxy lifetime below 1000h.
        // K=88_000; t_life = K · d_gap / J_beam. For d=0.5mm, J=50A
        // → t_life = 88_000·0.5/50 = 880 h, below the 1000 h floor.
        // Need a perveance-feasible config: V_b large + grid big enough to
        // pass perveance. V_b=1500, r=200mm, d=0.5mm → CL > 60 A.
        var advisory = NstarBaseline() with
        {
            BeamVoltage_V               = 1500.0,
            BeamCurrent_A               =   50.0,
            ScreenGridRadius_mm         =  200.0,
            AccelGridGap_mm             =    0.5,
            NeutralizerCathodeCurrent_A =   50.0,
        };
        // Use a bigger bus to admit this hypothetical extreme.
        var bigBus = new ResistojetConditions(
            BusVoltage_V:        100.0,
            BusPower_W_avail: 100000.0,
            AmbientPressure_Pa:   0.0,
            Propellant:          Propellant.N2H4Decomposed,
            InletTemperature_K:  300.0,
            InletComposition:    PropellantInletComposition.PureH2);
        var r = ElectricPropulsionOptimization.GenerateWith(advisory, bigBus);
        Assert.True(HasViolation(r.Advisories, "GIT_GRID_LIFETIME_BELOW_FLOOR"));
    }

    // ── Cross-kind isolation ────────────────────────────────────────────

    [Fact]
    public void GitGates_DoNotFire_OnResistojetDesign()
    {
        // Build a feasible resistojet baseline; none of the GIT gates should
        // fire because the kind block doesn't run.
        var resistojet = new ElectricPropulsionEngineDesign(
            Kind:                    ElectricPropulsionEngineKind.Resistojet,
            HeaterPower_W:           870.0,
            PropellantMassFlow_kgs:  1.2e-4,
            NozzleThroatRadius_mm:   0.20,
            NozzleAreaRatio:         100.0,
            HeaterChamberLength_mm:  25.0,
            HeaterChamberRadius_mm:   6.0);
        var cond = new ResistojetConditions(
            BusVoltage_V:        28.0,
            BusPower_W_avail:    900.0,
            AmbientPressure_Pa:  0.0,
            Propellant:          Propellant.N2H4Decomposed,
            InletTemperature_K:  900.0,
            InletComposition:    PropellantInletComposition.Hydrazine_Shell405);
        var r = ElectricPropulsionOptimization.GenerateWith(resistojet, cond);
        Assert.False(HasViolation(r.Violations, "GIT_BEAM_VOLTAGE_OUT_OF_BAND"));
        Assert.False(HasViolation(r.Violations, "GIT_PERVEANCE_LIMIT_EXCEEDED"));
        Assert.False(HasViolation(r.Violations, "GIT_NEUTRALIZER_CURRENT_MISMATCH"));
        Assert.False(HasViolation(r.Advisories, "GIT_GRID_LIFETIME_BELOW_FLOOR"));
        Assert.False(HasViolation(r.Advisories, "GIT_PLUME_DIVERGENCE_EXCESSIVE"));
    }

    [Fact]
    public void Baseline_FivePlumeAdvisoryDoesNotFire_AtClusterAnchor()
    {
        // The cluster-anchor plume value (0.349 rad ≈ 20°) sits comfortably
        // below the 30° advisory limit.
        var r = ElectricPropulsionOptimization.GenerateWith(NstarBaseline(), DefaultConditions());
        Assert.False(HasViolation(r.Advisories, "GIT_PLUME_DIVERGENCE_EXCESSIVE"));
    }
}
