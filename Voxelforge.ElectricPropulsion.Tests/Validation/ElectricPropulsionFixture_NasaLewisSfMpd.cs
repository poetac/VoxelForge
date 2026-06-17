// ElectricPropulsionFixture_NasaLewisSfMpd.cs — Sprint EP.W2.MPD acceptance.
//
// Wave-2 published-engine validation fixture for the NASA-Lewis 200 kW
// self-field MPD thruster (Sovey 1990, AIAA-90-2628). Ground-test article
// representative of the self-field MPD cluster — argon propellant,
// coaxial cathode/anode, no applied magnetic field.
//
//   Inputs:  J_arc=4000 A, ṁ_Ar=200 mg/s, r_c=10 mm, r_a=100 mm, L=150 mm,
//            MpdCathodeMaterial=ThoriatedTungsten.
//   Targets: T ≈ 4.9 N (bare Maecker), Isp ≈ 2500 s, P_arc ≈ 280 kW,
//            v_exit ≈ 24 km/s.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Self-field MPD variant under ADR-036 § EP pillar (±25 % thrust /
// ±15 % Isp). ±25 % thrust looser than GIT's ±20 % because the bare-Maecker model captures only the
// J² contribution; real SF-MPDs land ~1.5× higher once anode-fall + pinch
// effects are included — the band absorbs the missing ~50 % contribution
// rather than masks it (model widening would lose the signal).
//
// Citations:
//   • Maecker H. (1955). "Plasmaströmungen in Lichtbögen infolge
//     eigenmagnetischer Kompression." Z. Physik 141, pp. 198–216.
//   • Sovey J.S., Mantenieks M.A. (1990). "Performance and Lifetime
//     Assessment of MPD Thrusters." AIAA-90-2628.
//   • Polk J.E. (1991). "Operation of a 100 kW Class Applied-Field MPD
//     Thruster with Lithium." NASA-TM-104380. (Cathode-erosion + onset
//     reference; used for cathode material limits.)
//   • Choueiri E.Y. (1998). "Scaling of Thrust in Self-Field
//     Magnetoplasmadynamic Thrusters." J. Propulsion & Power 14(5),
//     pp. 744–753. (Onset criterion ξ_onset ≈ 100–200 kA²/(g/s).)

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_NasaLewisSfMpd
{
    private const double TargetThrust_N      = 4.9;        // bare Maecker baseline
    private const double TargetIsp_s         = 2500.0;     // SF-MPD cluster nominal
    private const double TargetExitVelocity_ms = 24500.0;  // T / ṁ
    private const double TargetCathodeT_K    = 3000.0;     // ThW operating point

    // ADR-029 D4 (generalised) tolerance contract.
    private const double ThrustToleranceFraction       = 0.25;
    private const double IspToleranceFraction          = 0.15;
    private const double ExitVelocityToleranceFraction = 0.15;

    private static ElectricPropulsionEngineDesign NasaLewisDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.MagnetoPlasmaDynamic,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:    2.0e-4,                              // 200 mg/s Ar
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        MpdArcCurrent_A     = 4000.0,
        MpdCathodeRadius_mm =   10.0,
        MpdAnodeRadius_mm   =  100.0,
        MpdChamberLength_mm =  150.0,
        MpdCathodeMaterial  = MpdCathodeMaterial.ThoriatedTungsten,
    };

    private static ResistojetConditions NasaLewisConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail: 300000.0,                                     // 300 kW PPU headroom
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,                 // placeholder; MPD ignores
        InletTemperature_K: 300.0,                                      // ambient; MPD ignores
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void NasaLewis_Thrust_WithinTwentyFivePercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NasaLewisDesign(), NasaLewisConditions());
        double low  = TargetThrust_N * (1.0 - ThrustToleranceFraction);
        double high = TargetThrust_N * (1.0 + ThrustToleranceFraction);
        Assert.InRange(result.Thrust_N, low, high);
    }

    [Fact]
    public void NasaLewis_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NasaLewisDesign(), NasaLewisConditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void NasaLewis_ExitVelocity_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NasaLewisDesign(), NasaLewisConditions());
        double low  = TargetExitVelocity_ms * (1.0 - ExitVelocityToleranceFraction);
        double high = TargetExitVelocity_ms * (1.0 + ExitVelocityToleranceFraction);
        Assert.InRange(result.ExitVelocity_ms, low, high);
    }

    [Fact]
    public void NasaLewis_CathodeBelowMaterialLimit()
    {
        // Thoriated W limit 3200 K. The lumped 0-D cathode model predicts
        // ~3000 K at the baseline operating point — comfortably below.
        var result = ElectricPropulsionOptimization.GenerateWith(NasaLewisDesign(), NasaLewisConditions());
        var plasma = Assert.IsType<MpdPlasmaState>(result.PlasmaState);
        Assert.True(plasma.CathodeWallTemp_K < 3200.0,
            $"ThW cathode at the NASA-Lewis baseline ({plasma.CathodeWallTemp_K:F0} K) "
          + "should sit below the 3200 K material limit.");
    }

    [Fact]
    public void NasaLewis_PlasmaState_IsMpd_AndArcCurrentMatchesDesign()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NasaLewisDesign(), NasaLewisConditions());
        Assert.NotNull(result.PlasmaState);
        var plasma = Assert.IsType<MpdPlasmaState>(result.PlasmaState);
        Assert.Equal(4000.0, plasma.BeamCurrent_A, precision: 6);
        Assert.True(result.IsFeasible,
            "NASA-Lewis baseline should pass all hard MPD gates "
          + "(J in band, ThW cathode below 3200 K, geometry not inverted).");
    }

    [Fact]
    public void NasaLewis_Deterministic()
    {
        var r1 = ElectricPropulsionOptimization.GenerateWith(NasaLewisDesign(), NasaLewisConditions());
        var r2 = ElectricPropulsionOptimization.GenerateWith(NasaLewisDesign(), NasaLewisConditions());
        Assert.Equal(r1.Thrust_N,    r2.Thrust_N);
        Assert.Equal(r1.IspVacuum_s, r2.IspVacuum_s);
    }

    [Fact]
    public void NasaLewis_PlasmaStateImplementsIPlasmaStateFromVoxelforgeCore()
    {
        // Fifth IPlasmaState consumer — promotion verification across HET +
        // Arcjet + PPT + GIT + MPD. The MpdPlasmaState assignability check
        // points at Voxelforge.Plasma.IPlasmaState (Voxelforge.Core
        // assembly), not any pillar-local interface.
        var result = ElectricPropulsionOptimization.GenerateWith(NasaLewisDesign(), NasaLewisConditions());
        Voxelforge.Plasma.IPlasmaState plasma =
            Assert.IsAssignableFrom<Voxelforge.Plasma.IPlasmaState>(result.PlasmaState);
        Assert.True(plasma.IonExitVelocity_ms > 0);
        Assert.Equal(4000.0, plasma.BeamCurrent_A, precision: 6);
        Assert.True(plasma.PlumeDivergenceHalfAngle_rad > 0
                  && plasma.PlumeDivergenceHalfAngle_rad < System.Math.PI / 2);
    }
}
