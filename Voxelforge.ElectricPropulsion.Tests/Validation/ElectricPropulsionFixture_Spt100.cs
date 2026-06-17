// ElectricPropulsionFixture_Spt100.cs — Validation depth pass.
//
// Second Hall-Effect Thruster validation fixture (after BPT-4000). The
// SPT-100 (Stationary Plasma Thruster, Fakel OKB / EDB Fakel, Russia) is
// the most widely-flown HET in history — > 200 units across Russian
// Express / Loral 1300 / EuroSpace orbital station-keeping buses since
// 1982. Its cluster anchor sits at a lower power class than BPT-4000
// (1.35 kW vs 4.5 kW), exercising the Busch discharge model at the lower
// end of its calibrated envelope.
//
//   Inputs:  V_d=300 V, I_d=4.5 A, B=0.02 T, R_anode=50 mm,
//            L_channel=25 mm, ṁ_xe=5 mg/s, AnodeMaterial=Graphite,
//            CathodeType=HollowCathode.
//   Targets: Thrust ≈ 80 mN, Isp ≈ 1600 s, P_d = 1350 W.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Hall-Effect Thruster (SPT-100 production anchor) variant under
// ADR-036 § EP pillar (±20 % thrust / ±15 % Isp / ±5 % discharge-power).
// Same band as BPT-4000 — the Busch model is the same; the SPT-100 simply
// anchors at a different operating point (1.35 kW vs BPT-4000's 4.5 kW).
//
// Citations:
//   • Goebel, D. M. & Katz, I. (2008). "Fundamentals of Electric
//     Propulsion: Ion and Hall Thrusters." JPL Space Science Series.
//     §3.1 + Table 3-1 (SPT-100 cluster anchor).
//   • Kim V., Popov G., et al. (2003). "Investigation of operation and
//     characteristics of small SPT with discharge chamber walls made of
//     different ceramics." IEPC-03-0234.

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_Spt100
{
    private const double TargetThrust_N = 0.080;   // 80 mN
    private const double TargetIsp_s    = 1600.0;
    private const double TargetPd_W     = 1350.0;  // V_d · I_d

    private const double ThrustToleranceFraction = 0.20;  // ±20 %
    private const double IspToleranceFraction    = 0.15;  // ±15 %
    private const double PdToleranceFraction     = 0.05;  // ±5  %

    private static ElectricPropulsionEngineDesign Spt100Design() => new(
        Kind:                    ElectricPropulsionEngineKind.HallEffect,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        DischargeVoltage_V = 300.0,
        DischargeCurrent_A =   4.5,
        MagneticField_T    =   0.02,
        AnodeRadius_mm     =  50.0,
        ChannelLength_mm   =  25.0,
        XenonMassFlow_kgs  =   5.0e-6,
        AnodeMaterial      = AnodeMaterial.Graphite,
        CathodeType        = CathodeType.HollowCathode,
    };

    private static ResistojetConditions Spt100Conditions() => new(
        BusVoltage_V:        300.0,
        BusPower_W_avail:   2000.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.Xenon,
        InletTemperature_K:  300.0,
        InletComposition:    PropellantInletComposition.PureH2);

    [Fact]
    public void Spt100_Thrust_WithinTwentyPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Spt100Design(), Spt100Conditions());
        double low  = TargetThrust_N * (1.0 - ThrustToleranceFraction);
        double high = TargetThrust_N * (1.0 + ThrustToleranceFraction);
        Assert.InRange(result.Thrust_N, low, high);
    }

    [Fact]
    public void Spt100_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Spt100Design(), Spt100Conditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void Spt100_DischargePower_MatchesVdTimesId()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Spt100Design(), Spt100Conditions());
        var plasma = Assert.IsType<HetPlasmaState>(result.PlasmaState);
        double low  = TargetPd_W * (1.0 - PdToleranceFraction);
        double high = TargetPd_W * (1.0 + PdToleranceFraction);
        Assert.InRange(plasma.DischargePower_W, low, high);
    }

    [Fact]
    public void Spt100_PlasmaState_IsHet_AndFeasible()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Spt100Design(), Spt100Conditions());
        Assert.IsType<HetPlasmaState>(result.PlasmaState);
        Assert.True(result.IsFeasible,
            "SPT-100 baseline should pass all hard HET gates.");
    }

    [Fact]
    public void Spt100_Deterministic()
    {
        var r1 = ElectricPropulsionOptimization.GenerateWith(Spt100Design(), Spt100Conditions());
        var r2 = ElectricPropulsionOptimization.GenerateWith(Spt100Design(), Spt100Conditions());
        Assert.Equal(r1.Thrust_N,    r2.Thrust_N);
        Assert.Equal(r1.IspVacuum_s, r2.IspVacuum_s);
    }
}
