// MairLozanoEmitterModelTests.cs — Sprint EP.W5 phase 2 unit tests for
// the closed-form Mair-Lozano FEEP emitter model.

using System;
using Voxelforge.ElectricPropulsion.Solvers;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class MairLozanoEmitterModelTests
{
    private const double Eps = 1.0e-9;

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Solve_NonPositiveVoltage_Throws()
    {
        Assert.Throws<ArgumentException>(() => MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: 0.0,
            beamCurrent_A:         100e-6,
            emitterTipRadius_mm:   0.005,
            propellant:            FeepPropellant.Indium));

        Assert.Throws<ArgumentException>(() => MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: -9000.0,
            beamCurrent_A:         100e-6,
            emitterTipRadius_mm:   0.005,
            propellant:            FeepPropellant.Indium));
    }

    [Fact]
    public void Solve_NanVoltage_Throws()
    {
        Assert.Throws<ArgumentException>(() => MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: double.NaN,
            beamCurrent_A:         100e-6,
            emitterTipRadius_mm:   0.005,
            propellant:            FeepPropellant.Indium));
    }

    [Fact]
    public void Solve_NonPositiveBeamCurrent_Throws()
    {
        Assert.Throws<ArgumentException>(() => MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: 9000.0,
            beamCurrent_A:         0.0,
            emitterTipRadius_mm:   0.005,
            propellant:            FeepPropellant.Indium));
    }

    [Fact]
    public void Solve_NonPositiveTipRadius_Throws()
    {
        Assert.Throws<ArgumentException>(() => MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: 9000.0,
            beamCurrent_A:         100e-6,
            emitterTipRadius_mm:   0.0,
            propellant:            FeepPropellant.Indium));
    }

    [Fact]
    public void Solve_FeepPropellantNone_Throws()
    {
        Assert.Throws<ArgumentException>(() => MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: 9000.0,
            beamCurrent_A:         100e-6,
            emitterTipRadius_mm:   0.005,
            propellant:            FeepPropellant.None));
    }

    // ── IFM Nano-class anchor (Indium @ 9 kV, 100 μA, 5 μm tip) ──────────

    [Fact]
    public void IfmNanoAnchor_Thrust_NearOneHundredMicroNewton()
    {
        // Calibrated cluster anchor: at the IFM Nano design point the
        // model produces T = 100 μN by construction (γ_In = 47 chosen
        // so this holds). The exact value lands at ~ 100.2 μN due to
        // rounded m_In and γ; tolerate ±3 % numerical floor.
        var r = MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: 9000.0,
            beamCurrent_A:         100e-6,
            emitterTipRadius_mm:   0.005,
            propellant:            FeepPropellant.Indium);
        Assert.InRange(r.Thrust_N, 0.97e-4, 1.03e-4);
    }

    [Fact]
    public void IfmNanoAnchor_Isp_NearOneEightThirtyFiveSeconds()
    {
        // Single-component model kinematic Isp = v_eff / g₀ at the IFM
        // Nano anchor lands ~ 1835 s. This is the THRUST-BEARING Isp,
        // not the marketing "effective Isp" of 4000-6000 s which
        // requires a two-population beam (deferred to Wave-4).
        var r = MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: 9000.0,
            beamCurrent_A:         100e-6,
            emitterTipRadius_mm:   0.005,
            propellant:            FeepPropellant.Indium);
        Assert.InRange(r.IspVacuum_s, 1750.0, 1920.0);
    }

    [Fact]
    public void IfmNanoAnchor_ConvergedTrue()
    {
        // Closed-form model: Converged is always true (no iterative
        // solver). Pin so a future Newton-Raphson variant doesn't
        // silently regress.
        var r = MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: 9000.0,
            beamCurrent_A:         100e-6,
            emitterTipRadius_mm:   0.005,
            propellant:            FeepPropellant.Indium);
        Assert.True(r.Converged);
    }

    [Fact]
    public void IfmNanoAnchor_TipFieldAroundNineExpEightVperM()
    {
        // E_tip = α · V_acc / r_tip = 0.5 · 9000 / 5e-6 = 9e8 V/m.
        // Sits just below the 1e9 FN threshold — the design point is
        // marginally sub-threshold; real IFM Nano runs at slightly
        // higher V_acc or sharper tip to push above.
        var r = MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: 9000.0,
            beamCurrent_A:         100e-6,
            emitterTipRadius_mm:   0.005,
            propellant:            FeepPropellant.Indium);
        Assert.Equal(9.0e8, r.EmitterTipField_VperM, precision: 0);  // ±0.5 V/m absolute
    }

    [Fact]
    public void IfmNanoAnchor_EffectiveIonMass_MatchesIndiumClusterFactor()
    {
        // m_eff = γ_In · m_In = 47 · 1.9063e-25 ≈ 8.96e-24 kg.
        var r = MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: 9000.0,
            beamCurrent_A:         100e-6,
            emitterTipRadius_mm:   0.005,
            propellant:            FeepPropellant.Indium);
        double expected = MairLozanoEmitterModel.IndiumClusterFactor
                        * MairLozanoEmitterModel.IndiumAtomicMass_kg;
        Assert.Equal(expected, r.EffectiveIonMass_kg, precision: 15);
    }

    // ── Scaling laws (closed-form sanity) ────────────────────────────────

    [Fact]
    public void Thrust_ScalesLinearlyWithBeamCurrent()
    {
        // T = I_beam · √(2 · m_eff · V / e) — linear in I_beam at fixed V.
        var r1 = MairLozanoEmitterModel.Solve(9000.0, 100e-6, 0.005, FeepPropellant.Indium);
        var r2 = MairLozanoEmitterModel.Solve(9000.0, 200e-6, 0.005, FeepPropellant.Indium);
        Assert.Equal(2.0, r2.Thrust_N / r1.Thrust_N, precision: 9);
    }

    [Fact]
    public void Thrust_ScalesAsSqrtVoltage_AtFixedCurrent()
    {
        // T ∝ √V_acc at fixed I_beam. Doubling V should give √2× thrust.
        var r1 = MairLozanoEmitterModel.Solve(5000.0, 100e-6, 0.005, FeepPropellant.Indium);
        var r2 = MairLozanoEmitterModel.Solve(10000.0, 100e-6, 0.005, FeepPropellant.Indium);
        Assert.Equal(Math.Sqrt(2.0), r2.Thrust_N / r1.Thrust_N, precision: 9);
    }

    [Fact]
    public void ExitVelocity_ScalesAsSqrtVoltage()
    {
        // v_eff ∝ √V_acc at fixed propellant.
        var r1 = MairLozanoEmitterModel.Solve(5000.0, 100e-6, 0.005, FeepPropellant.Indium);
        var r2 = MairLozanoEmitterModel.Solve(10000.0, 100e-6, 0.005, FeepPropellant.Indium);
        Assert.Equal(Math.Sqrt(2.0), r2.ExitVelocity_ms / r1.ExitVelocity_ms, precision: 9);
    }

    [Fact]
    public void TipField_InverseInTipRadius()
    {
        // E_tip = α · V / r_tip — inverse in r_tip at fixed V.
        var r1 = MairLozanoEmitterModel.Solve(9000.0, 100e-6, 0.005, FeepPropellant.Indium);
        var r2 = MairLozanoEmitterModel.Solve(9000.0, 100e-6, 0.010, FeepPropellant.Indium);
        Assert.Equal(0.5, r2.EmitterTipField_VperM / r1.EmitterTipField_VperM, precision: 9);
    }

    [Fact]
    public void MassFlow_LinearInBeamCurrent()
    {
        // ṁ = I_beam · m_eff / e — linear in I_beam at fixed propellant.
        var r1 = MairLozanoEmitterModel.Solve(9000.0, 100e-6, 0.005, FeepPropellant.Indium);
        var r2 = MairLozanoEmitterModel.Solve(9000.0, 300e-6, 0.005, FeepPropellant.Indium);
        Assert.Equal(3.0, r2.MassFlow_kgs / r1.MassFlow_kgs, precision: 9);
    }

    [Fact]
    public void ThrustEqualsMassFlowTimesExitVelocity()
    {
        // Energy + charge conservation: T = ṁ · v_eff exactly.
        var r = MairLozanoEmitterModel.Solve(9000.0, 100e-6, 0.005, FeepPropellant.Indium);
        double reconstructed = r.MassFlow_kgs * r.ExitVelocity_ms;
        Assert.Equal(r.Thrust_N, reconstructed, precision: 12);
    }

    // ── Indium vs Cesium differentiators ─────────────────────────────────

    [Fact]
    public void CesiumIspExceedsIndiumIsp_AtIdenticalGeometryAndPower()
    {
        // Cesium has smaller cluster factor (γ_Cs = 5 < γ_In = 47) →
        // smaller m_eff → higher v_eff → higher Isp. The fundamental
        // Indium/Cesium differentiator in this model.
        var indium  = MairLozanoEmitterModel.Solve(9000.0, 100e-6, 0.005, FeepPropellant.Indium);
        var cesium  = MairLozanoEmitterModel.Solve(9000.0, 100e-6, 0.005, FeepPropellant.Cesium);
        Assert.True(cesium.IspVacuum_s > indium.IspVacuum_s,
            $"Cesium Isp ({cesium.IspVacuum_s:F0}) should exceed Indium Isp "
          + $"({indium.IspVacuum_s:F0}) at identical operating point.");
    }

    [Fact]
    public void CesiumMassFlowBelowIndiumMassFlow_AtIdenticalCurrent()
    {
        // ṁ = I · m_eff / e; Cs's smaller m_eff means lower ṁ at the same I.
        var indium  = MairLozanoEmitterModel.Solve(9000.0, 100e-6, 0.005, FeepPropellant.Indium);
        var cesium  = MairLozanoEmitterModel.Solve(9000.0, 100e-6, 0.005, FeepPropellant.Cesium);
        Assert.True(cesium.MassFlow_kgs < indium.MassFlow_kgs);
    }

    [Fact]
    public void Indium_PlumeDivergenceAroundFifteenDegrees()
    {
        // FEEP plumes are narrow because the extractor electrode acts as
        // a focusing element. ~15° = 0.262 rad is the model's anchor.
        var r = MairLozanoEmitterModel.Solve(9000.0, 100e-6, 0.005, FeepPropellant.Indium);
        Assert.Equal(0.262, r.PlumeDivergence_rad, precision: 3);
    }

    // ── Cycle solver thin wrapper ────────────────────────────────────────

    [Fact]
    public void FeepCycleSolver_NullDesign_Throws()
    {
        var cond = new ResistojetConditions(
            BusVoltage_V:        12000.0,
            BusPower_W_avail:    1.0,
            AmbientPressure_Pa:  0.0,
            Propellant:          Propellant.Xenon,
            InletTemperature_K:  300.0,
            InletComposition:    PropellantInletComposition.PureH2);
        Assert.Throws<ArgumentNullException>(() => FeepCycleSolver.Solve(null!, cond));
    }

    [Fact]
    public void FeepCycleSolver_NullConditions_Throws()
    {
        var design = MakeFeepDesign();
        Assert.Throws<ArgumentNullException>(() => FeepCycleSolver.Solve(design, null!));
    }

    [Fact]
    public void FeepCycleSolver_NonFeepKind_Throws()
    {
        var design = MakeFeepDesign() with { Kind = ElectricPropulsionEngineKind.HallEffect };
        var cond = MakeFeepConditions();
        Assert.Throws<ArgumentException>(() => FeepCycleSolver.Solve(design, cond));
    }

    [Fact]
    public void FeepCycleSolver_NaNRequired_Throws()
    {
        var design = MakeFeepDesign() with { FeepAcceleratingVoltage_V = double.NaN };
        var cond = MakeFeepConditions();
        Assert.Throws<ArgumentException>(() => FeepCycleSolver.Solve(design, cond));
    }

    [Fact]
    public void FeepCycleSolver_PropellantNone_Throws()
    {
        var design = MakeFeepDesign() with { FeepPropellantMaterial = FeepPropellant.None };
        var cond = MakeFeepConditions();
        Assert.Throws<ArgumentException>(() => FeepCycleSolver.Solve(design, cond));
    }

    [Fact]
    public void FeepCycleSolver_PackagesPlasmaState()
    {
        var design = MakeFeepDesign();
        var cond = MakeFeepConditions();
        var result = FeepCycleSolver.Solve(design, cond);
        Assert.NotNull(result.PlasmaState);
        Assert.Equal(design.FeepAcceleratingVoltage_V, result.PlasmaState.AcceleratingVoltage_V);
        Assert.Equal(design.FeepPropellantMaterial, result.PlasmaState.PropellantMaterial);
        Assert.Equal(result.Emitter.ExitVelocity_ms, result.PlasmaState.IonExitVelocity_ms, precision: 9);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static ElectricPropulsionEngineDesign MakeFeepDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.Feep,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        FeepAcceleratingVoltage_V = 9000.0,
        FeepBeamCurrent_A         = 100e-6,
        FeepEmitterTipRadius_mm   = 0.005,
        FeepPropellantMaterial    = FeepPropellant.Indium,
    };

    private static ResistojetConditions MakeFeepConditions() => new(
        BusVoltage_V:        12000.0,
        BusPower_W_avail:    5.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.Xenon,    // FEEP ignores; sentinel
        InletTemperature_K:  300.0,
        InletComposition:    PropellantInletComposition.PureH2);
}
