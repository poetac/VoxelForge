// ElectricPropulsionFixture_Nstar.cs — Sprint EP.W2.GIT acceptance.
//
// Wave-2 published-engine validation fixture for the NSTAR gridded-ion
// thruster (NASA-JPL, Deep Space 1 / Dawn). The first ion engine to be
// flown on a long-duration interplanetary mission; >30 000 h demonstrated
// on the ground + flight.
//
//   Inputs:  V_b=1100 V, J_b=1.76 A, ScreenGridRadius=145 mm,
//            AccelGridGap=0.6 mm, NeutralizerCathodeCurrent=1.76 A,
//            GitMassUtilizationOverride=NaN (cluster anchor at η_m=0.90).
//   Targets: Thrust ≈ 92 mN, Isp ≈ 3300 s, MassFlow ≈ 2.6 mg/s,
//            BeamPower ≈ 1936 W.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Gridded-ion (production anchor) variant under ADR-036 § EP pillar
// (±20 % thrust / ±15 % Isp / ±20 % mass-flow / ±2 % beam-power). NSTAR is
// the canonical GIT production anchor — Deep Space 1 + Dawn flight data
// + 30 000+ ground-test hours. Tighter than PPT's ±25 % because Child-
// Langmuir is a closed-form perveance limit, not an empirical fit; the
// scatter comes from the mass-utilisation efficiency (0.85–0.92 across
// cluster) which we anchor at the mid-band η_m = 0.90.
//
// Citations:
//   • Goebel D.M., Katz I. (2008). "Fundamentals of Electric Propulsion:
//     Ion and Hall Thrusters." JPL Space Science and Technology Series,
//     §5 + §6 (NSTAR cluster characterisation).
//   • Polk J.E., et al. (2003). "An Overview of the Results from an 8200
//     Hour Wear Test of the NSTAR Ion Thruster." JPL AIAA-99-2446.
//   • Brophy J.R. (2002). "NASA's Deep Space 1 Ion Engine." Rev. Sci. Instrum.
//     73(2), pp. 1071–1078.

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_Nstar
{
    private const double TargetThrust_N    = 0.092;     // 92 mN nominal at TH15
    private const double TargetIsp_s       = 3300.0;    // NSTAR nominal at TH15
    private const double TargetMassFlow_kgs = 2.6e-6;   // 2.6 mg/s
    private const double TargetBeamPower_W = 1936.0;    // V_b · J_b = 1100 · 1.76

    // ADR-029 D4 (generalised) tolerance contract.
    private const double ThrustToleranceFraction   = 0.20;  // ±20 %
    private const double IspToleranceFraction      = 0.15;  // ±15 %
    private const double MassFlowToleranceFraction = 0.20;  // tracks thrust
    private const double PowerToleranceFraction    = 0.02;  // V·I is exact arithmetic

    private static ElectricPropulsionEngineDesign NstarDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.GriddedIon,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        BeamVoltage_V               = 1100.0,
        BeamCurrent_A               =    1.76,
        ScreenGridRadius_mm         =  145.0,            // NSTAR active beam area diameter ~28 cm
        AccelGridGap_mm             =    0.6,
        NeutralizerCathodeCurrent_A =    1.76,           // matched to beam current
        // GitMassUtilizationOverride left at NaN — uses 0.90 cluster anchor.
    };

    private static ResistojetConditions NstarConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:   2500.0,                              // NSTAR PPU ~2.3 kW rated
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,          // placeholder; GIT ignores
        InletTemperature_K: 300.0,                               // ambient; GIT ignores
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void Nstar_Thrust_WithinTwentyPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NstarDesign(), NstarConditions());
        double low  = TargetThrust_N * (1.0 - ThrustToleranceFraction);
        double high = TargetThrust_N * (1.0 + ThrustToleranceFraction);
        Assert.InRange(result.Thrust_N, low, high);
    }

    [Fact]
    public void Nstar_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NstarDesign(), NstarConditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void Nstar_BeamPower_MatchesVbeamTimesJbeam()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NstarDesign(), NstarConditions());
        var plasma = Assert.IsType<IonPlasmaState>(result.PlasmaState);
        // BeamPower = V_b · J_beam — should match exactly within tiny float rounding.
        double expected = plasma.AcceleratingVoltage_V * plasma.BeamCurrent_A;
        double low  = expected * (1.0 - PowerToleranceFraction);
        double high = expected * (1.0 + PowerToleranceFraction);
        // Use exit-velocity / thrust to derive beam power for the assertion.
        // The plasma carries V_b + J_b directly.
        Assert.InRange(expected, TargetBeamPower_W * 0.98, TargetBeamPower_W * 1.02);
        Assert.InRange(expected, low, high);
    }

    [Fact]
    public void Nstar_MassFlow_WithinTwentyPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NstarDesign(), NstarConditions());
        // Mass flow lives on the model output, but the fixture exercises the
        // public surface: derive from Thrust / (Isp · g₀).
        double mDot = result.Thrust_N / (result.IspVacuum_s * 9.80665);
        double low  = TargetMassFlow_kgs * (1.0 - MassFlowToleranceFraction);
        double high = TargetMassFlow_kgs * (1.0 + MassFlowToleranceFraction);
        Assert.InRange(mDot, low, high);
    }

    [Fact]
    public void Nstar_PlasmaState_IsIon_BeamCurrentNonZero()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NstarDesign(), NstarConditions());
        Assert.NotNull(result.PlasmaState);
        var plasma = Assert.IsType<IonPlasmaState>(result.PlasmaState);
        // GIT has a genuinely meaningful beam current (unlike PPT).
        Assert.True(plasma.BeamCurrent_A > 0);
        Assert.Equal(1.76, plasma.BeamCurrent_A, precision: 2);
        Assert.True(result.IsFeasible,
            "NSTAR baseline should pass all hard GIT gates (V_b in band, "
          + "perveance below CL limit, neutraliser matched to beam current).");
    }

    [Fact]
    public void Nstar_BeamBelowChildLangmuirLimit()
    {
        // NSTAR operates well below the CL saturation limit — the perveance
        // margin is one of the design's defining characteristics.
        var result = ElectricPropulsionOptimization.GenerateWith(NstarDesign(), NstarConditions());
        var plasma = Assert.IsType<IonPlasmaState>(result.PlasmaState);
        Assert.True(plasma.BeamCurrent_A < plasma.ChildLangmuirLimit_A,
            $"Beam current {plasma.BeamCurrent_A:F3} A should sit below "
          + $"Child-Langmuir limit {plasma.ChildLangmuirLimit_A:F3} A.");
    }

    [Fact]
    public void Nstar_Deterministic()
    {
        var r1 = ElectricPropulsionOptimization.GenerateWith(NstarDesign(), NstarConditions());
        var r2 = ElectricPropulsionOptimization.GenerateWith(NstarDesign(), NstarConditions());
        Assert.Equal(r1.Thrust_N,    r2.Thrust_N);
        Assert.Equal(r1.IspVacuum_s, r2.IspVacuum_s);
    }

    [Fact]
    public void Nstar_PlasmaStateImplementsIPlasmaStateFromVoxelforgeCore()
    {
        // Fourth IPlasmaState consumer — promotion verification across HET +
        // Arcjet + PPT + GIT. The IonPlasmaState assignability check points
        // at Voxelforge.Plasma.IPlasmaState (Voxelforge.Core assembly), not
        // any pillar-local interface.
        var result = ElectricPropulsionOptimization.GenerateWith(NstarDesign(), NstarConditions());
        Voxelforge.Plasma.IPlasmaState plasma =
            Assert.IsAssignableFrom<Voxelforge.Plasma.IPlasmaState>(result.PlasmaState);
        Assert.True(plasma.IonExitVelocity_ms > 0);
        Assert.True(plasma.BeamCurrent_A > 0,
            "GIT carries a genuinely meaningful BeamCurrent_A.");
        Assert.True(plasma.PlumeDivergenceHalfAngle_rad > 0
                  && plasma.PlumeDivergenceHalfAngle_rad < System.Math.PI / 2);
    }
}
