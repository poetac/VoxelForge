// HetFeasibilityTests.cs — Sprint EP.W2.HET acceptance tests for the
// 6 HET feasibility gates (3 Hard + 3 Advisory). Mirrors the per-gate
// fire/no-fire pattern in ElectricPropulsionFeasibilityTests.

using System.Linq;
using Voxelforge.ElectricPropulsion.Plasma;
using Voxelforge.ElectricPropulsion.Solvers;

namespace Voxelforge.ElectricPropulsion.Tests.Feasibility;

public sealed class HetFeasibilityTests
{
    // BPT-4000 baseline (well within all gates).
    private static ElectricPropulsionEngineDesign HappyHetDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.HallEffect,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        DischargeVoltage_V = 300.0,
        DischargeCurrent_A = 15.0,
        MagneticField_T    = 0.02,
        AnodeRadius_mm     = 30.0,
        ChannelLength_mm   = 25.0,
        XenonMassFlow_kgs  = 1.6e-5,
        AnodeMaterial      = AnodeMaterial.Graphite,
        CathodeType        = CathodeType.HollowCathode,
    };

    private static ResistojetConditions VacuumConditions() => new(
        BusVoltage_V:        300.0,
        BusPower_W_avail:    5000.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.Xenon,
        InletTemperature_K:  300.0,
        InletComposition:    PropellantInletComposition.PureH2);

    private static ElectricPropulsionResult RunResult(ElectricPropulsionEngineDesign design)
    {
        var hetCycle = HetCycleSolver.Solve(design, VacuumConditions());
        var d = hetCycle.Discharge;
        return new ElectricPropulsionResult(
            Design:                 design,
            Conditions:             VacuumConditions(),
            Thrust_N:               d.Thrust_N,
            IspVacuum_s:            d.IspVacuum_s,
            ExitVelocity_ms:        d.IonExitVelocity_ms,
            ThrustEfficiency:       0.0,
            HeaterTemp_K:           double.NaN,
            ChamberTemp_K:          double.NaN,
            ExitMachNumber:         double.NaN,
            ExitPressure_Pa:        double.NaN,
            RadiationLossFraction:  double.NaN,
            ChokedFlow:             true,
            Violations:             System.Array.Empty<Voxelforge.Optimization.FeasibilityViolation>(),
            IsFeasible:             true) with
        {
            PlasmaState = hetCycle.PlasmaState,
        };
    }

    // ── Hard gates ──────────────────────────────────────────────────────

    [Fact]
    public void Happy_HetDesign_ProducesNoHardViolations()
    {
        var d = HappyHetDesign();
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.Empty(fr.Hard);
    }

    [Fact]
    public void HetDischargeVoltageOutOfBand_FiresOnLowVoltage()
    {
        // 50 V is below the ADR-038 floor of 100 V (xenon ionisation
        // unreliable below ~90–110 V per Goebel & Katz §3.4).
        var d = HappyHetDesign() with { DischargeVoltage_V = 50.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.Contains(fr.Hard, v => v.ConstraintId == "HET_DISCHARGE_VOLTAGE_OUT_OF_BAND");
    }

    [Fact]
    public void HetDischargeVoltageOutOfBand_FiresOnHighVoltage()
    {
        // 1500 V is above the ADR-038 ceiling of 1000 V. The Wave-1
        // Busch HET model is no longer the binding model in this regime;
        // channel-wall erosion is the actual constraint.
        var d = HappyHetDesign() with { DischargeVoltage_V = 1500.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.Contains(fr.Hard, v => v.ConstraintId == "HET_DISCHARGE_VOLTAGE_OUT_OF_BAND");
    }

    [Fact]
    public void HetDischargeVoltageOutOfBand_DoesNotFire_OnHiVHAcClass()
    {
        // Regression guard for ADR-038 D1 widening — 600 V (HiVHAc /
        // BHT-8000 / HERMeS cluster) must sit inside the new band.
        var d = HappyHetDesign() with { DischargeVoltage_V = 600.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.DoesNotContain(fr.Hard, v => v.ConstraintId == "HET_DISCHARGE_VOLTAGE_OUT_OF_BAND");
    }

    [Fact]
    public void HetAnodeOverheat_FiresOnTinyAnodeArea()
    {
        // Shrink the anode wall so radiation can't shed the 30% anode loss.
        var d = HappyHetDesign() with { AnodeRadius_mm = 5.0, ChannelLength_mm = 5.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.Contains(fr.Hard, v => v.ConstraintId == "HET_ANODE_OVERHEAT");
    }

    [Fact]
    public void HetAnodeOverheat_FiresLowerForBoronNitride()
    {
        // BN has a 1500 K limit; mid-size geometry still passes for graphite
        // but should fire for BN. The default geometry produces ~1561 K
        // which is above BN (1500 K) but below Graphite (2000 K).
        var bn = HappyHetDesign() with { AnodeMaterial = AnodeMaterial.BoronNitride };
        var fr = ElectricPropulsionFeasibility.Evaluate(bn, VacuumConditions(), RunResult(bn));
        Assert.Contains(fr.Hard, v => v.ConstraintId == "HET_ANODE_OVERHEAT");
    }

    [Fact]
    public void HetMagneticFieldInsufficient_FiresBelowFloor()
    {
        var d = HappyHetDesign() with { MagneticField_T = 0.001 };
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.Contains(fr.Hard, v => v.ConstraintId == "HET_MAGNETIC_FIELD_INSUFFICIENT");
    }

    // ── Advisory gates ──────────────────────────────────────────────────

    [Fact]
    public void HetPlumeDivergenceExcessive_FiresOnWeakField()
    {
        // Lower B → larger arctan(K_div / B). At B=0.005 T (the field-floor),
        // θ = arctan(0.012/0.005) = arctan(2.4) ≈ 67° > 30° → fires.
        var d = HappyHetDesign() with { MagneticField_T = 0.006 };
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.Contains(fr.Advisories, v => v.ConstraintId == "HET_PLUME_DIVERGENCE_EXCESSIVE");
    }

    [Fact]
    public void HetCathodeLifeLimit_FiresOnOverDrivenHollowCathode()
    {
        // HollowCathode rated 20 A; 1.2× = 24 A. 25 A trips advisory.
        // Stay within voltage band (<500 V) so we don't trip discharge band.
        var d = HappyHetDesign() with { DischargeCurrent_A = 25.0, DischargeVoltage_V = 200.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.Contains(fr.Advisories, v => v.ConstraintId == "HET_CATHODE_LIFE_LIMIT");
    }

    [Fact]
    public void HetCathodeLifeLimit_FiresEarlierForFilamentCathode()
    {
        // FilamentCathode rated 5 A; 1.2× = 6 A. 7 A trips advisory.
        var d = HappyHetDesign() with { CathodeType = CathodeType.FilamentCathode, DischargeCurrent_A = 7.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.Contains(fr.Advisories, v => v.ConstraintId == "HET_CATHODE_LIFE_LIMIT");
    }

    [Fact]
    public void HetMassUtilizationLow_FiresWhenDischargeVoltageTooLow()
    {
        // Post-#775 η_m = 1 − exp(−C_ion·√V_d) is V_d-only (C_ion = 0.1817,
        // BPT-4000 anchor at V_d=300 V → η_m≈0.957). Drop V_d to the
        // ADR-038 §D1 discharge-voltage band floor (100 V) to under-ionise
        // the plasma:
        //   η_m = 1 − exp(−0.1817·√100) = 1 − exp(−1.817) ≈ 0.838
        // 0.838 sits just below the 0.85 HetMassUtilizationFloor → fires
        // the advisory cleanly. V_d=100 V is inside the [100, 1000] V band
        // so HET_DISCHARGE_VOLTAGE_OUT_OF_BAND does NOT fire (no hard-gate
        // interference). See #807 for the trip-mechanism re-architecture.
        var d = HappyHetDesign() with { DischargeVoltage_V = 100.0 };
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.Contains(fr.Advisories, v => v.ConstraintId == "HET_MASS_UTILIZATION_LOW");
        Assert.DoesNotContain(fr.Hard, v => v.ConstraintId == "HET_DISCHARGE_VOLTAGE_OUT_OF_BAND");
    }

    [Fact]
    public void HetMassUtilizationLow_DoesNotFire_AtTypicalDischargeVoltage()
    {
        // Regression guard locking in the post-#775 V_d-only η_m formula
        // at the BPT-4000 anchor (V_d = 300 V):
        //   η_m = 1 − exp(−0.1817·√300) = 1 − exp(−3.147) ≈ 0.957
        // 0.957 is well above the 0.85 HetMassUtilizationFloor; advisory
        // must stay silent. Pairs with HetMassUtilizationLow_FiresWhen-
        // DischargeVoltageTooLow as a bracketing pair around the floor.
        var d = HappyHetDesign();
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.DoesNotContain(fr.Advisories, v => v.ConstraintId == "HET_MASS_UTILIZATION_LOW");
    }

    [Fact]
    public void Happy_HetDesign_ProducesNoAdvisories()
    {
        var d = HappyHetDesign();
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        Assert.Empty(fr.Advisories);
    }

    // ── Cross-kind isolation ───────────────────────────────────────────

    [Fact]
    public void Resistojet_ConstraintIds_NotEmittedOnHetDesign()
    {
        var d = HappyHetDesign();
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), RunResult(d));
        var ids = fr.Hard.Select(v => v.ConstraintId).Concat(fr.Advisories.Select(v => v.ConstraintId)).ToHashSet();
        Assert.DoesNotContain("RESISTOJET_HEATER_TEMP_EXCEEDED", ids);
        Assert.DoesNotContain("RESISTOJET_NOZZLE_UNCHOKED", ids);
        Assert.DoesNotContain("RESISTOJET_PROPELLANT_DECOMPOSITION", ids);
    }

    [Fact]
    public void HetGates_NotEmittedOnResistojetDesign()
    {
        // Pure resistojet inputs shouldn't trigger any HET gate.
        var resistojet = new ElectricPropulsionEngineDesign(
            Kind:                    ElectricPropulsionEngineKind.Resistojet,
            HeaterPower_W:           870.0,
            PropellantMassFlow_kgs:  1.2e-4,
            NozzleThroatRadius_mm:   0.20,
            NozzleAreaRatio:         100.0,
            HeaterChamberLength_mm:  25.0,
            HeaterChamberRadius_mm:  6.0);
        var cond = new ResistojetConditions(
            BusVoltage_V:        28.0,
            BusPower_W_avail:    900.0,
            AmbientPressure_Pa:  0.0,
            Propellant:          Propellant.N2H4Decomposed,
            InletTemperature_K:  900.0,
            InletComposition:    PropellantInletComposition.Hydrazine_Shell405);
        var stub = new ElectricPropulsionResult(
            Design: resistojet, Conditions: cond,
            Thrust_N: 0.36, IspVacuum_s: 300.0, ExitVelocity_ms: 2940.0,
            ThrustEfficiency: 0.7, HeaterTemp_K: 1500.0, ChamberTemp_K: 1300.0,
            ExitMachNumber: 5.0, ExitPressure_Pa: 0.0,
            RadiationLossFraction: 0.3, ChokedFlow: true,
            Violations: System.Array.Empty<Voxelforge.Optimization.FeasibilityViolation>(),
            IsFeasible: true);
        var fr = ElectricPropulsionFeasibility.Evaluate(resistojet, cond, stub);
        var ids = fr.Hard.Select(v => v.ConstraintId).Concat(fr.Advisories.Select(v => v.ConstraintId)).ToHashSet();
        Assert.DoesNotContain("HET_DISCHARGE_VOLTAGE_OUT_OF_BAND", ids);
        Assert.DoesNotContain("HET_ANODE_OVERHEAT", ids);
        Assert.DoesNotContain("HET_MAGNETIC_FIELD_INSUFFICIENT", ids);
        Assert.DoesNotContain("HET_PLUME_DIVERGENCE_EXCESSIVE", ids);
        Assert.DoesNotContain("HET_CATHODE_LIFE_LIMIT", ids);
        Assert.DoesNotContain("HET_MASS_UTILIZATION_LOW", ids);
    }

    [Fact]
    public void HetEvaluation_RequiresPlasmaState_OrAnodeGateNoOps()
    {
        // A HET design with no PlasmaState on the result skips advisories
        // that depend on the cast (HET_PLUME_DIVERGENCE_EXCESSIVE,
        // HET_MASS_UTILIZATION_LOW). The hard discharge-voltage and
        // magnetic-field gates still fire from design fields.
        var d = HappyHetDesign() with { DischargeVoltage_V = 50.0, MagneticField_T = 0.001 };
        var resultNoPlasma = new ElectricPropulsionResult(
            Design: d, Conditions: VacuumConditions(),
            Thrust_N: 0.0, IspVacuum_s: 0.0, ExitVelocity_ms: 0.0,
            ThrustEfficiency: 0.0, HeaterTemp_K: double.NaN, ChamberTemp_K: double.NaN,
            ExitMachNumber: double.NaN, ExitPressure_Pa: double.NaN,
            RadiationLossFraction: double.NaN, ChokedFlow: false,
            Violations: System.Array.Empty<Voxelforge.Optimization.FeasibilityViolation>(),
            IsFeasible: false);
        var fr = ElectricPropulsionFeasibility.Evaluate(d, VacuumConditions(), resultNoPlasma);
        Assert.Contains(fr.Hard, v => v.ConstraintId == "HET_DISCHARGE_VOLTAGE_OUT_OF_BAND");
        Assert.Contains(fr.Hard, v => v.ConstraintId == "HET_MAGNETIC_FIELD_INSUFFICIENT");
        // PlasmaState-dependent gates do not fire because PlasmaState is null:
        Assert.DoesNotContain(fr.Advisories, v => v.ConstraintId == "HET_PLUME_DIVERGENCE_EXCESSIVE");
        Assert.DoesNotContain(fr.Advisories, v => v.ConstraintId == "HET_MASS_UTILIZATION_LOW");
    }
}
