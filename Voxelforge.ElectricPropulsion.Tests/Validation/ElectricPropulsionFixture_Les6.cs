// ElectricPropulsionFixture_Les6.cs — Validation depth pass.
//
// First-ever-flown Pulsed Plasma Thruster fixture: the LES-6 thruster
// (Lincoln Experimental Satellite 6, 1968). Vondra & Thomassen documented
// the LES-6 performance envelope at 1.85 J / pulse, ~1 Hz, ~26 µN·s
// impulse bit. Sits at the LOWER end of the EP-PPT design envelope vs
// EO-1's 22 J / 5 Hz / 860 µN·s — validates the Solbes-Vondra fit at
// small-capacitor / low-Isp scales typical of early-1970s hardware.
//
//   Inputs:  E_cap=1.85 J, f_pulse=1.0 Hz, ElectrodeGap=10 mm,
//            PropellantBarLength=15 mm, ElectrodeWidth=8 mm,
//            PptIspCalibration=280 s (Vondra-Thomassen LES-6 anchor; the
//            small-discharge geometry underperforms the cluster-default
//            8500 m/s, so the override pins the calibration).
//   Targets: I_bit ≈ 26 µN·s, Isp ≈ 280 s, AveragePower ≈ 1.85 W.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Pulsed Plasma Thruster (PPT) variant under ADR-036 § EP pillar
// (±25 % impulse-bit, ±15 % Isp, ±5 % power [E_cap × f_pulse exact]). LES-6
// pins the LOW end of the PPT envelope; the cluster-default 8500 m/s exhaust
// velocity overshoots small-discharge LES-6 geometry, so PptIspCalibration =
// 280 s pins the Solbes-Vondra fit. Same band as AerojetEo1; basis remains
// per-pulse impulse (ADR-036 D4 ambiguity clarified by this fixture's targets).
//
// Citations:
//   • Vondra R.J., Thomassen K., Solbes A. (1974). "Flight Qualified
//     Pulsed Plasma Thruster." J. Spacecraft 11(9), pp. 613–617.
//   • Vondra R.J., Thomassen K. (1974). "Performance improvements in
//     solid fuel microthrusters." J. Spacecraft 11(11), pp. 738–742.

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_Les6
{
    private const double TargetImpulseBit_Ns = 26e-6;       // 26 µN·s
    private const double TargetIsp_s         = 280.0;
    private const double TargetAvgPower_W    = 1.85;        // E_cap × f_pulse

    private const double ImpulseBitToleranceFraction = 0.25;
    private const double IspToleranceFraction        = 0.15;
    private const double PowerToleranceFraction      = 0.05;

    private static ElectricPropulsionEngineDesign Les6Design() => new(
        Kind:                    ElectricPropulsionEngineKind.PulsedPlasmaThruster,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        CapacitorEnergy_J         = 1.85,
        PulseFrequency_Hz         = 1.0,
        PptElectrodeGap_mm        = 10.0,
        PptPropellantBarLength_mm = 15.0,
        PptElectrodeWidth_mm      =  8.0,
        // LES-6 documented Isp 280 s — the small-discharge geometry
        // significantly underperforms the cluster-default 8500 m/s
        // (≈866 s) baseline. Override to Vondra-Thomassen 1974 anchor.
        PptIspCalibration         = 280.0,
    };

    private static ResistojetConditions Les6Conditions() => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:    50.0,                  // LES-6 PPU rated ~20 W; +30 W headroom
        AmbientPressure_Pa:   0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void Les6_ImpulseBit_WithinTwentyFivePercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Les6Design(), Les6Conditions());
        var plasma = Assert.IsType<PptPlasmaState>(result.PlasmaState);
        double low  = TargetImpulseBit_Ns * (1.0 - ImpulseBitToleranceFraction);
        double high = TargetImpulseBit_Ns * (1.0 + ImpulseBitToleranceFraction);
        Assert.InRange(plasma.ImpulseBit_Ns, low, high);
    }

    [Fact]
    public void Les6_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Les6Design(), Les6Conditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void Les6_AveragePower_MatchesECapTimesFrequency()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Les6Design(), Les6Conditions());
        var plasma = Assert.IsType<PptPlasmaState>(result.PlasmaState);
        double low  = TargetAvgPower_W * (1.0 - PowerToleranceFraction);
        double high = TargetAvgPower_W * (1.0 + PowerToleranceFraction);
        Assert.InRange(plasma.AveragePower_W, low, high);
    }

    [Fact]
    public void Les6_LowerIspThanEo1_AtSmallDischargeGeometry()
    {
        // Cross-fixture invariant: LES-6 at 1.85 J / 280 s sits well
        // below EO-1's 22 J / 870 s anchor. The override mechanism is
        // what lets the same physics model bracket both ends of the
        // documented PPT envelope.
        var result = ElectricPropulsionOptimization.GenerateWith(Les6Design(), Les6Conditions());
        Assert.True(result.IspVacuum_s < 870.0 * 0.5,
            $"LES-6 Isp ({result.IspVacuum_s:F0} s) should be ≤ half EO-1's "
          + "870 s baseline (small-discharge geometry undeperforms).");
    }

    [Fact]
    public void Les6_PlasmaState_IsPpt_AndFeasible()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Les6Design(), Les6Conditions());
        Assert.IsType<PptPlasmaState>(result.PlasmaState);
        Assert.True(result.IsFeasible,
            "LES-6 baseline should pass all hard PPT gates "
          + "(E_cap in [0.5, 50] J band, breakdown threshold cleared).");
    }

    [Fact]
    public void Les6_Deterministic()
    {
        var r1 = ElectricPropulsionOptimization.GenerateWith(Les6Design(), Les6Conditions());
        var r2 = ElectricPropulsionOptimization.GenerateWith(Les6Design(), Les6Conditions());
        Assert.Equal(r1.Thrust_N,    r2.Thrust_N);
        Assert.Equal(r1.IspVacuum_s, r2.IspVacuum_s);
    }
}
