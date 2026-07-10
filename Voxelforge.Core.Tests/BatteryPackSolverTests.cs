// BatteryPackSolverTests — pins the series-parallel pack roll-up: linear
// OCV(SoC), per-cell IR drop at I_pack/N_p, R_pack = N_s·R_cell/N_p, the
// ∫OCV·dSoC stored-energy integral, Joule heating I²·R_pack, and the BP.W2
// piecewise temperature-derating fit with its 0.1 clamp. Pure closed form →
// exact assertions. Backfills coverage onto the Linux 'core' CI leg.

using System;
using Voxelforge.Battery;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class BatteryPackSolverTests
{
    // Tesla-Model-3-class NMC pack: 96s46p, fully charged, 100 A discharge
    // at 25 °C (derating factor exactly 1). NMC: 3.0–4.2 V, 30 mΩ, 5 Ah.
    private static BatteryPackDesign NmcPack()
        => new(BatteryChemistry.NickelManganeseCobalt,
               CellsInSeries: 96, ParallelStrings: 46,
               StateOfCharge: 1.0, LoadCurrent_A: 100.0);

    [Fact]
    public void Solve_VoltagesAndResistance_MatchSeriesParallelRollUp()
    {
        var r = BatteryPackSolver.Solve(NmcPack());
        Assert.Equal(4.2, r.OpenCircuitCellVoltage_V, 12);              // OCV at SoC = 1
        // V_cell = 4.2 − (100/46)·0.030 ≈ 4.1348 V.
        Assert.Equal(4.1347826086956525, r.LoadedCellVoltage_V, 12);
        Assert.Equal(403.2, r.PackOpenCircuitVoltage_V, 9);             // 96·4.2
        Assert.Equal(396.93913043478267, r.PackLoadedVoltage_V, 9);
        Assert.Equal(96 * 0.030 / 46.0, r.PackInternalResistance_Ohm, 15);
    }

    [Fact]
    public void Solve_StoredEnergy_IsOcvIntegralTimesCellCount()
    {
        // E = N_s·N_p·C·[V_min·SoC + ½·ΔV·SoC²]. Full charge:
        // 96·46·5·(3.0 + 0.6) = 79 488 Wh exactly (≈ the 82 kWh-class pack).
        var full = BatteryPackSolver.Solve(NmcPack());
        Assert.Equal(79_488.0, full.PackEnergyStored_Wh, 6);
        // Half charge: 96·46·5·(1.5 + 0.15) = 36 432 Wh — NOT half of full,
        // because the OCV integral is quadratic in SoC.
        var half = BatteryPackSolver.Solve(NmcPack() with { StateOfCharge = 0.5 });
        Assert.Equal(36_432.0, half.PackEnergyStored_Wh, 6);
        Assert.True(half.PackEnergyStored_Wh < 0.5 * full.PackEnergyStored_Wh);
    }

    [Fact]
    public void Solve_PowerAndJouleHeating_MatchClosedForm()
    {
        var r = BatteryPackSolver.Solve(NmcPack());
        Assert.Equal(r.PackLoadedVoltage_V * 100.0, r.PackElectricalPower_W, 9);
        Assert.Equal(100.0 * 100.0 * r.PackInternalResistance_Ohm, r.PackHeatGeneration_W, 9);
        // Charging (negative current) flips power sign; heat stays positive.
        var charging = BatteryPackSolver.Solve(NmcPack() with { LoadCurrent_A = -100.0 });
        Assert.True(charging.PackElectricalPower_W < 0);
        Assert.True(charging.PackHeatGeneration_W > 0);
    }

    [Fact]
    public void ComputeTemperatureCapacityDerating_PiecewiseLinearWithFloor()
    {
        Assert.Equal(1.0, BatteryPackSolver.ComputeTemperatureCapacityDerating(25.0), 12);
        Assert.Equal(1.0, BatteryPackSolver.ComputeTemperatureCapacityDerating(0.0), 12);
        Assert.Equal(1.0, BatteryPackSolver.ComputeTemperatureCapacityDerating(45.0), 12);
        Assert.Equal(0.9, BatteryPackSolver.ComputeTemperatureCapacityDerating(-20.0), 12);  // 1−0.005·20
        Assert.Equal(0.94, BatteryPackSolver.ComputeTemperatureCapacityDerating(65.0), 12);  // 1−0.003·20
        // Absurd cold clamps at the 0.1 floor rather than going negative.
        Assert.Equal(0.1, BatteryPackSolver.ComputeTemperatureCapacityDerating(-500.0), 12);
    }

    [Fact]
    public void Solve_ColdPack_DeratesStoredEnergyOnly()
    {
        var warm = BatteryPackSolver.Solve(NmcPack());
        var cold = BatteryPackSolver.Solve(NmcPack() with { CellTemperature_C = -20.0 });
        Assert.Equal(0.9 * warm.PackEnergyStored_Wh, cold.PackEnergyStored_Wh, 6);
        // Voltage arithmetic is temperature-independent at Wave-1 fidelity.
        Assert.Equal(warm.PackLoadedVoltage_V, cold.PackLoadedVoltage_V, 12);
    }

    [Fact]
    public void Solve_LfpChemistry_UsesItsOwnRegistryAnchors()
    {
        // LFP: 2.5–3.65 V, 20 mΩ. At SoC = 1: OCV = 3.65 V.
        var r = BatteryPackSolver.Solve(NmcPack() with
        {
            Chemistry = BatteryChemistry.LithiumIronPhosphate,
        });
        Assert.Equal(3.65, r.OpenCircuitCellVoltage_V, 12);
        Assert.Equal(96 * 0.020 / 46.0, r.PackInternalResistance_Ohm, 15);
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            BatteryPackSolver.Solve(NmcPack() with { StateOfCharge = 1.1 }));
        Assert.Throws<ArgumentException>(() =>
            BatteryPackSolver.Solve(NmcPack() with { CellsInSeries = 0 }));
        Assert.Throws<ArgumentException>(() =>
            BatteryPackSolver.Solve(NmcPack() with { Chemistry = BatteryChemistry.None }));
    }
}
