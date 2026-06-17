// ArcjetCycleSolverTests.cs — wrapper-level tests for the Arcjet cycle solver.
// Sibling to HetCycleSolverTests on the HET side.

using System;
using Voxelforge.ElectricPropulsion.Plasma;
using Voxelforge.ElectricPropulsion.Solvers;
using Voxelforge.Plasma;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class ArcjetCycleSolverTests
{
    private static ElectricPropulsionEngineDesign Mr509Design() => new(
        Kind:                    ElectricPropulsionEngineKind.Arcjet,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  3.9e-5,
        NozzleThroatRadius_mm:   0.5,
        NozzleAreaRatio:        100.0,
        HeaterChamberLength_mm:  12.0,
        HeaterChamberRadius_mm:   4.0)
    {
        ArcVoltage_V             = 100.0,
        ArcCurrent_A             =  18.0,
        ArcGap_mm                =   2.0,
        ArcjetElectrodeMaterial  = ArcjetElectrodeMaterial.Tungsten,
    };

    private static ResistojetConditions VacuumConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:   2200.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 900.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void Solve_Mr509_PopulatesPlasmaState()
    {
        var r = ArcjetCycleSolver.Solve(Mr509Design(), VacuumConditions());
        Assert.NotNull(r.PlasmaState);
        Assert.NotNull(r.Maecker);
        Assert.True(r.Maecker.Converged);
    }

    [Fact]
    public void Solve_Mr509_PlasmaStateMirrorsModelOutput()
    {
        var r = ArcjetCycleSolver.Solve(Mr509Design(), VacuumConditions());
        Assert.Equal(r.Maecker.ExitVelocity_ms,              r.PlasmaState.IonExitVelocity_ms);
        Assert.Equal(r.Maecker.PlumeDivergenceHalfAngle_rad, r.PlasmaState.PlumeDivergenceHalfAngle_rad);
        Assert.Equal(r.Maecker.ArcPower_W,                   r.PlasmaState.ArcPower_W);
        Assert.Equal(r.Maecker.AnodeWallTemp_K,              r.PlasmaState.AnodeWallTemp_K);
    }

    [Fact]
    public void Solve_Mr509_BeamCurrentEqualsArcCurrent()
    {
        // No neutraliser in arcjet — all of I_arc is "useful" current.
        var r = ArcjetCycleSolver.Solve(Mr509Design(), VacuumConditions());
        Assert.Equal(18.0, r.PlasmaState.BeamCurrent_A, precision: 9);
        Assert.Equal(18.0, r.PlasmaState.ArcCurrent_A,  precision: 9);
        Assert.Equal(100.0, r.PlasmaState.ArcVoltage_V, precision: 9);
    }

    [Fact]
    public void Solve_Mr509_DefaultsToClusterAnchorThermalEfficiency()
    {
        // ArcjetThermalEfficiency = NaN means "use 0.40 cluster anchor".
        var r = ArcjetCycleSolver.Solve(Mr509Design(), VacuumConditions());
        Assert.Equal(MaeckerKovityaArcModel.DefaultThermalEfficiency,
                     r.PlasmaState.ThermalEfficiency,
                     precision: 9);
    }

    [Fact]
    public void Solve_OverridenThermalEfficiency_PropagatesIntoModel()
    {
        var design = Mr509Design() with { ArcjetThermalEfficiency = 0.50 };
        var r = ArcjetCycleSolver.Solve(design, VacuumConditions());
        Assert.Equal(0.50, r.PlasmaState.ThermalEfficiency, precision: 9);
        // Higher η raises Isp vs default.
        var rBase = ArcjetCycleSolver.Solve(Mr509Design(), VacuumConditions());
        Assert.True(r.Maecker.IspVacuum_s > rBase.Maecker.IspVacuum_s);
    }

    [Fact]
    public void Solve_OnHallEffectKind_Throws()
    {
        var hetMisuse = Mr509Design() with { Kind = ElectricPropulsionEngineKind.HallEffect };
        Assert.Throws<ArgumentException>(
            () => ArcjetCycleSolver.Solve(hetMisuse, VacuumConditions()));
    }

    [Fact]
    public void Solve_OnResistojetKind_Throws()
    {
        var resMisuse = Mr509Design() with { Kind = ElectricPropulsionEngineKind.Resistojet };
        Assert.Throws<ArgumentException>(
            () => ArcjetCycleSolver.Solve(resMisuse, VacuumConditions()));
    }

    [Fact]
    public void Solve_NaNArcVoltage_Throws()
    {
        var broken = Mr509Design() with { ArcVoltage_V = double.NaN };
        Assert.Throws<ArgumentException>(
            () => ArcjetCycleSolver.Solve(broken, VacuumConditions()));
    }

    [Fact]
    public void Solve_NaNArcCurrent_Throws()
    {
        var broken = Mr509Design() with { ArcCurrent_A = double.NaN };
        Assert.Throws<ArgumentException>(
            () => ArcjetCycleSolver.Solve(broken, VacuumConditions()));
    }

    [Fact]
    public void Solve_NaNArcGap_Throws()
    {
        var broken = Mr509Design() with { ArcGap_mm = double.NaN };
        Assert.Throws<ArgumentException>(
            () => ArcjetCycleSolver.Solve(broken, VacuumConditions()));
    }

    [Fact]
    public void Solve_NullDesign_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ArcjetCycleSolver.Solve(null!, VacuumConditions()));
    }

    [Fact]
    public void Solve_NullConditions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ArcjetCycleSolver.Solve(Mr509Design(), null!));
    }

    [Fact]
    public void Solve_PlasmaState_ImplementsIPlasmaState()
    {
        var r = ArcjetCycleSolver.Solve(Mr509Design(), VacuumConditions());
        // Idiomatic xUnit assertion that the concrete type implements the
        // abstraction (mirrors HetCycleSolverTests pattern post-CA1859 fix).
        IPlasmaState plasma = Assert.IsAssignableFrom<IPlasmaState>(r.PlasmaState);
        Assert.True(plasma.IonExitVelocity_ms > 0);
        Assert.True(plasma.BeamCurrent_A > 0);
        Assert.True(plasma.PlumeDivergenceHalfAngle_rad > 0
                  && plasma.PlumeDivergenceHalfAngle_rad < Math.PI / 2);
    }
}
