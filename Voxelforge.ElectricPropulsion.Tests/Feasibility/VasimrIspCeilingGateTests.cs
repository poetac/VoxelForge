// VasimrIspCeilingGateTests.cs — issue #79: VASIMR_ISP_CEILING_EXCEEDED
// hard gate.
//
// HeliconIcrhMagneticNozzleModel's E_per_ion (hence Isp) has no
// mathematical ceiling at very low ionisation fraction η_i — the same
// fixed ICRH power gets dumped into vanishingly few ions. Energy stays
// conserved throughout, so no energy-balance gate catches it; a
// constructed low-η_i / high-P_icrh design used to report Isp > 100 000 s
// yet stay feasible (only the advisory VASIMR_IONIZATION_FRACTION_LOW
// fired). These tests fail on the pre-fix code (no ceiling gate existed
// at all) and pass post-fix.

using Voxelforge.ElectricPropulsion.Plasma;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Feasibility;

public sealed class VasimrIspCeilingGateTests
{
    // ---- Unit-level: direct Evaluate() boundary checks -------------------

    private static ElectricPropulsionEngineDesign VasimrDesignSeed() => new(
        Kind:                    ElectricPropulsionEngineKind.Vasimr,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        VasimrHeliconRfPower_W    = 30000.0,
        VasimrIcrhRfPower_W       = 170000.0,
        VasimrSolenoidField_T     = 2.0,
        VasimrNozzleExitRadius_mm = 100.0,
        VasimrArgonMassFlow_kgs   = 1.0e-4,
    };

    private static ResistojetConditions VasimrConditionsSeed(double busPower_W = 250000.0) => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:    busPower_W,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.Xenon,
        InletTemperature_K:  300.0,
        InletComposition:    PropellantInletComposition.PureH2);

    private static ElectricPropulsionResult VasimrResult(double isp_s) => new(
        Design:                 VasimrDesignSeed(),
        Conditions:             VasimrConditionsSeed(),
        Thrust_N:               4.63,
        IspVacuum_s:            isp_s,
        ExitVelocity_ms:        isp_s * 9.80665,
        ThrustEfficiency:       0.60,
        HeaterTemp_K:           0.0,
        ChamberTemp_K:          0.0,
        ExitMachNumber:         0.0,
        ExitPressure_Pa:        0.0,
        RadiationLossFraction:  0.0,
        ChokedFlow:             true,
        Violations:             System.Array.Empty<Voxelforge.Optimization.FeasibilityViolation>(),
        IsFeasible:             true);

    [Fact]
    public void IspCeilingExceeded_FiresAboveCeiling()
    {
        var result = VasimrResult(isp_s: ElectricPropulsionFeasibility.VasimrIspCeiling_s + 1.0);
        var fr = ElectricPropulsionFeasibility.Evaluate(VasimrDesignSeed(), VasimrConditionsSeed(), result);
        Assert.Contains(fr.Hard, v => v.ConstraintId == "VASIMR_ISP_CEILING_EXCEEDED");
    }

    [Fact]
    public void IspCeilingExceeded_DoesNotFireAtExactCeiling()
    {
        // Gate uses strict >, so sitting exactly at the ceiling must pass.
        var result = VasimrResult(isp_s: ElectricPropulsionFeasibility.VasimrIspCeiling_s);
        var fr = ElectricPropulsionFeasibility.Evaluate(VasimrDesignSeed(), VasimrConditionsSeed(), result);
        Assert.DoesNotContain(fr.Hard, v => v.ConstraintId == "VASIMR_ISP_CEILING_EXCEEDED");
    }

    [Fact]
    public void IspCeilingExceeded_DoesNotFireWellBelowCeiling()
    {
        var result = VasimrResult(isp_s: 5000.0);
        var fr = ElectricPropulsionFeasibility.Evaluate(VasimrDesignSeed(), VasimrConditionsSeed(), result);
        Assert.DoesNotContain(fr.Hard, v => v.ConstraintId == "VASIMR_ISP_CEILING_EXCEEDED");
    }

    // ---- End-to-end: the actual degenerate low-η_i corner ----------------

    [Fact]
    public void LowIonisationFraction_HighIcrh_ReproducesUnrealisticIsp_AndTripsGate()
    {
        // Starved helicon (5 kW) relative to a large argon flow (500 mg/s)
        // drives η_i down to a few percent; the same 500 kW of ICRH power
        // then gets dumped into the few ions that do form, producing an
        // Isp far above any VX-200-class hardware result — reproducing
        // the "reachable only via direct/CLI/deserialised construction"
        // scenario issue #79 describes.
        var design = VasimrDesignSeed() with
        {
            VasimrHeliconRfPower_W  = 5000.0,
            VasimrIcrhRfPower_W     = 500000.0,
            VasimrArgonMassFlow_kgs = 5.0e-4,
        };
        var result = ElectricPropulsionOptimization.GenerateWith(
            design, VasimrConditionsSeed(busPower_W: 600000.0));

        var plasma = Assert.IsType<VasimrPlasmaState>(result.PlasmaState);
        Assert.True(plasma.IonisationFraction < 0.10,
            $"Test setup should reproduce a starved-helicon corner; got η_i={plasma.IonisationFraction:F3}.");
        Assert.True(result.IspVacuum_s > 10_000.0,
            $"Test setup should reproduce an unrealistic Isp; got {result.IspVacuum_s:F0} s.");

        Assert.Contains(result.Violations, v => v.ConstraintId == "VASIMR_ISP_CEILING_EXCEEDED");
        Assert.False(result.IsFeasible,
            "A design whose Isp exceeds the physical ceiling must be infeasible.");
    }

    // ---- Regression guard: VX-200i baseline is unaffected -----------------

    [Fact]
    public void Vx200iBaseline_StaysUnderIspCeiling_AndFeasible()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(VasimrDesignSeed(), VasimrConditionsSeed());
        Assert.True(result.IspVacuum_s < ElectricPropulsionFeasibility.VasimrIspCeiling_s);
        Assert.DoesNotContain(result.Violations, v => v.ConstraintId == "VASIMR_ISP_CEILING_EXCEEDED");
        Assert.True(result.IsFeasible);
    }
}
