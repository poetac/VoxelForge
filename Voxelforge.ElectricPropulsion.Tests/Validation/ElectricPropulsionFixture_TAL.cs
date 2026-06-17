// ElectricPropulsionFixture_TAL.cs — Validation depth pass (Sprint M).
//
// Fourth Hall-Effect Thruster validation fixture (after BPT-4000 +
// SPT-100 + HiVHAc). TAL (Thrusters with Anode Layer) is the Russian
// alternate HET topology — magnetic-field geometry is concentrated at
// the anode rather than distributed across the channel, giving a
// thinner discharge layer + different efficiency / Isp tradeoff vs the
// SPT (Stationary Plasma Thruster) family. Flown on Russian Yamal +
// Gals series satellites.
//
//   Inputs:  V_d=300 V, I_d=4.5 A, B=0.025 T (higher than SPT — 0.020),
//            R_anode=50 mm, L_channel=20 mm (shorter than SPT — 25),
//            ṁ_xe=5 mg/s, AnodeMaterial=Graphite,
//            CathodeType=HollowCathode.
//   Targets: Thrust ≈ 85 mN, Isp ≈ 1 800 s, P_d=1 350 W.
//
// TAL's defining property vs SPT: thinner discharge layer + higher
// efficiency at the same operating point. Voxelforge's Busch HET model
// doesn't explicitly distinguish TAL vs SPT topology — both are HET
// kind under the same physics dispatch — but the design-space inputs
// (higher B, shorter channel) reflect the TAL-specific anchor.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Hall-Effect Thruster (TAL topology, Russian alternate) variant
// under ADR-036 § EP pillar (±20 % thrust / ±15 % Isp / ±5 % discharge-power).
// Same band as the other HET fixtures; the Busch model treats both SPT and
// TAL topologies uniformly..
//
// Citations:
//   • Kim V. (1998). "Main Physical Features and Processes Determining
//     the Performance of Stationary Plasma Thrusters." J. Propulsion &
//     Power 14(5), 736-743.
//   • Belikov M.B., Gorshkov O.A., Lovtsov A.S. (2008). "High-power
//     thruster development at TsNIIMASH and Keldysh Research Center
//     based on the TAL topology." IEPC-2007-117.

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_TAL
{
    private const double TargetThrust_N = 0.085;
    private const double TargetIsp_s    = 1800.0;
    private const double TargetPd_W     = 1350.0;

    private const double ThrustToleranceFraction = 0.20;
    private const double IspToleranceFraction    = 0.15;
    private const double PdToleranceFraction     = 0.05;

    private static ElectricPropulsionEngineDesign TALDesign() => new(
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
        MagneticField_T    =   0.025,                // higher B than SPT
        AnodeRadius_mm     =  50.0,
        ChannelLength_mm   =  20.0,                  // shorter channel than SPT
        XenonMassFlow_kgs  =   5.0e-6,
        AnodeMaterial      = AnodeMaterial.Graphite,
        CathodeType        = CathodeType.HollowCathode,
    };

    private static ResistojetConditions TALConditions() => new(
        BusVoltage_V:        300.0,
        BusPower_W_avail:   2000.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.Xenon,
        InletTemperature_K:  300.0,
        InletComposition:    PropellantInletComposition.PureH2);

    [Fact]
    public void TAL_Thrust_WithinTwentyPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(TALDesign(), TALConditions());
        double low  = TargetThrust_N * (1.0 - ThrustToleranceFraction);
        double high = TargetThrust_N * (1.0 + ThrustToleranceFraction);
        Assert.InRange(result.Thrust_N, low, high);
    }

    [Fact]
    public void TAL_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(TALDesign(), TALConditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void TAL_DischargePower_MatchesVdTimesId()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(TALDesign(), TALConditions());
        var plasma = Assert.IsType<HetPlasmaState>(result.PlasmaState);
        double low  = TargetPd_W * (1.0 - PdToleranceFraction);
        double high = TargetPd_W * (1.0 + PdToleranceFraction);
        Assert.InRange(plasma.DischargePower_W, low, high);
    }

    [Fact]
    public void TAL_HigherIspThanSpt100_AtMatchingPower()
    {
        // SPT-100 baseline 1 600 s @ 1 350 W. TAL at matching power but
        // higher B-field + shorter channel should produce ≥ 10 % higher
        // Isp (thinner-discharge-layer efficiency advantage). Pin the
        // cross-fixture invariant.
        var result = ElectricPropulsionOptimization.GenerateWith(TALDesign(), TALConditions());
        Assert.True(result.IspVacuum_s > 1600.0 * 1.10,
            $"TAL Isp ({result.IspVacuum_s:F0} s) should exceed SPT-100's "
          + "1600 s by ≥ 10 % at matching discharge power.");
    }

    [Fact]
    public void TAL_PlasmaState_IsHet_AndFeasible()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(TALDesign(), TALConditions());
        Assert.IsType<HetPlasmaState>(result.PlasmaState);
        Assert.True(result.IsFeasible,
            "TAL baseline should pass all hard HET gates (B-field above floor, "
          + "V_d in band, graphite anode under temp limit).");
    }

    [Fact]
    public void TAL_Deterministic()
    {
        var r1 = ElectricPropulsionOptimization.GenerateWith(TALDesign(), TALConditions());
        var r2 = ElectricPropulsionOptimization.GenerateWith(TALDesign(), TALConditions());
        Assert.Equal(r1.Thrust_N,    r2.Thrust_N);
        Assert.Equal(r1.IspVacuum_s, r2.IspVacuum_s);
    }
}
