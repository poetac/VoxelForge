// ElectricPropulsionFixture_HiVHAc.cs — Validation depth pass (Sprint E
// continuation).
//
// Third Hall-Effect Thruster validation fixture (after BPT-4000 + SPT-100).
// HiVHAc (High Voltage Hall Accelerator) is the NASA Glenn 3.5 kW-class
// next-generation HET designed for variable-Isp operation. Validates the
// Busch HET model at the upper-Isp / higher-discharge-voltage end of the
// envelope vs BPT-4000's mid-range and SPT-100's low-power anchor.
//
//   Inputs:  V_d=600 V (high-Isp mode), I_d=4 A, B=0.02 T, R_anode=50 mm,
//            L_channel=30 mm, ṁ_xe=6.1 mg/s, AnodeMaterial=Graphite,
//            CathodeType=HollowCathode. [#546 fixture audit, 2026-05-18:
//            previously 8 mg/s, which is the low-Isp / 400-V mode
//            operating point. The 600 V / 4 A high-Isp design point in
//            Kamhawi 2014 IEPC-2013-444 uses ~6 mg/s, consistent with
//            the published thrust 156 mN ÷ (Isp 2600 s · g₀).]
//   Targets: Thrust ≈ 156 mN, Isp ≈ 2 600 s, P_d=2 400 W.
//
// HiVHAc's defining property: by raising V_d from the 300 V "industry
// standard" up to 600+ V, it trades thrust-per-power for Isp — the
// variable-Isp Hall capability. Voxelforge's Busch model captures the
// voltage-scaling via its discharge-power and ion-acceleration terms.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Hall-Effect Thruster (HV variant) under ADR-036 § EP pillar
// (±20 % thrust / ±15 % Isp / ±5 % discharge-power)..
//
// Citations:
//   • Kamhawi H., Haag T.W., Mathers A.D. (2014). "Investigation of a
//     High Voltage Hall Accelerator (HiVHAc) at NASA Glenn." IEPC-2013-444.
//   • Soulas G.C., Patterson M.J. (2007). "NEXT Long-Duration Test
//     Update." AIAA-2007-5274 (companion high-Isp HET reference).

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_HiVHAc
{
    private const double TargetThrust_N = 0.156;
    private const double TargetIsp_s    = 2600.0;
    private const double TargetPd_W     = 2400.0;

    private const double ThrustToleranceFraction = 0.20;
    private const double IspToleranceFraction    = 0.15;
    private const double PdToleranceFraction     = 0.05;

    private static ElectricPropulsionEngineDesign HiVHAcDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.HallEffect,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        DischargeVoltage_V = 600.0,
        DischargeCurrent_A =   4.0,
        MagneticField_T    =   0.02,
        AnodeRadius_mm     =  50.0,
        ChannelLength_mm   =  30.0,
        XenonMassFlow_kgs  =   6.1e-6,    // #546 audit: real Kamhawi 2014 600 V / 4 A point ≈ 6 mg/s, not 8.
        AnodeMaterial      = AnodeMaterial.Graphite,
        CathodeType        = CathodeType.HollowCathode,
    };

    private static ResistojetConditions HiVHAcConditions() => new(
        BusVoltage_V:        600.0,
        BusPower_W_avail:   3500.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.Xenon,
        InletTemperature_K:  300.0,
        InletComposition:    PropellantInletComposition.PureH2);

    [Fact]
    public void HiVHAc_Thrust_WithinTwentyPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(HiVHAcDesign(), HiVHAcConditions());
        double low  = TargetThrust_N * (1.0 - ThrustToleranceFraction);
        double high = TargetThrust_N * (1.0 + ThrustToleranceFraction);
        Assert.InRange(result.Thrust_N, low, high);
    }

    [Fact]
    public void HiVHAc_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(HiVHAcDesign(), HiVHAcConditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void HiVHAc_DischargePower_MatchesVdTimesId()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(HiVHAcDesign(), HiVHAcConditions());
        var plasma = Assert.IsType<HetPlasmaState>(result.PlasmaState);
        double low  = TargetPd_W * (1.0 - PdToleranceFraction);
        double high = TargetPd_W * (1.0 + PdToleranceFraction);
        Assert.InRange(plasma.DischargePower_W, low, high);
    }

    [Fact]
    public void HiVHAc_HigherIspThanBpt4000_AtHigherVoltage()
    {
        // BPT-4000 baseline 300 V → Isp 1543 s. HiVHAc 600 V should
        // produce ≥ 50 % higher Isp (sqrt(V) scaling). Pin the cross-
        // fixture invariant.
        var result = ElectricPropulsionOptimization.GenerateWith(HiVHAcDesign(), HiVHAcConditions());
        Assert.True(result.IspVacuum_s > 1543.0 * 1.50,
            $"HiVHAc Isp ({result.IspVacuum_s:F0} s) should exceed BPT-4000's "
          + "1543 s baseline by ≥ 50 % at the doubled discharge voltage.");
    }

    [Fact]
    public void HiVHAc_PlasmaState_IsHet()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(HiVHAcDesign(), HiVHAcConditions());
        Assert.IsType<HetPlasmaState>(result.PlasmaState);
    }

    [Fact]
    public void HiVHAc_WithinDischargeVoltageBand_AfterB1()
    {
        // ADR-038 D1 widened the HET discharge-voltage band to
        // [100, 1000] V (from [150, 500] V) to cover modern HV-Hall
        // thrusters. HiVHAc's 600 V V_d now sits inside the band; the
        // HET_DISCHARGE_VOLTAGE_OUT_OF_BAND gate must not fire and the
        // fixture must report IsFeasible. The renamed test is a
        // band-tightening trip-wire: if a future ADR shrinks the band
        // back below 600 V, the prior test name collides and the
        // suite breaks loudly, prompting a re-review.
        var result = ElectricPropulsionOptimization.GenerateWith(HiVHAcDesign(), HiVHAcConditions());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "HET_DISCHARGE_VOLTAGE_OUT_OF_BAND");
        Assert.True(result.IsFeasible,
            $"HiVHAc should be feasible post-ADR-038. Saw {result.Violations.Count} "
          + $"violations: {string.Join(", ", result.Violations.Select(v => v.ConstraintId))}.");
    }

    [Fact]
    public void HiVHAc_Deterministic()
    {
        var r1 = ElectricPropulsionOptimization.GenerateWith(HiVHAcDesign(), HiVHAcConditions());
        var r2 = ElectricPropulsionOptimization.GenerateWith(HiVHAcDesign(), HiVHAcConditions());
        Assert.Equal(r1.Thrust_N,    r2.Thrust_N);
        Assert.Equal(r1.IspVacuum_s, r2.IspVacuum_s);
    }
}
