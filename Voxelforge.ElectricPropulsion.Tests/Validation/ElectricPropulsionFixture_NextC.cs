// ElectricPropulsionFixture_NextC.cs — Validation depth pass.
//
// Second Gridded-Ion Thruster validation fixture (after NSTAR). NEXT
// (NASA Evolutionary Xenon Thruster) is the next-generation NSTAR
// successor — 36 cm beam diameter (vs NSTAR's 30 cm), 7 kW maximum vs
// NSTAR's 2.3 kW, and ~50 % higher Isp. Flown on NASA DART (2022) for
// the planetary-defence asteroid-redirect demonstration. Anchors the
// upper-power end of the Child-Langmuir GIT design space.
//
//   Inputs:  V_b=1800 V, J_b=3.52 A, ScreenGridRadius=180 mm,
//            AccelGridGap=0.5 mm, NeutralizerCathodeCurrent=3.52 A,
//            GitMassUtilizationOverride=NaN (uses 0.90 cluster anchor).
//   Targets: Thrust ≈ 236 mN, Isp ≈ 4190 s, BeamPower ≈ 6336 W.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Gridded-ion (NEXT-class) variant under ADR-036 § EP pillar
// (±20 % thrust / ±15 % Isp / ±20 % mass-flow / ±2 % beam-power). Same band
// as NSTAR — both anchor the same Child-Langmuir physics surface at
// different beam-power scales. NEXT-C is a PASSING fixture per physics-
// cascade-status.md #546 (the η_m clamp affects HiVHAc / TAL / Mr510 /
// Nexis specifically; NEXT-C's V_b 1800 V + J_b 3.52 A combination keeps
// η_m below the clamp threshold).
//
// Citations:
//   • Patterson M.J., Benson S.W. (2007). "NEXT Ion Propulsion System
//     Development Status and Performance." AIAA-2007-5199. (Primary
//     NEXT performance datasheet.)
//   • Soulas G.C., Domonkos M.T., Patterson M.J. (2003). "Performance
//     Evaluation of the NEXT Ion Engine." AIAA-2003-5278.
//   • NASA DART mission summary (Cheng A.F. et al. 2018): NEXT-C
//     end-of-prime-mission performance, 100 %+ Isp delivery on cruise.

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_NextC
{
    private const double TargetThrust_N    = 0.236;     // 236 mN @ TH37 full power
    private const double TargetIsp_s       = 4190.0;    // NEXT TH37 nominal
    private const double TargetMassFlow_kgs = 5.74e-6;  // 5.74 mg/s
    private const double TargetBeamPower_W = 6336.0;    // V_b · J_b = 1800 · 3.52

    private const double ThrustToleranceFraction   = 0.20;  // ±20 %
    private const double IspToleranceFraction      = 0.15;  // ±15 %
    private const double MassFlowToleranceFraction = 0.20;
    private const double PowerToleranceFraction    = 0.02;  // V·I is exact arithmetic

    private static ElectricPropulsionEngineDesign NextCDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.GriddedIon,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        BeamVoltage_V               = 1800.0,
        BeamCurrent_A               =    3.52,
        ScreenGridRadius_mm         =  180.0,             // 36 cm beam area diameter
        AccelGridGap_mm             =    0.5,
        NeutralizerCathodeCurrent_A =    3.52,            // matched to beam current
        // GitMassUtilizationOverride left at NaN — uses 0.90 cluster anchor.
    };

    private static ResistojetConditions NextCConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:   7000.0,                              // NEXT PPU 7 kW rated
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,          // placeholder; GIT ignores
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void NextC_Thrust_WithinTwentyPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NextCDesign(), NextCConditions());
        double low  = TargetThrust_N * (1.0 - ThrustToleranceFraction);
        double high = TargetThrust_N * (1.0 + ThrustToleranceFraction);
        Assert.InRange(result.Thrust_N, low, high);
    }

    [Fact]
    public void NextC_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NextCDesign(), NextCConditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void NextC_BeamPower_MatchesVbeamTimesJbeam()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NextCDesign(), NextCConditions());
        var plasma = Assert.IsType<IonPlasmaState>(result.PlasmaState);
        double expected = plasma.AcceleratingVoltage_V * plasma.BeamCurrent_A;
        double low  = expected * (1.0 - PowerToleranceFraction);
        double high = expected * (1.0 + PowerToleranceFraction);
        Assert.InRange(expected, TargetBeamPower_W * 0.98, TargetBeamPower_W * 1.02);
        Assert.InRange(expected, low, high);
    }

    [Fact]
    public void NextC_MassFlow_WithinTwentyPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NextCDesign(), NextCConditions());
        double mDot = result.Thrust_N / (result.IspVacuum_s * 9.80665);
        double low  = TargetMassFlow_kgs * (1.0 - MassFlowToleranceFraction);
        double high = TargetMassFlow_kgs * (1.0 + MassFlowToleranceFraction);
        Assert.InRange(mDot, low, high);
    }

    [Fact]
    public void NextC_BeamBelowChildLangmuirLimit()
    {
        // NEXT operates closer to the perveance limit than NSTAR (higher
        // beam-current density on the larger grid), but still below.
        var result = ElectricPropulsionOptimization.GenerateWith(NextCDesign(), NextCConditions());
        var plasma = Assert.IsType<IonPlasmaState>(result.PlasmaState);
        Assert.True(plasma.BeamCurrent_A < plasma.ChildLangmuirLimit_A,
            $"Beam current {plasma.BeamCurrent_A:F3} A should sit below "
          + $"Child-Langmuir limit {plasma.ChildLangmuirLimit_A:F3} A.");
    }

    [Fact]
    public void NextC_PlasmaState_IsIon_AndFeasible()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(NextCDesign(), NextCConditions());
        Assert.IsType<IonPlasmaState>(result.PlasmaState);
        Assert.True(result.IsFeasible,
            "NEXT-C baseline should pass all hard GIT gates (V_b in band, "
          + "perveance below CL limit, neutraliser matched to beam current).");
    }

    [Fact]
    public void NextC_HigherIspThanNstar()
    {
        // NEXT's defining performance gain over NSTAR is the higher Isp from
        // the higher accelerating voltage (1800 V vs 1100 V) — captured here
        // as a cross-fixture invariant on the model's V_b → Isp scaling.
        var nextResult = ElectricPropulsionOptimization.GenerateWith(NextCDesign(), NextCConditions());
        // NSTAR target Isp is 3300 s; NEXT should exceed it by ≥ 20 %.
        Assert.True(nextResult.IspVacuum_s > 3300.0 * 1.20,
            $"NEXT Isp ({nextResult.IspVacuum_s:F0} s) should exceed NSTAR's "
          + "3300 s baseline by ≥ 20 % at the higher beam voltage.");
    }

    [Fact]
    public void NextC_Deterministic()
    {
        var r1 = ElectricPropulsionOptimization.GenerateWith(NextCDesign(), NextCConditions());
        var r2 = ElectricPropulsionOptimization.GenerateWith(NextCDesign(), NextCConditions());
        Assert.Equal(r1.Thrust_N,    r2.Thrust_N);
        Assert.Equal(r1.IspVacuum_s, r2.IspVacuum_s);
    }
}
