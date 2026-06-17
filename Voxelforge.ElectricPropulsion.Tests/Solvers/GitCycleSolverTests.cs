// GitCycleSolverTests.cs — Sprint EP.W2.GIT wrapper-level tests.
//
// Covers solver-dispatch validation (kind / null guards), NaN-trap fields,
// and the IonPlasmaState packaging shape. Physics is verified in
// ChildLangmuirBeamModelTests; this layer pins the integration contract.

using System;
using Voxelforge.ElectricPropulsion.Plasma;
using Voxelforge.ElectricPropulsion.Solvers;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class GitCycleSolverTests
{
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
        ScreenGridRadius_mm         =  145.0,
        AccelGridGap_mm             =    0.6,
        NeutralizerCathodeCurrent_A =    1.76,
    };

    private static ResistojetConditions DefaultConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:   2500.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void Solve_NstarDesign_ProducesIonPlasmaState()
    {
        var r = GitCycleSolver.Solve(NstarDesign(), DefaultConditions());
        Assert.NotNull(r.PlasmaState);
        Assert.NotNull(r.Beam);
        Assert.True(r.Beam.Converged);
    }

    [Fact]
    public void Solve_NstarDesign_PlasmaStateCarriesGridGeometry()
    {
        var design = NstarDesign();
        var r = GitCycleSolver.Solve(design, DefaultConditions());
        IonPlasmaState plasma = r.PlasmaState;
        Assert.Equal(design.BeamVoltage_V, plasma.AcceleratingVoltage_V, precision: 6);
        Assert.Equal(design.NeutralizerCathodeCurrent_A, plasma.NeutralizerCurrent_A, precision: 6);
        Assert.True(plasma.Perveance_AOverV1p5 > 0);
        Assert.True(plasma.ChildLangmuirLimit_A > 0);
        Assert.Equal(r.Beam.BeamCurrent_A, plasma.BeamCurrent_A, precision: 6);
        Assert.Equal(r.Beam.IonExitVelocity_ms, plasma.IonExitVelocity_ms, precision: 6);
    }

    [Fact]
    public void Solve_NullDesign_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            GitCycleSolver.Solve(null!, DefaultConditions()));

    [Fact]
    public void Solve_NullConditions_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            GitCycleSolver.Solve(NstarDesign(), null!));

    [Fact]
    public void Solve_WrongKind_Throws()
    {
        var ptt = NstarDesign() with { Kind = ElectricPropulsionEngineKind.PulsedPlasmaThruster };
        Assert.Throws<ArgumentException>(() =>
            GitCycleSolver.Solve(ptt, DefaultConditions()));
    }

    [Fact]
    public void Solve_NaNBeamVoltage_Throws()
    {
        var design = NstarDesign() with { BeamVoltage_V = double.NaN };
        Assert.Throws<ArgumentException>(() =>
            GitCycleSolver.Solve(design, DefaultConditions()));
    }

    [Fact]
    public void Solve_NaNBeamCurrent_Throws()
    {
        var design = NstarDesign() with { BeamCurrent_A = double.NaN };
        Assert.Throws<ArgumentException>(() =>
            GitCycleSolver.Solve(design, DefaultConditions()));
    }

    [Fact]
    public void Solve_NaNScreenGridRadius_Throws()
    {
        var design = NstarDesign() with { ScreenGridRadius_mm = double.NaN };
        Assert.Throws<ArgumentException>(() =>
            GitCycleSolver.Solve(design, DefaultConditions()));
    }

    [Fact]
    public void Solve_NaNAccelGridGap_Throws()
    {
        var design = NstarDesign() with { AccelGridGap_mm = double.NaN };
        Assert.Throws<ArgumentException>(() =>
            GitCycleSolver.Solve(design, DefaultConditions()));
    }

    [Fact]
    public void Solve_NaNNeutralizerCurrent_Throws()
    {
        var design = NstarDesign() with { NeutralizerCathodeCurrent_A = double.NaN };
        Assert.Throws<ArgumentException>(() =>
            GitCycleSolver.Solve(design, DefaultConditions()));
    }

    [Fact]
    public void Solve_OptionalMassUtilizationOverrideMayBeNaN()
    {
        // GitMassUtilizationOverride at NaN is the explicit cluster-anchor mode.
        var design = NstarDesign() with { GitMassUtilizationOverride = double.NaN };
        var r = GitCycleSolver.Solve(design, DefaultConditions());
        Assert.True(r.Beam.Converged);
    }
}
