// ArcjetFeasibilityTests.cs — per-gate fire/no-fire + cross-kind isolation
// for the Wave-2 arcjet feasibility-gate block. Sibling to HetFeasibilityTests.

using System.Linq;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Feasibility;

public sealed class ArcjetFeasibilityTests
{
    private static ElectricPropulsionEngineDesign Mr509Design() => new(
        Kind:                    ElectricPropulsionEngineKind.Arcjet,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  3.9e-5,
        NozzleThroatRadius_mm:   0.5,
        NozzleAreaRatio:        100.0,
        HeaterChamberLength_mm:  12.0,
        HeaterChamberRadius_mm:   4.0)
    {
        ArcVoltage_V             = 100.0,
        ArcCurrent_A             =  18.0,
        ArcGap_mm                =   2.0,
        ArcjetElectrodeMaterial  = ArcjetElectrodeMaterial.Tungsten,
    };

    private static ResistojetConditions VacuumConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:   2200.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 900.0,
        InletComposition:   PropellantInletComposition.PureH2);

    private static ElectricPropulsionResult Solve(ElectricPropulsionEngineDesign d)
        => ElectricPropulsionOptimization.GenerateWith(d, VacuumConditions());

    // ── Hard gates ───────────────────────────────────────────────────────

    [Fact]
    public void Mr509Baseline_PassesAllHardGates()
    {
        var r = Solve(Mr509Design());
        Assert.True(r.IsFeasible);
        Assert.Empty(r.Violations);
    }

    [Fact]
    public void VoltageOutOfBand_BelowMin_FiresHard()
    {
        // ArcjetVoltageMin_V = 40; 30 V is below.
        var r = Solve(Mr509Design() with { ArcVoltage_V = 30.0 });
        Assert.False(r.IsFeasible);
        Assert.Contains(r.Violations, v => v.ConstraintId == "ARCJET_VOLTAGE_OUT_OF_BAND");
    }

    [Fact]
    public void VoltageOutOfBand_AboveMax_FiresHard()
    {
        // ArcjetVoltageMax_V = 400; 500 V is above. Bus power 60 kW so V·I clip
        // doesn't pre-empt the gate.
        var d = Mr509Design() with { ArcVoltage_V = 500.0, ArcCurrent_A = 5.0 };
        var cond = VacuumConditions() with { BusPower_W_avail = 6000.0 };
        var r = ElectricPropulsionOptimization.GenerateWith(d, cond);
        Assert.False(r.IsFeasible);
        Assert.Contains(r.Violations, v => v.ConstraintId == "ARCJET_VOLTAGE_OUT_OF_BAND");
    }

    [Fact]
    public void AnodeOverheat_TightenChamberWithSmallArea_FiresHard()
    {
        // Shrinking the anode wall area while keeping arc power constant pushes
        // the radiative balance temperature past the molybdenum 2890 K limit.
        var d = Mr509Design() with
        {
            ArcjetElectrodeMaterial = ArcjetElectrodeMaterial.Molybdenum,
            HeaterChamberLength_mm  = 1.0,    // reduce A_anode by 12×
            HeaterChamberRadius_mm  = 1.0,    //   and another 4× → 48× tighter
        };
        var r = Solve(d);
        Assert.False(r.IsFeasible);
        Assert.Contains(r.Violations, v => v.ConstraintId == "ARCJET_ANODE_OVERHEAT");
    }

    // ── Advisory gates ───────────────────────────────────────────────────

    [Fact]
    public void ThermalEfficiencyLow_CalibrationOverride_FiresAdvisory()
    {
        // Override η_thermal to 0.20 (below 0.25 floor).
        var d = Mr509Design() with { ArcjetThermalEfficiency = 0.20 };
        var r = Solve(d);
        Assert.Contains(r.Advisories, v => v.ConstraintId == "ARCJET_THERMAL_EFFICIENCY_LOW");
    }

    [Fact]
    public void ThermalEfficiencyLow_AtAnchor_DoesNotFire()
    {
        // Default η_thermal = 0.40 — well above 0.25 floor.
        var r = Solve(Mr509Design());
        Assert.DoesNotContain(r.Advisories, v => v.ConstraintId == "ARCJET_THERMAL_EFFICIENCY_LOW");
    }

    // ── Cross-kind isolation ─────────────────────────────────────────────

    [Fact]
    public void ResistojetBaseline_DoesNotEmitArcjetGates()
    {
        // A Resistojet design must not pick up arcjet-only gate IDs even if
        // its arcjet fields end up at default (NaN).
        var resistojet = new ElectricPropulsionEngineDesign(
            Kind:                    ElectricPropulsionEngineKind.Resistojet,
            HeaterPower_W:           870.0,
            PropellantMassFlow_kgs:  1.2e-4,
            NozzleThroatRadius_mm:   0.20,
            NozzleAreaRatio:        100.0,
            HeaterChamberLength_mm:  25.0,
            HeaterChamberRadius_mm:   6.0);
        var resistojetCond = VacuumConditions() with
        {
            InletComposition = PropellantInletComposition.Hydrazine_Shell405,
        };
        var r = ElectricPropulsionOptimization.GenerateWith(resistojet, resistojetCond);
        var allIds = r.Violations.Concat(r.Advisories).Select(v => v.ConstraintId).ToList();
        Assert.DoesNotContain(allIds, id => id.StartsWith("ARCJET_", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ArcjetBaseline_DoesNotEmitResistojetGates()
    {
        var r = Solve(Mr509Design());
        var allIds = r.Violations.Concat(r.Advisories).Select(v => v.ConstraintId).ToList();
        Assert.DoesNotContain(allIds, id => id.StartsWith("RESISTOJET_", System.StringComparison.Ordinal));
        Assert.DoesNotContain(allIds, id => id.StartsWith("HET_", System.StringComparison.Ordinal));
    }
}
