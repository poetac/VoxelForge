// AppliedFieldMpdFeasibilityTests.cs — Sprint EP.W3.AF gate tests.
//
// Pins:
//   • MPD_APPLIED_FIELD_OUT_OF_BAND fires when B is outside [0.05, 0.50] T.
//   • MPD_APPLIED_FIELD_OUT_OF_BAND does NOT fire when B is NaN or zero.
//   • MPD_APPLIED_FIELD_DOMINATES fires (advisory) when T_af / T_total > 0.80.
//   • Self-field MPD designs (B = NaN) trip neither new gate.

using System.Linq;
using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Feasibility;

public sealed class AppliedFieldMpdFeasibilityTests
{
    private static ElectricPropulsionEngineDesign MpdBaseline(
        double B,
        double k_af = double.NaN,
        double J = 1500.0) => new(
            Kind:                    ElectricPropulsionEngineKind.MagnetoPlasmaDynamic,
            HeaterPower_W:           double.NaN,
            PropellantMassFlow_kgs:    4.0e-5,
            NozzleThroatRadius_mm:   double.NaN,
            NozzleAreaRatio:         double.NaN,
            HeaterChamberLength_mm:  double.NaN,
            HeaterChamberRadius_mm:  double.NaN)
        {
            MpdArcCurrent_A                 = J,
            MpdCathodeRadius_mm             =   6.0,
            MpdAnodeRadius_mm               =  50.0,
            MpdChamberLength_mm             = 100.0,
            MpdCathodeMaterial              = MpdCathodeMaterial.ThoriatedTungsten,
            MpdAppliedFieldStrength_T       = B,
            MpdAppliedFieldCouplingOverride = k_af,
        };

    private static ResistojetConditions Conditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail: 300000.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void Gate_AppliedFieldBelowBand_Fires()
    {
        // 0.02 T < 0.05 T floor → MPD_APPLIED_FIELD_OUT_OF_BAND fires (hard).
        var design = MpdBaseline(B: 0.02);
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conditions());
        Assert.Contains(result.Violations,
            v => v.ConstraintId == "MPD_APPLIED_FIELD_OUT_OF_BAND");
        Assert.False(result.IsFeasible);
    }

    [Fact]
    public void Gate_AppliedFieldAboveBand_Fires()
    {
        // 0.60 T > 0.50 T ceiling → MPD_APPLIED_FIELD_OUT_OF_BAND fires (hard).
        var design = MpdBaseline(B: 0.60);
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conditions());
        Assert.Contains(result.Violations,
            v => v.ConstraintId == "MPD_APPLIED_FIELD_OUT_OF_BAND");
        Assert.False(result.IsFeasible);
    }

    [Fact]
    public void Gate_AppliedFieldInBand_DoesNotFire()
    {
        // 0.15 T sits inside [0.05, 0.50] band → no MPD_APPLIED_FIELD_OUT_OF_BAND.
        var design = MpdBaseline(B: 0.15);
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conditions());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "MPD_APPLIED_FIELD_OUT_OF_BAND");
    }

    [Fact]
    public void Gate_AppliedFieldNaN_DoesNotFireBandGate()
    {
        // NaN means "applied field disabled" — self-field MPD path. The band
        // gate must NOT fire for self-field MPD designs (Wave-2 backwards
        // compatibility invariant).
        var design = MpdBaseline(B: double.NaN);
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conditions());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "MPD_APPLIED_FIELD_OUT_OF_BAND");
        Assert.DoesNotContain(result.Advisories,
            v => v.ConstraintId == "MPD_APPLIED_FIELD_DOMINATES");
    }

    [Fact]
    public void Gate_AppliedFieldZero_DoesNotFireBandGate()
    {
        // Explicit zero must be treated identically to NaN (the design knob
        // is "0 → disabled" semantically). The band gate stays silent.
        var design = MpdBaseline(B: 0.0);
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conditions());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "MPD_APPLIED_FIELD_OUT_OF_BAND");
        Assert.DoesNotContain(result.Advisories,
            v => v.ConstraintId == "MPD_APPLIED_FIELD_DOMINATES");
    }

    [Fact]
    public void Gate_AppliedFieldDominates_FiresAdvisory_AtHighKaf()
    {
        // At k_af = 0.30 (override), B = 0.20 T, J = 1500 A, r_a = 50 mm:
        //   T_af = 0.30 · 1500 · 0.20 · 0.05 = 4.50 N
        //   T_self = 2.87e-7 · 1500² ≈ 0.646 N
        //   T_af / T_total ≈ 4.50 / 5.146 ≈ 0.875 > 0.80 ceiling.
        var design = MpdBaseline(B: 0.20, k_af: 0.30);
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conditions());
        Assert.Contains(result.Advisories,
            v => v.ConstraintId == "MPD_APPLIED_FIELD_DOMINATES");
    }

    [Fact]
    public void Gate_AppliedFieldDominates_DoesNotFire_AtCalibratedKaf()
    {
        // At k_af = 0.10 (Polk-LiLFA calibrated), B = 0.15 T, J = 1500 A:
        //   T_af = 0.10 · 1500 · 0.15 · 0.05 = 1.125 N
        //   T_self ≈ 0.646 N
        //   T_af / T_total ≈ 1.125 / 1.771 ≈ 0.635 < 0.80 ceiling.
        var design = MpdBaseline(B: 0.15, k_af: 0.10);
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conditions());
        Assert.DoesNotContain(result.Advisories,
            v => v.ConstraintId == "MPD_APPLIED_FIELD_DOMINATES");
    }

    [Fact]
    public void Gate_AppliedFieldDominates_OnlyAppliesToMpd()
    {
        // Cross-kind isolation — a Resistojet result must never carry the
        // MPD_APPLIED_FIELD_DOMINATES advisory regardless of other state.
        var resistojet = new ElectricPropulsionEngineDesign(
            Kind:                    ElectricPropulsionEngineKind.Resistojet,
            HeaterPower_W:           870.0,
            PropellantMassFlow_kgs:    1.2e-4,
            NozzleThroatRadius_mm:    0.20,
            NozzleAreaRatio:        100.0,
            HeaterChamberLength_mm:  25.0,
            HeaterChamberRadius_mm:   6.0);
        var result = ElectricPropulsionOptimization.GenerateWith(resistojet, Conditions());
        Assert.DoesNotContain(result.Advisories,
            v => v.ConstraintId == "MPD_APPLIED_FIELD_DOMINATES");
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "MPD_APPLIED_FIELD_OUT_OF_BAND");
    }

    [Fact]
    public void PlasmaState_AppliedFieldMetadata_Populated()
    {
        var design = MpdBaseline(B: 0.15, k_af: 0.10);
        var result = ElectricPropulsionOptimization.GenerateWith(design, Conditions());
        var plasma = Assert.IsType<MpdPlasmaState>(result.PlasmaState);
        Assert.Equal(0.15, plasma.AppliedFieldStrength_T, precision: 6);
        Assert.True(plasma.AppliedFieldThrust_N > 0);
        Assert.True(plasma.SelfFieldThrust_N > 0);
        Assert.Equal(plasma.SelfFieldThrust_N + plasma.AppliedFieldThrust_N,
                     result.Thrust_N, precision: 9);
    }
}
