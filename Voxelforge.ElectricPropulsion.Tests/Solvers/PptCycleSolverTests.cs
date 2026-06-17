// PptCycleSolverTests.cs — wrapper-level tests for the PPT cycle solver.
// Sibling to ArcjetCycleSolverTests on the Arcjet side.

using System;
using Voxelforge.ElectricPropulsion.Plasma;
using Voxelforge.ElectricPropulsion.Solvers;
using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class PptCycleSolverTests
{
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
    };

    private static ResistojetConditions VacuumConditions() => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:   200.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void Solve_Eo1_PopulatesPlasmaState()
    {
        var r = PptCycleSolver.Solve(Eo1Design(), VacuumConditions());
        Assert.NotNull(r.PlasmaState);
        Assert.NotNull(r.Ablation);
        Assert.True(r.Ablation.Converged);
    }

    [Fact]
    public void Solve_Eo1_PlasmaStateMirrorsModelOutput()
    {
        var r = PptCycleSolver.Solve(Eo1Design(), VacuumConditions());
        Assert.Equal(r.Ablation.ExitVelocity_ms,              r.PlasmaState.IonExitVelocity_ms);
        Assert.Equal(r.Ablation.PlumeDivergenceHalfAngle_rad, r.PlasmaState.PlumeDivergenceHalfAngle_rad);
        Assert.Equal(r.Ablation.ImpulseBit_Ns,                r.PlasmaState.ImpulseBit_Ns);
        Assert.Equal(r.Ablation.MassPerPulse_kg,              r.PlasmaState.MassPerPulse_kg);
        Assert.Equal(r.Ablation.AveragePower_W,               r.PlasmaState.AveragePower_W);
    }

    [Fact]
    public void Solve_Eo1_BeamCurrentZero()
    {
        // PPT has no continuous current path.
        var r = PptCycleSolver.Solve(Eo1Design(), VacuumConditions());
        Assert.Equal(0.0, r.PlasmaState.BeamCurrent_A, precision: 12);
    }

    [Fact]
    public void Solve_Eo1_DefaultsToClusterAnchor()
    {
        // PptIspCalibration = NaN means "use 8500 m/s cluster anchor implicitly
        // via K_i / K_m at the design E_cap".
        var r = PptCycleSolver.Solve(Eo1Design(), VacuumConditions());
        // EO-1 calibration: K_m and K_i fitted so v_exit ≈ 8500 m/s at E_cap = 22 J.
        Assert.InRange(r.Ablation.ExitVelocity_ms, 8000.0, 9000.0);
    }

    [Fact]
    public void Solve_OverrideIspCalibration_PropagatesIntoModel()
    {
        var design = Eo1Design() with { PptIspCalibration = 600.0 };
        var r = PptCycleSolver.Solve(design, VacuumConditions());
        // v_exit = 600 · 9.80665 ≈ 5884 m/s
        Assert.Equal(600.0 * AblationDischargeModel.g0, r.Ablation.ExitVelocity_ms, precision: 6);
        Assert.Equal(600.0, r.Ablation.AverageIsp_s, precision: 6);
    }

    [Fact]
    public void Solve_OnArcjetKind_Throws()
    {
        var arcMisuse = Eo1Design() with { Kind = ElectricPropulsionEngineKind.Arcjet };
        Assert.Throws<ArgumentException>(
            () => PptCycleSolver.Solve(arcMisuse, VacuumConditions()));
    }

    [Fact]
    public void Solve_OnHallEffectKind_Throws()
    {
        var hetMisuse = Eo1Design() with { Kind = ElectricPropulsionEngineKind.HallEffect };
        Assert.Throws<ArgumentException>(
            () => PptCycleSolver.Solve(hetMisuse, VacuumConditions()));
    }

    [Fact]
    public void Solve_NaNCapacitorEnergy_Throws()
    {
        var broken = Eo1Design() with { CapacitorEnergy_J = double.NaN };
        Assert.Throws<ArgumentException>(
            () => PptCycleSolver.Solve(broken, VacuumConditions()));
    }

    [Fact]
    public void Solve_NullDesign_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => PptCycleSolver.Solve(null!, VacuumConditions()));
    }

    [Fact]
    public void Solve_PlasmaState_ImplementsIPlasmaState()
    {
        var r = PptCycleSolver.Solve(Eo1Design(), VacuumConditions());
        // Idiomatic xUnit assertion (CA1859-safe) that the concrete type
        // implements the abstraction. IPlasmaState now lives in
        // Voxelforge.Plasma (ADR-029a), not Voxelforge.ElectricPropulsion.Plasma.
        IPlasmaState plasma = Assert.IsAssignableFrom<IPlasmaState>(r.PlasmaState);
        Assert.True(plasma.IonExitVelocity_ms > 0);
        Assert.Equal(0.0, plasma.BeamCurrent_A, precision: 12);
        Assert.True(plasma.PlumeDivergenceHalfAngle_rad > 0
                  && plasma.PlumeDivergenceHalfAngle_rad < Math.PI / 2);
    }
}
