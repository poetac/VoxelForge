// HydroWave2Tests.cs — Sprint HE.W2 unit tests for the auto-select helper.

using System;
using Voxelforge.Hydroelectric;
using Xunit;

namespace Voxelforge.Tests.Hydroelectric;

public sealed class HydroWave2Tests
{
    [Fact]
    public void SelectKind_HighHead_PicksPelton()
    {
        // 800 m head is squarely in the Pelton envelope.
        Assert.Equal(HydroTurbineKind.Pelton,
            HydroTurbineSolver.SelectKindForHead(800.0));
    }

    [Fact]
    public void SelectKind_MediumHead_PicksFrancis()
    {
        // 80 m (Three Gorges) is in the Francis envelope.
        Assert.Equal(HydroTurbineKind.Francis,
            HydroTurbineSolver.SelectKindForHead(80.0));
    }

    [Fact]
    public void SelectKind_LowHead_PicksKaplan()
    {
        // 10 m head is below Francis min (10 m exactly is at edge; 8 m
        // is below) → Kaplan.
        Assert.Equal(HydroTurbineKind.Kaplan,
            HydroTurbineSolver.SelectKindForHead(8.0));
    }

    [Fact]
    public void SelectKind_AtPeltonMin_PicksPelton()
    {
        // 200 m is exactly at Pelton envelope min.
        Assert.Equal(HydroTurbineKind.Pelton,
            HydroTurbineSolver.SelectKindForHead(200.0));
    }

    [Fact]
    public void SelectKind_AtFrancisMin_PicksFrancis()
    {
        // 10 m is exactly at Francis envelope min.
        Assert.Equal(HydroTurbineKind.Francis,
            HydroTurbineSolver.SelectKindForHead(10.0));
    }

    [Fact]
    public void SelectKind_RejectsZeroHead()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HydroTurbineSolver.SelectKindForHead(0.0));
    }
}
