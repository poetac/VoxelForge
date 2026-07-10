// ThermoelectricGeneratorSolverTests — pins the canonical figure-of-merit
// TEG math: η = η_Carnot·(√(1+ZT)−1)/(√(1+ZT)+Tc/Th), the exact hot-side
// energy balance Q_hot = P + Q_cold, the strict η < η_Carnot bound, and
// the TEG.W2 segmented-stack cascade η = 1−(1−η_h)(1−η_l) (#548-E fix).
// Pure closed form → exact assertions. Backfills coverage onto the Linux
// 'core' CI leg.

using System;
using Voxelforge.Thermoelectric;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class ThermoelectricGeneratorSolverTests
{
    // GPHS-RTG-class SiGe (ZT = 0.8) couple: T_hot = 1000 K, T_cold = 500 K,
    // Q_hot = 2000 W → η_Carnot = 0.5 exactly.
    private static ThermoelectricGeneratorDesign SiGeRtg()
        => new(ThermoelectricMaterial.SiliconGermanium,
               HotSideTemperature_K: 1000.0, ColdSideTemperature_K: 500.0,
               HotSideHeatInput_W: 2000.0);

    [Fact]
    public void Solve_SiGe_FigureOfMeritEfficiencyAndPower()
    {
        var r = ThermoelectricGeneratorSolver.Solve(SiGeRtg());
        Assert.Equal(0.5, r.CarnotEfficiency, 12);
        // η = 0.5·(√1.8 − 1)/(√1.8 + 0.5) ≈ 0.09275.
        Assert.Equal(0.09275445814522235, r.ConversionEfficiency, 12);
        Assert.Equal(185.5089162904447, r.ElectricPowerOutput_W, 9);
        Assert.Equal(1814.4910837095554, r.HeatRejectedToColdSide_W, 9);
        // First law: everything in leaves as electricity or cold-side heat.
        Assert.Equal(2000.0, r.ElectricPowerOutput_W + r.HeatRejectedToColdSide_W, 9);
        Assert.True(r.HotSideTemperatureInValidEnvelope);   // 1000 ∈ [773, 1273]
    }

    [Fact]
    public void Solve_BelowMaterialEnvelope_IsFlaggedNotRejected()
    {
        var r = ThermoelectricGeneratorSolver.Solve(SiGeRtg() with { HotSideTemperature_K = 700.0 });
        Assert.False(r.HotSideTemperatureInValidEnvelope);  // 700 < 773 SiGe floor
    }

    [Fact]
    public void ComputeFigureOfMeritEfficiency_ZeroZT_ProducesNothing()
        => Assert.Equal(0.0,
            ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(0.0, 1000.0, 500.0), 12);

    [Fact]
    public void ComputeFigureOfMeritEfficiency_StaysStrictlyBelowCarnot_AndGrowsWithZT()
    {
        // Even ZT = 30 (far beyond any real material) must stay under Carnot.
        double carnot = 1.0 - 500.0 / 1000.0;
        double etaHuge = ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(30.0, 1000.0, 500.0);
        Assert.True(etaHuge < carnot, $"η ({etaHuge}) must stay below Carnot ({carnot})");
        double eta1 = ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(1.0, 1000.0, 500.0);
        double eta2 = ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(2.0, 1000.0, 500.0);
        Assert.True(eta2 > eta1, "η must grow monotonically with ZT");
    }

    [Fact]
    public void Solve_BismuthTelluride_LowGradeWasteHeatPoint()
    {
        // Bi₂Te₃ (ZT = 1.0) at 400/300 K: η = 0.25·(√2−1)/(√2+0.75) ≈ 0.04785.
        var r = ThermoelectricGeneratorSolver.Solve(new ThermoelectricGeneratorDesign(
            ThermoelectricMaterial.BismuthTelluride, 400.0, 300.0, 1000.0));
        Assert.Equal(0.04784804623427543, r.ConversionEfficiency, 12);
        Assert.True(r.HotSideTemperatureInValidEnvelope);   // 400 ∈ [273, 473]
    }

    [Fact]
    public void ComputeSegmentedStackEfficiency_CascadeBeatsEitherSingleStage()
    {
        // SiGe hot segment (1000 → 600 K) over Bi₂Te₃ cold segment (600 → 300 K).
        double seg = ThermoelectricGeneratorSolver.ComputeSegmentedStackEfficiency(
            ThermoelectricMaterial.SiliconGermanium, ThermoelectricMaterial.BismuthTelluride,
            hotSideTemperature_K: 1000.0, intermediateTemperature_K: 600.0,
            coldSideTemperature_K: 300.0);
        Assert.Equal(0.17096115068937812, seg, 12);
        // Series-heat-engine cascade is ≥ either stage alone (the #548-E invariant).
        double etaHigh = ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(0.8, 1000.0, 600.0);
        double etaLow = ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(1.0, 600.0, 300.0);
        Assert.True(seg > etaHigh && seg > etaLow,
            $"cascade η ({seg}) must beat both stages ({etaHigh}, {etaLow})");
    }

    [Fact]
    public void SegmentedStack_RejectsIntermediateTemperatureOutsideGradient()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ThermoelectricGeneratorSolver.ComputeSegmentedStackEfficiency(
                ThermoelectricMaterial.SiliconGermanium, ThermoelectricMaterial.BismuthTelluride,
                1000.0, 1000.0, 300.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ThermoelectricGeneratorSolver.ComputeSegmentedStackEfficiency(
                ThermoelectricMaterial.SiliconGermanium, ThermoelectricMaterial.BismuthTelluride,
                1000.0, 250.0, 300.0));
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() => ThermoelectricGeneratorSolver.Solve(
            SiGeRtg() with { ColdSideTemperature_K = 1000.0 }));    // no gradient
        Assert.Throws<ArgumentException>(() => ThermoelectricGeneratorSolver.Solve(
            SiGeRtg() with { Material = ThermoelectricMaterial.None }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(-0.1, 1000.0, 500.0));
    }
}
