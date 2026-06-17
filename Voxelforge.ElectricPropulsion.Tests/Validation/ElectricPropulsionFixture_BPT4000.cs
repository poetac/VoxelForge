// ElectricPropulsionFixture_BPT4000.cs — Sprint EP.W2.HET acceptance.
//
// Wave-2 published-engine validation fixture for the Aerojet Rocketdyne
// BPT-4000 Hall-Effect Thruster (4.5 kW class, xenon, flown on Advanced
// Extremely High Frequency military communications satellites + USAF
// AEHF + numerous geostationary commercial bus heritage).
//
//   Inputs:  V_d=300 V, I_d=15 A, B=0.02 T, R_anode=30 mm,
//            L_channel=25 mm, ṁ_xe=16 mg/s, AnodeMaterial=Graphite,
//            CathodeType=HollowCathode.
//   Targets: Thrust ~0.270 N (datasheet), Isp ~1543 s, P_d=4.5 kW.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Hall-Effect Thruster (HET) variant under ADR-036 § EP pillar
// (±20 % thrust / ±15 % Isp / ±5 % discharge-power [V_d × I_d exact]).
// Wider than the MR-501B fixture's ±10% / ±8% to absorb the Busch-discharge-
// model calibration uncertainty in K_div, η_b, η_t.
//
// Citations:
//   • Goebel, D. M. & Katz, I. (2008). "Fundamentals of Electric
//     Propulsion: Ion and Hall Thrusters." JPL Space Science Series. §3.
//   • Aerojet Rocketdyne BPT-4000 datasheet (publicly summarised in
//     Goebel & Katz §3 Table 3-1).

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_BPT4000
{
    private const double TargetThrust_N = 0.270;
    private const double TargetIsp_s    = 1543.0;
    private const double TargetPd_W     = 4500.0;

    // ADR-029 D4 tolerance contract.
    private const double ThrustToleranceFraction = 0.20;  // ±20 %
    private const double IspToleranceFraction    = 0.15;  // ±15 %
    private const double PdToleranceFraction     = 0.05;  // ±5  % (V_d × I_d is exact arithmetic)

    private static ElectricPropulsionEngineDesign Bpt4000Design() => new(
        Kind:                    ElectricPropulsionEngineKind.HallEffect,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        DischargeVoltage_V = 300.0,
        DischargeCurrent_A =  15.0,
        MagneticField_T    =   0.02,
        AnodeRadius_mm     =  30.0,
        ChannelLength_mm   =  25.0,
        XenonMassFlow_kgs  =   1.6e-5,
        AnodeMaterial      = AnodeMaterial.Graphite,
        CathodeType        = CathodeType.HollowCathode,
    };

    private static ResistojetConditions Bpt4000Conditions() => new(
        BusVoltage_V:        300.0,
        BusPower_W_avail:    5000.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.Xenon,
        InletTemperature_K:  300.0,
        // HET ignores inlet composition; PureH2 is a placeholder per
        // the same convention as HetCycleSolverTests.
        InletComposition:    PropellantInletComposition.PureH2);

    [Fact]
    public void Bpt4000_Thrust_WithinTwentyPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Bpt4000Design(), Bpt4000Conditions());
        double low  = TargetThrust_N * (1.0 - ThrustToleranceFraction);
        double high = TargetThrust_N * (1.0 + ThrustToleranceFraction);
        Assert.InRange(result.Thrust_N, low, high);
    }

    [Fact]
    public void Bpt4000_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Bpt4000Design(), Bpt4000Conditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void Bpt4000_DischargePower_MatchesVdTimesId()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Bpt4000Design(), Bpt4000Conditions());
        Assert.IsType<HetPlasmaState>(result.PlasmaState);
        var plasma = (HetPlasmaState)result.PlasmaState!;
        double low  = TargetPd_W * (1.0 - PdToleranceFraction);
        double high = TargetPd_W * (1.0 + PdToleranceFraction);
        Assert.InRange(plasma.DischargePower_W, low, high);
    }

    [Fact]
    public void Bpt4000_MassUtilization_PhysicallyReasonable()
    {
        // BPT-4000 datasheet η_m ≈ 0.92 (Goebel & Katz §3.5). Wave-2
        // model lands ~0.96 with η_t = 0.75 calibration. Accept [0.85, 1.0].
        var result = ElectricPropulsionOptimization.GenerateWith(Bpt4000Design(), Bpt4000Conditions());
        var plasma = Assert.IsType<HetPlasmaState>(result.PlasmaState);
        Assert.InRange(plasma.MassUtilization, 0.85, 1.0);
    }

    [Fact]
    public void Bpt4000_PlasmaState_IsHet_NotNull()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Bpt4000Design(), Bpt4000Conditions());
        Assert.NotNull(result.PlasmaState);
        Assert.IsType<HetPlasmaState>(result.PlasmaState);
        Assert.True(result.IsFeasible,
            "BPT-4000 baseline should pass all hard gates (graphite anode, "
          + "B-field above floor, V_d in band).");
    }
}
