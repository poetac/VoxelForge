// MpdCycleSolverTests.cs — Sprint EP.W2.MPD wrapper-level tests.
//
// Covers solver-dispatch validation (kind / null guards), NaN-trap fields,
// and the MpdPlasmaState packaging shape. Physics is verified in
// SelfFieldLorentzModelTests; this layer pins the integration contract.

using System;
using Voxelforge.ElectricPropulsion.Plasma;
using Voxelforge.ElectricPropulsion.Solvers;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class MpdCycleSolverTests
{
    private static ElectricPropulsionEngineDesign NasaLewisDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.MagnetoPlasmaDynamic,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:    2.0e-4,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        MpdArcCurrent_A      = 4000.0,
        MpdCathodeRadius_mm  =   10.0,
        MpdAnodeRadius_mm    =  100.0,
        MpdChamberLength_mm  =  150.0,
        MpdCathodeMaterial   = MpdCathodeMaterial.ThoriatedTungsten,
    };

    private static ResistojetConditions DefaultConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail: 250000.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void Solve_NasaLewisDesign_ProducesMpdPlasmaState()
    {
        var r = MpdCycleSolver.Solve(NasaLewisDesign(), DefaultConditions());
        Assert.NotNull(r.PlasmaState);
        Assert.NotNull(r.Lorentz);
        Assert.True(r.Lorentz.Converged);
    }

    [Fact]
    public void Solve_NasaLewisDesign_PlasmaStateCarriesArcAndGeometry()
    {
        var design = NasaLewisDesign();
        var r = MpdCycleSolver.Solve(design, DefaultConditions());
        MpdPlasmaState plasma = r.PlasmaState;
        Assert.Equal(design.MpdArcCurrent_A, plasma.BeamCurrent_A, precision: 6);
        Assert.Equal(r.Lorentz.DischargeVoltage_V, plasma.DischargeVoltage_V, precision: 6);
        Assert.Equal(r.Lorentz.ThrustCoefficient_NperA2, plasma.ThrustCoefficient_NperA2, precision: 12);
        Assert.Equal(r.Lorentz.MagneticPressure_Pa, plasma.MagneticPressure_Pa, precision: 6);
        Assert.Equal(r.Lorentz.CathodeWallTemp_K, plasma.CathodeWallTemp_K, precision: 6);
        Assert.Equal(r.Lorentz.ThrustEfficiency_Maecker, plasma.ThrustEfficiency_Maecker, precision: 6);
        Assert.Equal(r.Lorentz.ExitVelocity_ms, plasma.IonExitVelocity_ms, precision: 6);
    }

    [Fact]
    public void Solve_NullDesign_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            MpdCycleSolver.Solve(null!, DefaultConditions()));

    [Fact]
    public void Solve_NullConditions_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            MpdCycleSolver.Solve(NasaLewisDesign(), null!));

    [Fact]
    public void Solve_WrongKind_Throws()
    {
        var git = NasaLewisDesign() with { Kind = ElectricPropulsionEngineKind.GriddedIon };
        Assert.Throws<ArgumentException>(() =>
            MpdCycleSolver.Solve(git, DefaultConditions()));
    }

    [Fact]
    public void Solve_NaNArcCurrent_Throws()
    {
        var design = NasaLewisDesign() with { MpdArcCurrent_A = double.NaN };
        Assert.Throws<ArgumentException>(() =>
            MpdCycleSolver.Solve(design, DefaultConditions()));
    }

    [Fact]
    public void Solve_NaNCathodeRadius_Throws()
    {
        var design = NasaLewisDesign() with { MpdCathodeRadius_mm = double.NaN };
        Assert.Throws<ArgumentException>(() =>
            MpdCycleSolver.Solve(design, DefaultConditions()));
    }

    [Fact]
    public void Solve_NaNAnodeRadius_Throws()
    {
        var design = NasaLewisDesign() with { MpdAnodeRadius_mm = double.NaN };
        Assert.Throws<ArgumentException>(() =>
            MpdCycleSolver.Solve(design, DefaultConditions()));
    }

    [Fact]
    public void Solve_NaNChamberLength_Throws()
    {
        var design = NasaLewisDesign() with { MpdChamberLength_mm = double.NaN };
        Assert.Throws<ArgumentException>(() =>
            MpdCycleSolver.Solve(design, DefaultConditions()));
    }

    [Fact]
    public void Solve_NaNPropellantMassFlow_Throws()
    {
        var design = NasaLewisDesign() with { PropellantMassFlow_kgs = double.NaN };
        Assert.Throws<ArgumentException>(() =>
            MpdCycleSolver.Solve(design, DefaultConditions()));
    }
}
