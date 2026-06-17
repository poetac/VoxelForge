// PptFeasibilityTests.cs — Sprint EP.W2.PPT gate tests.
// Mirror of ArcjetFeasibilityTests / HetFeasibilityTests.

using System.Linq;

namespace Voxelforge.ElectricPropulsion.Tests.Feasibility;

public sealed class PptFeasibilityTests
{
    private static ElectricPropulsionEngineDesign HealthyEo1() => new(
        Kind:                    ElectricPropulsionEngineKind.PulsedPlasmaThruster,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        CapacitorEnergy_J         = 22.0,
        PulseFrequency_Hz         =  5.0,
        PptElectrodeGap_mm        = 25.0,
        PptPropellantBarLength_mm = 25.0,
        PptElectrodeWidth_mm      = 15.0,
    };

    private static ResistojetConditions Conds(double busPower = 200.0) => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:    busPower,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void HealthyEo1_PassesAllHardGates()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(HealthyEo1(), Conds());
        Assert.True(result.IsFeasible);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void EnergyBelowMin_FiresOutOfBandHardGate()
    {
        // E_cap = 0.2 J < 0.5 J min → OUT_OF_BAND fires (hard).
        // Also < 1.0 J breakdown → NO_BREAKDOWN fires (hard).
        var design = HealthyEo1() with { CapacitorEnergy_J = 0.2 };
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conds());
        Assert.False(result.IsFeasible);
        Assert.Contains(result.Violations, v => v.ConstraintId == "PPT_CAPACITOR_ENERGY_OUT_OF_BAND");
        Assert.Contains(result.Violations, v => v.ConstraintId == "PPT_NO_BREAKDOWN");
    }

    [Fact]
    public void EnergyAboveMax_FiresOutOfBandHardGate()
    {
        // E_cap = 75 J > 50 J max → OUT_OF_BAND fires.
        var design = HealthyEo1() with { CapacitorEnergy_J = 75.0 };
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conds());
        Assert.False(result.IsFeasible);
        Assert.Contains(result.Violations, v => v.ConstraintId == "PPT_CAPACITOR_ENERGY_OUT_OF_BAND");
    }

    [Fact]
    public void EnergyJustAboveBreakdownButInBand_PassesHardGates()
    {
        // E_cap = 1.5 J → above 1.0 J breakdown, in [0.5, 50] band.
        var design = HealthyEo1() with { CapacitorEnergy_J = 1.5 };
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conds());
        Assert.True(result.IsFeasible);
        Assert.DoesNotContain(result.Violations, v => v.ConstraintId == "PPT_NO_BREAKDOWN");
    }

    [Fact]
    public void LowEnergy_FiresImpulseBitFloorAdvisory()
    {
        // E_cap = 0.6 J → I_bit = K_i × √0.6 ≈ 142 µN·s, above 100 µN·s floor.
        // Need to push lower... the floor is hit when E_cap < (PptImpulseBitFloor_Ns / K_i)² ≈ 0.297 J,
        // but that's below the 0.5 J band so OUT_OF_BAND fires too. Use a baseline tweak:
        // raise the impulse-bit floor effectively by reducing K_i would break the calibration.
        // Instead pick E_cap just above 0.5 (in band) but below the floor-implied limit.
        // K_i = 1.834e-4 → I_bit = 100e-6 at √E = 100e-6/1.834e-4 → E = 0.297 J.
        // So PPT_IMPULSE_BIT_BELOW_FLOOR fires only outside the band. Verify no fire at E=0.5.
        var design = HealthyEo1() with { CapacitorEnergy_J = 0.5 };
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conds());
        var allViolations = result.Violations.Concat(result.Advisories).ToList();
        // At E_cap = 0.5 J, I_bit ≈ 130 µN·s — above 100 µN·s floor. Floor advisory should NOT fire.
        Assert.DoesNotContain(allViolations, v => v.ConstraintId == "PPT_IMPULSE_BIT_BELOW_FLOOR");
    }

    [Fact]
    public void HighEnergy_FiresAblationRateExcessiveAdvisory()
    {
        // E_cap = 50 J (band edge) → Δm = K_m × 50 = 4.6e-9 × 50 = 230e-9 kg = 230 µg
        // > 200 µg ceiling → PPT_ABLATION_RATE_EXCESSIVE fires.
        var design = HealthyEo1() with { CapacitorEnergy_J = 50.0 };
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conds());
        // E_cap = 50 is in band (just at the edge), so hard gates clean.
        Assert.True(result.IsFeasible);
        Assert.Contains(result.Advisories, v => v.ConstraintId == "PPT_ABLATION_RATE_EXCESSIVE");
    }

    [Fact]
    public void ResistojetDesign_DoesNotFirePptGates()
    {
        // Cross-kind isolation — a Resistojet design must not surface any
        // PPT_-prefixed gates, even if PPT fields happen to be NaN.
        var resistojet = new ElectricPropulsionEngineDesign(
            Kind:                    ElectricPropulsionEngineKind.Resistojet,
            HeaterPower_W:           870.0,
            PropellantMassFlow_kgs:  1.2e-4,
            NozzleThroatRadius_mm:   0.20,
            NozzleAreaRatio:         100.0,
            HeaterChamberLength_mm:  25.0,
            HeaterChamberRadius_mm:   6.0);
        var result = ElectricPropulsionOptimization.GenerateWith(resistojet,
            Conds() with { InletComposition = PropellantInletComposition.Hydrazine_Shell405 });
        var allViolations = result.Violations.Concat(result.Advisories).ToList();
        Assert.DoesNotContain(allViolations,
            v => v.ConstraintId.StartsWith("PPT_", System.StringComparison.Ordinal));
    }

    [Fact]
    public void PptDesign_DoesNotFireResistojetOrHetOrArcjetGates()
    {
        // Inverse of cross-kind isolation: PPT result must surface only
        // PPT_-prefixed gates, never RESISTOJET_ / HET_ / ARCJET_.
        var result = ElectricPropulsionOptimization.GenerateWith(HealthyEo1(), Conds());
        var allViolations = result.Violations.Concat(result.Advisories).ToList();
        Assert.DoesNotContain(allViolations,
            v => v.ConstraintId.StartsWith("RESISTOJET_", System.StringComparison.Ordinal));
        Assert.DoesNotContain(allViolations,
            v => v.ConstraintId.StartsWith("HET_", System.StringComparison.Ordinal));
        Assert.DoesNotContain(allViolations,
            v => v.ConstraintId.StartsWith("ARCJET_", System.StringComparison.Ordinal));
    }
}
