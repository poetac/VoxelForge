// ElectricPropulsionFixture_VX200i.cs — Sprint EP.W4 phase 2
// acceptance.
//
// Wave-3 published-engine validation fixture for the Ad Astra Rocket
// VX-200i (the 200 kW VASIMR engine demonstrated 2009-2013 in the
// Houston Ground Test Facility; predecessor to the planned VF-200
// flight unit).
//
//   Inputs:  P_helicon=30 kW, P_icrh=170 kW, B_z=2 T, R_exit=100 mm,
//            ṁ=100 mg/s Ar.
//   Targets: Thrust ~ 5 N, Isp ~ 5000 s @ 200 kW (Chang Diaz 2009
//            published).
//
// Model produces T ≈ 4.63 N, Isp ≈ 4982 s — both within ±10 % of the
// published cluster mid-band. Per ADR-029 D4-generalised tolerance
// ladder ±25 % thrust / ±15 % Isp.
//
// Citations:
//   • Chang Diaz F.R., Squire J.P., Glover T.W., et al. (2009). "The
//     VASIMR engine: project status and recent accomplishments."
//     J. Propulsion & Power 25 / IEPC-2009-217.
//   • Bering E.A., Brukardt M., et al. (2010). "Recent improvements
//     in ionization costs and ion-cyclotron heating efficiency in the
//     VASIMR engine." AIAA-2010-6859.
//   • Squire J.P. et al. (2014). "VASIMR VX-200 performance
//     measurements and improvements." AIAA-2014-3899.

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_VX200i
{
    private const double TargetThrust_N = 5.0;          // 5 N
    private const double TargetIsp_s    = 5000.0;       // 5000 s
    private const double TargetPin_W    = 200000.0;     // 200 kW

    // ADR-029 D4-generalised tolerance contract.
    private const double ThrustToleranceFraction = 0.25;  // ±25 %
    private const double IspToleranceFraction    = 0.15;  // ±15 %
    private const double PinToleranceFraction    = 0.01;  // ±1 % (exact arithmetic)

    private static ElectricPropulsionEngineDesign Vx200iDesign() => new(
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

    private static ResistojetConditions Vx200iConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:    250000.0,                  // 250 kW bus, 50 kW margin over 200 kW total
        AmbientPressure_Pa:  0.0,                       // Vacuum / on-orbit (or chamber-pumped)
        Propellant:          Propellant.Xenon,          // VASIMR ignores inlet composition; sentinel
        InletTemperature_K:  300.0,
        InletComposition:    PropellantInletComposition.PureH2);

    [Fact]
    public void Vx200i_Thrust_WithinTwentyFivePercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Vx200iDesign(), Vx200iConditions());
        double low  = TargetThrust_N * (1.0 - ThrustToleranceFraction);
        double high = TargetThrust_N * (1.0 + ThrustToleranceFraction);
        Assert.InRange(result.Thrust_N, low, high);
    }

    [Fact]
    public void Vx200i_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Vx200iDesign(), Vx200iConditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void Vx200i_PlasmaState_IsVasimr_NotNull()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Vx200iDesign(), Vx200iConditions());
        Assert.NotNull(result.PlasmaState);
        Assert.IsType<VasimrPlasmaState>(result.PlasmaState);
    }

    [Fact]
    public void Vx200i_IsFeasible_AtBaseline()
    {
        // VX-200i baseline sits in band for all 3 hard gates (P_total
        // < bus, B_z in [0.3, 6] T, mirror ratio > 1). Advisory gates
        // may fire — don't block feasibility.
        var result = ElectricPropulsionOptimization.GenerateWith(Vx200iDesign(), Vx200iConditions());
        Assert.True(result.IsFeasible,
            "VX-200i baseline should pass all hard VASIMR gates "
          + "(P_total=200 kW < 250 kW bus; B_z=2 T in [0.3, 6] T; "
          + "M=3.0 > 1.0).");
    }

    [Fact]
    public void Vx200i_IonisationFractionRecordedOnPlasmaState()
    {
        // η_i ≈ 0.95 at VX-200i baseline.
        var result = ElectricPropulsionOptimization.GenerateWith(Vx200iDesign(), Vx200iConditions());
        var plasma = Assert.IsType<VasimrPlasmaState>(result.PlasmaState);
        Assert.InRange(plasma.IonisationFraction, 0.85, 1.0);
    }

    [Fact]
    public void Vx200i_NozzleConversionRecordedOnPlasmaState()
    {
        // η_nozzle = 1 - 1/M = 1 - 1/3 ≈ 0.667 at VX-200i baseline.
        var result = ElectricPropulsionOptimization.GenerateWith(Vx200iDesign(), Vx200iConditions());
        var plasma = Assert.IsType<VasimrPlasmaState>(result.PlasmaState);
        Assert.InRange(plasma.NozzleConversionEfficiency, 0.60, 0.75);
    }

    [Fact]
    public void Vx200i_ThrustEfficiency_InClusterBand()
    {
        // Chang Diaz 2009 reports ~60 % thrust efficiency at VX-200.
        // Model lands at η_T = ½·ṁ·v² / P_in ≈ 0.55-0.70 depending
        // on the operating point.
        var result = ElectricPropulsionOptimization.GenerateWith(Vx200iDesign(), Vx200iConditions());
        Assert.InRange(result.ThrustEfficiency, 0.50, 0.75);
    }

    [Fact]
    public void Vx200i_VariableIspRegime_HigherIcrhFractionRaisesIsp()
    {
        // Defining VASIMR property: shifting P_icrh fraction up at
        // fixed total power raises Isp (and lowers thrust).
        var baseline = ElectricPropulsionOptimization.GenerateWith(Vx200iDesign(), Vx200iConditions());

        // High-thrust mode: 100 kW helicon / 100 kW ICRH at same total
        // power. Should land at LOWER Isp than the baseline (15/85 split).
        var highThrustDesign = Vx200iDesign() with
        {
            VasimrHeliconRfPower_W = 100000.0,
            VasimrIcrhRfPower_W    = 100000.0,
        };
        var highThrust = ElectricPropulsionOptimization.GenerateWith(highThrustDesign, Vx200iConditions());

        Assert.True(baseline.IspVacuum_s > highThrust.IspVacuum_s,
            $"VX-200i baseline (15/85 helicon/ICRH) Isp {baseline.IspVacuum_s:F0} s "
          + $"should exceed high-thrust mode (50/50) Isp {highThrust.IspVacuum_s:F0} s "
          + "— the variable-Isp regime is a defining VASIMR property.");
    }

    [Fact]
    public void Vx200i_InputPower_TwoHundredKilowatts()
    {
        // V·I exact arithmetic: P_helicon + P_icrh = 30 + 170 = 200 kW.
        double pIn = Vx200iDesign().VasimrHeliconRfPower_W + Vx200iDesign().VasimrIcrhRfPower_W;
        double low  = TargetPin_W * (1.0 - PinToleranceFraction);
        double high = TargetPin_W * (1.0 + PinToleranceFraction);
        Assert.InRange(pIn, low, high);
    }
}
