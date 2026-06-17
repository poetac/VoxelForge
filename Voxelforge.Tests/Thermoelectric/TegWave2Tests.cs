// TegWave2Tests.cs — Sprint TEG.W2 unit tests for the segmented-stack
// efficiency helper.

using System;
using Voxelforge.Thermoelectric;
using Xunit;

namespace Voxelforge.Tests.Thermoelectric;

public sealed class TegWave2Tests
{
    [Fact]
    public void SegmentedStack_SiGePlusBiTe_BeatsSingleStageBoth()
    {
        // SiGe high-T (1273 K → 573 K) + Bi₂Te₃ low-T (573 K → 300 K)
        // segmented stack should outperform either single stage alone
        // across the full T range.
        double eta_segmented = ThermoelectricGeneratorSolver.ComputeSegmentedStackEfficiency(
            highTemperatureMaterial:    ThermoelectricMaterial.SiliconGermanium,
            lowTemperatureMaterial:     ThermoelectricMaterial.BismuthTelluride,
            hotSideTemperature_K:       1273.0,
            intermediateTemperature_K:   573.0,
            coldSideTemperature_K:       300.0);
        double eta_sige = ThermoelectricGeneratorSolver.ComputeFigureOfMeritEfficiency(
            figureOfMerit_ZT:     ThermoelectricMaterialRegistry.SiliconGermanium.FigureOfMerit_ZT,
            hotSideTemperature_K: 1273.0,
            coldSideTemperature_K: 300.0);
        Assert.True(eta_segmented > eta_sige,
            $"Segmented η ({eta_segmented:F4}) expected > SiGe-only "
          + $"η ({eta_sige:F4}).");
    }

    [Fact]
    public void SegmentedStack_RejectsInvalidIntermediate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ThermoelectricGeneratorSolver.ComputeSegmentedStackEfficiency(
                ThermoelectricMaterial.SiliconGermanium,
                ThermoelectricMaterial.BismuthTelluride,
                1273.0, 1300.0, 300.0));      // T_int > T_hot
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ThermoelectricGeneratorSolver.ComputeSegmentedStackEfficiency(
                ThermoelectricMaterial.SiliconGermanium,
                ThermoelectricMaterial.BismuthTelluride,
                1273.0, 250.0, 300.0));       // T_int < T_cold
    }

    [Fact]
    public void SegmentedStack_BelowCarnotBound()
    {
        double T_hot = 1273.0;
        double T_cold = 300.0;
        double eta = ThermoelectricGeneratorSolver.ComputeSegmentedStackEfficiency(
            ThermoelectricMaterial.SiliconGermanium,
            ThermoelectricMaterial.BismuthTelluride,
            T_hot, 573.0, T_cold);
        double eta_carnot = 1.0 - T_cold / T_hot;
        Assert.True(eta < eta_carnot,
            $"Segmented η ({eta:F4}) must be < Carnot bound ({eta_carnot:F4}).");
    }
}
