// ElectricPropulsionFeasibilityTests.cs — Sprint E.2 acceptance tests
// for the 10-gate parallel feasibility evaluator.
//
// One per-gate violation test for each of 5 hard + 5 advisory gates,
// plus a canonical-order snapshot pin and a happy-path "all-pass" test
// that exercises a design + result tuned to clear every hard gate.

using System.Collections.Generic;
using System.Linq;

namespace Voxelforge.ElectricPropulsion.Tests.Feasibility;

public sealed class ElectricPropulsionFeasibilityTests
{
    // ---- Reusable seeds -------------------------------------------------

    private static ElectricPropulsionEngineDesign DesignSeed() => new(
        Kind:                    ElectricPropulsionEngineKind.Resistojet,
        HeaterPower_W:           870.0,
        PropellantMassFlow_kgs:  1.2e-4,
        NozzleThroatRadius_mm:   0.20,
        NozzleAreaRatio:         100.0,
        HeaterChamberLength_mm:  25.0,
        HeaterChamberRadius_mm:  6.0);

    private static ResistojetConditions ConditionsSeed() => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:    900.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K:  900.0,
        InletComposition:    PropellantInletComposition.Hydrazine_Shell405);

    /// <summary>
    /// Build a result tuple that's "feasible" — every gate threshold is
    /// satisfied. Per-test mutators flip individual fields to trigger a
    /// specific gate.
    /// </summary>
    private static ElectricPropulsionResult FeasibleResult(
        ElectricPropulsionEngineDesign? design = null,
        ResistojetConditions? conditions = null) => new(
        Design:                 design ?? DesignSeed(),
        Conditions:             conditions ?? ConditionsSeed(),
        Thrust_N:               0.36,
        IspVacuum_s:            300.0,
        ExitVelocity_ms:        2940.0,
        ThrustEfficiency:       0.70,
        HeaterTemp_K:           1300.0,
        ChamberTemp_K:          1100.0,    // below NH3 1100 K limit; safe
        ExitMachNumber:         4.5,
        ExitPressure_Pa:        50.0,
        RadiationLossFraction:  0.30,
        ChokedFlow:             true,
        Violations:             System.Array.Empty<Voxelforge.Optimization.FeasibilityViolation>(),
        IsFeasible:             true);

    private static bool ContainsId(IReadOnlyList<Voxelforge.Optimization.FeasibilityViolation> list, string id)
        => list.Any(v => v.ConstraintId == id);

    // ---- Hard gate 1 — RESISTOJET_HEATER_TEMP_EXCEEDED -----------------

    [Fact]
    public void HeaterTempExceeded_FiresAbovePlatinumLimit()
    {
        // Pt limit 2500 K. Heater = 2600 K trips.
        var result = FeasibleResult() with { HeaterTemp_K = 2600.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), result);
        Assert.True(ContainsId(fr.Hard, "RESISTOJET_HEATER_TEMP_EXCEEDED"));
    }

    [Fact]
    public void HeaterTempExceeded_DoesNotFireBelowLimit()
    {
        var result = FeasibleResult() with { HeaterTemp_K = 2400.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), result);
        Assert.False(ContainsId(fr.Hard, "RESISTOJET_HEATER_TEMP_EXCEEDED"));
    }

    [Fact]
    public void HeaterTempExceeded_UsesTungstenLimitForWReHeater()
    {
        // W-Re limit 2800 K. 2700 K is between Pt (2500) and WRe (2800);
        // should not fire if HeaterMaterial = WRe.
        var design = DesignSeed() with { HeaterMaterial = HeaterMaterial.TungstenRhenium };
        var result = FeasibleResult(design) with { HeaterTemp_K = 2700.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(design, ConditionsSeed(), result);
        Assert.False(ContainsId(fr.Hard, "RESISTOJET_HEATER_TEMP_EXCEEDED"));
    }

    // ---- Hard gate 2 — RESISTOJET_RADIATION_FRACTION_EXCESSIVE ---------

    [Fact]
    public void RadiationFractionExcessive_FiresAboveHalf()
    {
        var result = FeasibleResult() with { RadiationLossFraction = 0.55 };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), result);
        Assert.True(ContainsId(fr.Hard, "RESISTOJET_RADIATION_FRACTION_EXCESSIVE"));
    }

    // ---- Hard gate 3 — RESISTOJET_NOZZLE_UNCHOKED ---------------------

    [Fact]
    public void NozzleUnchoked_FiresWhenChokedFlowFalse()
    {
        var result = FeasibleResult() with { ChokedFlow = false };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), result);
        Assert.True(ContainsId(fr.Hard, "RESISTOJET_NOZZLE_UNCHOKED"));
    }

    // ---- Hard gate 4 — RESISTOJET_PROPELLANT_DECOMPOSITION -------------

    [Fact]
    public void PropellantDecomposition_FiresAboveHydrazineLimit()
    {
        // Hydrazine catalyst products (32% NH3, 24% N2, 44% H2) inherit
        // NH3's 1100 K limit since it's the lowest-non-trivial-fraction
        // species. Wait — pillar spec says hydrazine products limit is
        // 1400 K (further NH3 cracking). But MixtureDecompositionLimit_K
        // returns the floor of all present species, which is 1100 K
        // (NH3). So we trip at 1500 K.
        var result = FeasibleResult() with { ChamberTemp_K = 1500.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), result);
        Assert.True(ContainsId(fr.Hard, "RESISTOJET_PROPELLANT_DECOMPOSITION"));
    }

    [Fact]
    public void PropellantDecomposition_DoesNotFireBelowLimit_ForPureH2()
    {
        // Pure H2 limit is 3500 K — chamber at 2000 K should not fire.
        var conditions = ConditionsSeed() with
        {
            Propellant = Propellant.H2,
            InletComposition = PropellantInletComposition.PureH2,
        };
        var result = FeasibleResult(conditions: conditions) with { ChamberTemp_K = 2000.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), conditions, result);
        Assert.False(ContainsId(fr.Hard, "RESISTOJET_PROPELLANT_DECOMPOSITION"));
    }

    // ---- Hard gate 5 — RESISTOJET_HEAT_LEAK_EXCEEDS_INPUT --------------

    [Fact]
    public void HeatLeakExceedsInput_FiresWhenLossesAtOrAboveInput()
    {
        var result = FeasibleResult() with { RadiationLossFraction = 1.05 };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), result);
        Assert.True(ContainsId(fr.Hard, "RESISTOJET_HEAT_LEAK_EXCEEDS_INPUT"));
        // The radiation gate also fires (1.05 > 0.50); both are real
        // failures so both hard violations land.
        Assert.True(ContainsId(fr.Hard, "RESISTOJET_RADIATION_FRACTION_EXCESSIVE"));
    }

    // ---- Advisory gate 6 — RESISTOJET_AREA_RATIO_OUT_OF_BAND ------------

    [Fact]
    public void AreaRatioOutOfBand_FiresBelowLowerBound()
    {
        var design = DesignSeed() with { NozzleAreaRatio = 15.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(design, ConditionsSeed(), FeasibleResult(design));
        Assert.True(ContainsId(fr.Advisories, "RESISTOJET_AREA_RATIO_OUT_OF_BAND"));
    }

    [Fact]
    public void AreaRatioOutOfBand_FiresAboveUpperBound()
    {
        var design = DesignSeed() with { NozzleAreaRatio = 200.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(design, ConditionsSeed(), FeasibleResult(design));
        Assert.True(ContainsId(fr.Advisories, "RESISTOJET_AREA_RATIO_OUT_OF_BAND"));
    }

    // ---- Advisory gate 7 — RESISTOJET_THRUST_BELOW_MIN -----------------

    [Fact]
    public void ThrustBelowMin_FiresBelow005N()
    {
        var result = FeasibleResult() with { Thrust_N = 0.04 };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), result);
        Assert.True(ContainsId(fr.Advisories, "RESISTOJET_THRUST_BELOW_MIN"));
    }

    // ---- Advisory gate 8 — RESISTOJET_ISP_BELOW_FLOOR ------------------

    [Fact]
    public void IspBelowFloor_FiresBelow200s()
    {
        var result = FeasibleResult() with { IspVacuum_s = 180.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), result);
        Assert.True(ContainsId(fr.Advisories, "RESISTOJET_ISP_BELOW_FLOOR"));
    }

    // ---- Advisory gate 9 — RESISTOJET_EFFICIENCY_BELOW_FLOOR -----------

    [Fact]
    public void EfficiencyBelowFloor_FiresBelow065()
    {
        var result = FeasibleResult() with { ThrustEfficiency = 0.55 };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), result);
        Assert.True(ContainsId(fr.Advisories, "RESISTOJET_EFFICIENCY_BELOW_FLOOR"));
    }

    // ---- Advisory gate 10 — RESISTOJET_FROZEN_FLOW_LOSS_EXCESSIVE ------

    [Fact]
    public void FrozenFlowLossExcessive_FiresAbove2500K_WithNorH()
    {
        // With N2H4 catalyst products (NH3+N2+H2, all N or H species
        // present), T_c=2700 K trips. Note: the propellant decomposition
        // gate also fires (limit is 1100 K for NH3 mix).
        var result = FeasibleResult() with { ChamberTemp_K = 2700.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), result);
        Assert.True(ContainsId(fr.Advisories, "RESISTOJET_FROZEN_FLOW_LOSS_EXCESSIVE"));
    }

    [Fact]
    public void FrozenFlowLossExcessive_DoesNotFire_ForPureH2O()
    {
        // Pure water vapor has no N or H species → frozen flow doesn't
        // apply. T_c = 2700 K should not fire this gate (though it
        // exceeds H2O's 2700 K decomp limit, that's a different gate).
        var conditions = ConditionsSeed() with
        {
            Propellant = Propellant.H2O,
            InletComposition = PropellantInletComposition.PureH2O,
        };
        var result = FeasibleResult(conditions: conditions) with { ChamberTemp_K = 2600.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), conditions, result);
        Assert.False(ContainsId(fr.Advisories, "RESISTOJET_FROZEN_FLOW_LOSS_EXCESSIVE"));
    }

    // ---- Happy path -----------------------------------------------------

    [Fact]
    public void HappyPath_OnTunedDesign_PassesAllHardGates()
    {
        // Tuned-pass design: low chamber T (under NH3 decomp limit),
        // moderate radiation loss, supersonic choked flow, ε in band.
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), FeasibleResult());
        Assert.Empty(fr.Hard);
        // Advisories may be empty too — our seed is tuned to clear them.
        Assert.Empty(fr.Advisories);
    }

    // ---- Ordering snapshot ----------------------------------------------

    [Fact]
    public void GateOrdering_HardGatesEmitInCanonicalSequence()
    {
        // Construct a result that trips every hard gate. Verify the
        // emission order matches pillar spec §6 Table 1: HEATER_TEMP →
        // RADIATION_FRACTION → NOZZLE_UNCHOKED → PROPELLANT_DECOMPOSITION
        // → HEAT_LEAK_EXCEEDS_INPUT.
        var result = FeasibleResult() with
        {
            HeaterTemp_K           = 2700.0,
            RadiationLossFraction  = 1.05,
            ChokedFlow             = false,
            ChamberTemp_K          = 3000.0,  // exceeds NH3 limit AND frozen-flow threshold
        };
        var fr = ElectricPropulsionFeasibility.Evaluate(DesignSeed(), ConditionsSeed(), result);

        var hardIds = fr.Hard.Select(v => v.ConstraintId).ToArray();
        Assert.Equal(new[]
        {
            "RESISTOJET_HEATER_TEMP_EXCEEDED",
            "RESISTOJET_RADIATION_FRACTION_EXCESSIVE",
            "RESISTOJET_NOZZLE_UNCHOKED",
            "RESISTOJET_PROPELLANT_DECOMPOSITION",
            "RESISTOJET_HEAT_LEAK_EXCEEDS_INPUT",
        }, hardIds);
    }

    [Fact]
    public void GateOrdering_AdvisoryGatesEmitInCanonicalSequence()
    {
        // Trip every advisory gate. Verify order: AREA_RATIO →
        // THRUST → ISP → EFFICIENCY → FROZEN_FLOW.
        var design = DesignSeed() with { NozzleAreaRatio = 200.0 };
        var result = FeasibleResult(design) with
        {
            Thrust_N           = 0.01,
            IspVacuum_s        = 100.0,
            ThrustEfficiency   = 0.30,
            ChamberTemp_K      = 2700.0,
        };
        var fr = ElectricPropulsionFeasibility.Evaluate(design, ConditionsSeed(), result);

        var advisoryIds = fr.Advisories.Select(v => v.ConstraintId).ToArray();
        Assert.Equal(new[]
        {
            "RESISTOJET_AREA_RATIO_OUT_OF_BAND",
            "RESISTOJET_THRUST_BELOW_MIN",
            "RESISTOJET_ISP_BELOW_FLOOR",
            "RESISTOJET_EFFICIENCY_BELOW_FLOOR",
            "RESISTOJET_FROZEN_FLOW_LOSS_EXCESSIVE",
        }, advisoryIds);
    }

    // ---- ConstraintId completeness -------------------------------------

    [Fact]
    public void GateRegistry_ContainsAllTenExpectedConstraintIds()
    {
        // Catch refactors that drop a gate. Trips every gate, asserts
        // 5 hard + 5 advisory ConstraintIds match the pillar spec §6.
        var design = DesignSeed() with { NozzleAreaRatio = 200.0 };
        var result = FeasibleResult(design) with
        {
            HeaterTemp_K           = 2700.0,
            RadiationLossFraction  = 1.05,
            ChokedFlow             = false,
            ChamberTemp_K          = 3000.0,
            Thrust_N               = 0.01,
            IspVacuum_s            = 100.0,
            ThrustEfficiency       = 0.30,
        };
        var fr = ElectricPropulsionFeasibility.Evaluate(design, ConditionsSeed(), result);

        var allIds = fr.Hard.Concat(fr.Advisories).Select(v => v.ConstraintId).ToHashSet();
        Assert.Equal(10, allIds.Count);
        Assert.Contains("RESISTOJET_HEATER_TEMP_EXCEEDED",         allIds);
        Assert.Contains("RESISTOJET_RADIATION_FRACTION_EXCESSIVE", allIds);
        Assert.Contains("RESISTOJET_NOZZLE_UNCHOKED",              allIds);
        Assert.Contains("RESISTOJET_PROPELLANT_DECOMPOSITION",     allIds);
        Assert.Contains("RESISTOJET_HEAT_LEAK_EXCEEDS_INPUT",      allIds);
        Assert.Contains("RESISTOJET_AREA_RATIO_OUT_OF_BAND",       allIds);
        Assert.Contains("RESISTOJET_THRUST_BELOW_MIN",             allIds);
        Assert.Contains("RESISTOJET_ISP_BELOW_FLOOR",              allIds);
        Assert.Contains("RESISTOJET_EFFICIENCY_BELOW_FLOOR",       allIds);
        Assert.Contains("RESISTOJET_FROZEN_FLOW_LOSS_EXCESSIVE",   allIds);
    }
}
