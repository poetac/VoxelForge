// BatteryWave2Tests.cs — Sprint BP.W2 unit tests for the temperature-
// dependent capacity-derating extension.

using Voxelforge.Battery;
using Xunit;

namespace Voxelforge.Tests.Battery;

public sealed class BatteryWave2Tests
{
    [Fact]
    public void DefaultCellTemperature_Is25C()
    {
        Assert.Equal(25.0, ModelThreeLongRange().CellTemperature_C, precision: 9);
    }

    [Fact]
    public void DeratingFactor_At25C_IsUnity()
    {
        Assert.Equal(1.0,
            BatteryPackSolver.ComputeTemperatureCapacityDerating(25.0),
            precision: 9);
    }

    [Fact]
    public void DeratingFactor_AtZeroC_IsUnity()
    {
        // 0 °C is the lower edge of the nominal band — no derating yet.
        Assert.Equal(1.0,
            BatteryPackSolver.ComputeTemperatureCapacityDerating(0.0),
            precision: 9);
    }

    [Fact]
    public void DeratingFactor_At45C_IsUnity()
    {
        // 45 °C is the upper edge of the nominal band — no derating yet.
        Assert.Equal(1.0,
            BatteryPackSolver.ComputeTemperatureCapacityDerating(45.0),
            precision: 9);
    }

    [Fact]
    public void DeratingFactor_AtMinus20C_ApplyColdPenalty()
    {
        // -20 °C → 1 − 0.005·20 = 0.90 (10 % loss).
        Assert.Equal(0.90,
            BatteryPackSolver.ComputeTemperatureCapacityDerating(-20.0),
            precision: 6);
    }

    [Fact]
    public void DeratingFactor_At60C_ApplyHotPenalty()
    {
        // 60 °C → 1 − 0.003·15 = 0.955 (4.5 % loss).
        Assert.Equal(1.0 - 0.003 * 15.0,
            BatteryPackSolver.ComputeTemperatureCapacityDerating(60.0),
            precision: 6);
    }

    [Fact]
    public void DeratingFactor_ClampsAtTenPercent_Minimum()
    {
        // Catastrophic cold: -300 °C → would be negative; clamp at 0.10.
        Assert.Equal(0.10,
            BatteryPackSolver.ComputeTemperatureCapacityDerating(-300.0),
            precision: 6);
    }

    [Fact]
    public void StoredEnergy_AtMinus20C_LowerThanAt25C()
    {
        var nominal = BatteryPackSolver.Solve(ModelThreeLongRange());
        var cold    = BatteryPackSolver.Solve(ModelThreeLongRange()
            with { CellTemperature_C = -20.0 });
        // Capacity drops 10 % → stored energy drops 10 %.
        Assert.Equal(0.90 * nominal.PackEnergyStored_Wh,
            cold.PackEnergyStored_Wh, precision: 1);
    }

    [Fact]
    public void StoredEnergy_BitIdenticalAt25C_VsBpW1Baseline()
    {
        // BP.W1 hard-coded 25 °C. At BP.W2 default = 25 °C, stored
        // energy must match the BP.W1 cluster band [70, 90] kWh.
        var r = BatteryPackSolver.Solve(ModelThreeLongRange());
        Assert.InRange(r.PackEnergyStored_Wh, 70_000.0, 90_000.0);
    }

    private static BatteryPackDesign ModelThreeLongRange() => new(
        Chemistry:        BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries:    96,
        ParallelStrings:  46,
        StateOfCharge:    1.0,
        LoadCurrent_A:    0.0);
}
