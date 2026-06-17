// ElectricPropulsionFixture_AerojetEo1.cs — Sprint EP.W2.PPT acceptance.
//
// Wave-2 published-engine validation fixture for the Aerojet EO-1 EP-12
// Pulsed Plasma Thruster. Flown on the NASA EO-1 (Earth Observing-1)
// spacecraft for fine attitude control + the demonstration thrust event
// in 2002 (Hofer 2003 IEPC paper; Spores et al. AIAA-2002-3974).
//
//   Inputs:  E_cap=22 J, f_pulse=5 Hz, ElectrodeGap=25 mm,
//            PropellantBarLength=25 mm, ElectrodeWidth=15 mm,
//            PptIspCalibration=NaN (cluster anchor).
//   Targets: I_bit ≈ 860 µN·s (per pulse), Isp ≈ 870 s, AveragePower ≈ 110 W,
//            AverageThrust ≈ 4.30 mN, Δm ≈ 101 µg/pulse.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Pulsed Plasma Thruster (PPT) variant under ADR-036 § EP pillar
// (±25 % impulse-bit, ±15 % Isp, ±5 % power [E_cap × f_pulse is exact arithmetic],
// ±25 % thrust tracks impulse-bit). ADR-036 D4 flags PPT "impulse-bit" basis
// AMBIGUOUS — this fixture clarifies the basis: per-pulse impulse (I_bit ≈
// 860 µN·s), with derived average thrust = I_bit × f_pulse. Bands wider than
// HET's ±20 % to absorb residual Solbes-Vondra ablation-fit scatter
// (Solbes-Vondra 1973 J. Spacecraft 10(6)).
//
// Citations:
//   • Vondra & Thomassen (1974). "Flight Qualified Pulsed Plasma Thruster."
//     J. Spacecraft 11(9), pp. 613–617.
//   • Solbes & Vondra (1973). "Performance Study of a Solid Fuel Pulsed
//     Electric Microthruster." J. Spacecraft 10(6), pp. 406–410.
//   • Hofer R.R., et al. (2003). "EO-1 Pulsed Plasma Thruster Performance
//     Characterization." 28th IEPC.
//   • Sutton & Biblarz (2017). "Rocket Propulsion Elements" 9e §16.4.

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_AerojetEo1
{
    private const double TargetImpulseBit_Ns = 860e-6;     // 860 µN·s
    private const double TargetIsp_s         = 870.0;      // ≈ DefaultExhaustVelocity_ms / g0
    private const double TargetAvgPower_W    = 110.0;      // E_cap × f_pulse = 22 × 5
    private const double TargetAvgThrust_N   = 4.30e-3;    // 4.30 mN

    // ADR-029 D4 (generalised) tolerance contract.
    private const double ImpulseBitToleranceFraction = 0.25;  // ±25 %
    private const double IspToleranceFraction        = 0.15;  // ±15 %
    private const double PowerToleranceFraction      = 0.05;  // ±5 % (E × f is exact arithmetic)
    private const double ThrustToleranceFraction     = 0.25;  // tracks impulse-bit

    private static ElectricPropulsionEngineDesign Eo1Design() => new(
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
        // PptIspCalibration left at NaN — uses the cluster anchor (8500 m/s).
    };

    private static ResistojetConditions Eo1Conditions() => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:   200.0,                             // EO-1 EP-12 PPU rated ~200 W
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,        // placeholder; PPT ignores
        InletTemperature_K: 300.0,                             // ambient bar — PPT ignores
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void Eo1_ImpulseBit_WithinTwentyFivePercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Eo1Design(), Eo1Conditions());
        var plasma = Assert.IsType<PptPlasmaState>(result.PlasmaState);
        double low  = TargetImpulseBit_Ns * (1.0 - ImpulseBitToleranceFraction);
        double high = TargetImpulseBit_Ns * (1.0 + ImpulseBitToleranceFraction);
        Assert.InRange(plasma.ImpulseBit_Ns, low, high);
    }

    [Fact]
    public void Eo1_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Eo1Design(), Eo1Conditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void Eo1_AveragePower_MatchesEcapTimesFpulse()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Eo1Design(), Eo1Conditions());
        var plasma = Assert.IsType<PptPlasmaState>(result.PlasmaState);
        double low  = TargetAvgPower_W * (1.0 - PowerToleranceFraction);
        double high = TargetAvgPower_W * (1.0 + PowerToleranceFraction);
        Assert.InRange(plasma.AveragePower_W, low, high);
    }

    [Fact]
    public void Eo1_AverageThrust_WithinTwentyFivePercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Eo1Design(), Eo1Conditions());
        double low  = TargetAvgThrust_N * (1.0 - ThrustToleranceFraction);
        double high = TargetAvgThrust_N * (1.0 + ThrustToleranceFraction);
        Assert.InRange(result.Thrust_N, low, high);
    }

    [Fact]
    public void Eo1_PlasmaState_IsPpt_AndBeamCurrentZero()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Eo1Design(), Eo1Conditions());
        Assert.NotNull(result.PlasmaState);
        var plasma = Assert.IsType<PptPlasmaState>(result.PlasmaState);
        // PPT has no continuous current path.
        Assert.Equal(0.0, plasma.BeamCurrent_A, precision: 12);
        Assert.True(result.IsFeasible,
            "EO-1 baseline should pass all hard PPT gates (E_cap in band, "
          + "above breakdown threshold).");
    }

    [Fact]
    public void Eo1_Deterministic()
    {
        var r1 = ElectricPropulsionOptimization.GenerateWith(Eo1Design(), Eo1Conditions());
        var r2 = ElectricPropulsionOptimization.GenerateWith(Eo1Design(), Eo1Conditions());
        Assert.Equal(r1.Thrust_N,    r2.Thrust_N);
        Assert.Equal(r1.IspVacuum_s, r2.IspVacuum_s);
    }

    [Fact]
    public void Eo1_PlasmaStateImplementsIPlasmaStateFromVoxelforgeCore()
    {
        // Promotion verification: the PptPlasmaState assignability check
        // points at Voxelforge.Plasma.IPlasmaState (Voxelforge.Core
        // assembly), not the old EP-pillar-local interface.
        var result = ElectricPropulsionOptimization.GenerateWith(Eo1Design(), Eo1Conditions());
        Voxelforge.Plasma.IPlasmaState plasma =
            Assert.IsAssignableFrom<Voxelforge.Plasma.IPlasmaState>(result.PlasmaState);
        Assert.True(plasma.IonExitVelocity_ms > 0);
        Assert.Equal(0.0, plasma.BeamCurrent_A, precision: 12);
        Assert.True(plasma.PlumeDivergenceHalfAngle_rad > 0
                  && plasma.PlumeDivergenceHalfAngle_rad < System.Math.PI / 2);
    }
}
