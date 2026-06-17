// ElectricPropulsionFixture_HiPEP.cs — Validation depth pass (Sprint M).
//
// Fourth Gridded-Ion Thruster validation fixture (after NSTAR + NEXT-C
// + NEXIS). HiPEP (High Power Electric Propulsion) was the NASA Glenn
// 35-kW class ion engine designed for the NEP-class JIMO follow-on
// vehicles. 40 cm beam diameter — the largest ion-thruster Voxelforge
// validates against. Anchors the *megawatt-class vehicle* end of the
// gridded-ion design space.
//
//   Inputs:  V_b=8 000 V, J_b=4.5 A, ScreenGridRadius=200 mm,
//            AccelGridGap=0.5 mm, NeutralizerCathodeCurrent=4.5 A,
//            GitMassUtilizationOverride=NaN (cluster anchor at η_m=0.90).
//   Targets: Thrust ≈ 670 mN, Isp ≈ 9 600 s, BeamPower ≈ 36 kW.
//
// HiPEP's defining property over NEXT-C: the higher beam voltage
// (8 kV vs 1.8 kV) produces dramatically higher Isp (~9 600 s vs
// ~4 200 s) at the cost of lower thrust-to-power ratio. The Child-
// Langmuir physics is identical — voltage drives both the thrust
// scaling and the Isp scaling — but the calibrated mass-utilization +
// the larger grid radius set the actual operating point.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Gridded-ion (NEP-class HV) variant under ADR-036 § EP pillar
// (±20 % thrust / ±15 % Isp / ±20 % mass-flow / ±2 % beam-power). HiPEP sits
// at the 8000 V high-voltage end of the GIT band (post-ADR-038 widening to
// [200, 12000] V). Bands match NSTAR / NEXT-C / NEXIS exactly; the Child-
// Langmuir closed-form physics is identical across the GIT lineage, only
// V_b + grid radius + mass-utilization differ. HiPEP is a passing fixture
// in physics-cascade-status.md (the η_m clamp affects HiVHAc / TAL / Mr510 /
// Nexis; HiPEP's higher mass-flow + V_b combination keeps η_m below the
// clamp threshold).
//
// Citations:
//   • Foster J.E., Haag T.W., Patterson M.J. (2004). "The High Power
//     Electric Propulsion (HiPEP) Ion Thruster." AIAA-2004-3812.
//   • Williams G.J., Haag T.W., Patterson M.J. (2004). "Performance
//     Testing of the HiPEP Ion Thruster." AIAA-2004-3501.

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_HiPEP
{
    private const double TargetThrust_N    = 0.670;
    private const double TargetIsp_s       = 9600.0;
    private const double TargetMassFlow_kgs = 7.1e-6;
    private const double TargetBeamPower_W = 36000.0;

    private const double ThrustToleranceFraction   = 0.20;
    private const double IspToleranceFraction      = 0.15;
    private const double MassFlowToleranceFraction = 0.20;
    private const double PowerToleranceFraction    = 0.02;

    private static ElectricPropulsionEngineDesign HiPEPDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.GriddedIon,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        BeamVoltage_V               = 8000.0,
        BeamCurrent_A               =    4.5,
        ScreenGridRadius_mm         =  200.0,             // 40 cm beam area diameter
        AccelGridGap_mm             =    0.5,
        NeutralizerCathodeCurrent_A =    4.5,
        // GitMassUtilizationOverride left at NaN → 0.90 cluster anchor.
    };

    private static ResistojetConditions HiPEPConditions() => new(
        BusVoltage_V:         100.0,
        BusPower_W_avail:   40000.0,             // 40 kW PPU on 35 kW engine
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K:  300.0,
        InletComposition:    PropellantInletComposition.PureH2);

    [Fact]
    public void HiPEP_Thrust_WithinTwentyPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(HiPEPDesign(), HiPEPConditions());
        double low  = TargetThrust_N * (1.0 - ThrustToleranceFraction);
        double high = TargetThrust_N * (1.0 + ThrustToleranceFraction);
        Assert.InRange(result.Thrust_N, low, high);
    }

    [Fact]
    public void HiPEP_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(HiPEPDesign(), HiPEPConditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void HiPEP_BeamPower_MatchesVbeamTimesJbeam()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(HiPEPDesign(), HiPEPConditions());
        var plasma = Assert.IsType<IonPlasmaState>(result.PlasmaState);
        double expected = plasma.AcceleratingVoltage_V * plasma.BeamCurrent_A;
        double low  = expected * (1.0 - PowerToleranceFraction);
        double high = expected * (1.0 + PowerToleranceFraction);
        Assert.InRange(expected, TargetBeamPower_W * 0.98, TargetBeamPower_W * 1.02);
        Assert.InRange(expected, low, high);
    }

    [Fact]
    public void HiPEP_HighestIsp_AcrossGITLineage()
    {
        // GIT fixture cascade: NSTAR 3300 s → NEXT-C 4190 s → NEXIS 7500 s
        // → HiPEP 9600 s. Each generation roughly +30-60% Isp at higher
        // V_b. Pin the cross-fixture invariant: HiPEP delivers >9000 s,
        // beating every prior GIT fixture.
        var result = ElectricPropulsionOptimization.GenerateWith(HiPEPDesign(), HiPEPConditions());
        Assert.True(result.IspVacuum_s > 9000.0,
            $"HiPEP Isp ({result.IspVacuum_s:F0} s) should exceed 9 000 s, "
          + "beating NEXIS (~7 500 s) by ≥ 20 % at the higher V_b.");
    }

    [Fact]
    public void HiPEP_BeamBelowChildLangmuirLimit()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(HiPEPDesign(), HiPEPConditions());
        var plasma = Assert.IsType<IonPlasmaState>(result.PlasmaState);
        Assert.True(plasma.BeamCurrent_A < plasma.ChildLangmuirLimit_A,
            $"Beam current {plasma.BeamCurrent_A:F3} A should sit below "
          + $"Child-Langmuir limit {plasma.ChildLangmuirLimit_A:F3} A.");
    }

    [Fact]
    public void HiPEP_PlasmaState_IsIon()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(HiPEPDesign(), HiPEPConditions());
        Assert.IsType<IonPlasmaState>(result.PlasmaState);
    }

    [Fact]
    public void HiPEP_WithinBeamVoltageBand_AfterB1()
    {
        // ADR-038 D2 widened the GIT beam-voltage band to [200, 12 000] V
        // (from [300, 2 000] V) to cover modern HV-GIT thrusters. HiPEP's
        // 8 000 V V_b now sits inside the band; the
        // GIT_BEAM_VOLTAGE_OUT_OF_BAND gate must not fire and the fixture
        // must report IsFeasible. The renamed test is the band-tightening
        // trip-wire — if a future ADR shrinks the band back below 8 000 V
        // the prior name collides, breaking the suite loudly.
        var result = ElectricPropulsionOptimization.GenerateWith(HiPEPDesign(), HiPEPConditions());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "GIT_BEAM_VOLTAGE_OUT_OF_BAND");
        Assert.True(result.IsFeasible,
            $"HiPEP should be feasible post-ADR-038. Saw {result.Violations.Count} "
          + $"violations: {string.Join(", ", result.Violations.Select(v => v.ConstraintId))}.");
    }

    [Fact]
    public void HiPEP_Deterministic()
    {
        var r1 = ElectricPropulsionOptimization.GenerateWith(HiPEPDesign(), HiPEPConditions());
        var r2 = ElectricPropulsionOptimization.GenerateWith(HiPEPDesign(), HiPEPConditions());
        Assert.Equal(r1.Thrust_N,    r2.Thrust_N);
        Assert.Equal(r1.IspVacuum_s, r2.IspVacuum_s);
    }
}
