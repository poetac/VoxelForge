// BatteryPackSolverTests.cs — Sprint BP.W1 unit tests for the
// closed-form battery pack performance snapshot.

using System;
using Voxelforge.Battery;
using Xunit;

namespace Voxelforge.Tests.Battery;

public sealed class BatteryPackSolverTests
{
    // ── BatteryChemistryRegistry ─────────────────────────────────────────

    [Fact]
    public void Registry_NMC_HasExpectedClusterAnchors()
    {
        var p = BatteryChemistryRegistry.NickelManganeseCobalt;
        Assert.Equal(3.0,   p.OcvMin_V,               precision: 6);
        Assert.Equal(4.2,   p.OcvMax_V,               precision: 6);
        Assert.Equal(0.030, p.InternalResistance_Ohm, precision: 6);
        Assert.Equal(5.0,   p.NominalCapacity_Ah,     precision: 6);
    }

    [Fact]
    public void Registry_LFP_LowerVoltageHigherCycleLife()
    {
        var nmc = BatteryChemistryRegistry.NickelManganeseCobalt;
        var lfp = BatteryChemistryRegistry.LithiumIronPhosphate;
        Assert.True(lfp.OcvMax_V < nmc.OcvMax_V);
        Assert.True(lfp.OcvMin_V < nmc.OcvMin_V);
        // LFP cluster R_int is lower than NMC cluster.
        Assert.True(lfp.InternalResistance_Ohm < nmc.InternalResistance_Ohm);
    }

    [Fact]
    public void Registry_For_ThrowsOnNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BatteryChemistryRegistry.For(BatteryChemistry.None));
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneChemistry()
    {
        var d = ModelThreeLongRange() with { Chemistry = BatteryChemistry.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsSoCOutOfBand()
    {
        Assert.Throws<ArgumentException>(
            () => (ModelThreeLongRange() with { StateOfCharge = -0.1 }).ValidateSelf());
        Assert.Throws<ArgumentException>(
            () => (ModelThreeLongRange() with { StateOfCharge =  1.1 }).ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveCellCounts()
    {
        Assert.Throws<ArgumentException>(
            () => (ModelThreeLongRange() with { CellsInSeries  = 0 }).ValidateSelf());
        Assert.Throws<ArgumentException>(
            () => (ModelThreeLongRange() with { ParallelStrings = 0 }).ValidateSelf());
    }

    // ── Tesla Model 3 LR baseline ────────────────────────────────────────

    [Fact]
    public void ModelThreeLR_FullSoC_OpenCircuitPackVoltageNearFourHundred()
    {
        // 96 cells × 4.2 V = 403.2 V at full SoC (Tesla nominal 400 V).
        var r = BatteryPackSolver.Solve(ModelThreeLongRange());
        Assert.Equal(403.2, r.PackOpenCircuitVoltage_V, precision: 2);
    }

    [Fact]
    public void ModelThreeLR_FullSoC_StoredEnergyInClusterBand()
    {
        // E = 96·46·5 Ah·3.6 V = 79,488 Wh ≈ 79.5 kWh. Cluster band
        // [70 kWh, 90 kWh] swallows per-cell capacity variation (the
        // real 2170-format cells run ≈ 5.0-5.5 Ah).
        var r = BatteryPackSolver.Solve(ModelThreeLongRange());
        Assert.InRange(r.PackEnergyStored_Wh, 70_000.0, 90_000.0);
    }

    [Fact]
    public void ModelThreeLR_DischargeAt200A_PowerInClusterBand()
    {
        var d = ModelThreeLongRange() with { LoadCurrent_A = 200.0 };
        var r = BatteryPackSolver.Solve(d);
        // P = V·I ≈ 390 V · 200 A = 78 kW. Cluster band [70 kW, 85 kW].
        Assert.InRange(r.PackElectricalPower_W, 70_000.0, 85_000.0);
    }

    [Fact]
    public void DischargeReducesLoadedVoltage_VsOpenCircuit()
    {
        var d = ModelThreeLongRange() with { LoadCurrent_A = 200.0 };
        var r = BatteryPackSolver.Solve(d);
        // Pack-loaded V must be lower than pack-OC V on discharge.
        Assert.True(r.PackLoadedVoltage_V < r.PackOpenCircuitVoltage_V);
    }

    [Fact]
    public void ChargeIncreasesLoadedVoltage_VsOpenCircuit()
    {
        // I_pack < 0 (charge) → V_loaded > V_oc.
        var d = ModelThreeLongRange() with { LoadCurrent_A = -200.0 };
        var r = BatteryPackSolver.Solve(d);
        Assert.True(r.PackLoadedVoltage_V > r.PackOpenCircuitVoltage_V);
    }

    [Fact]
    public void HeatGenerationIsAlwaysNonNegative()
    {
        // Q = I²·R is positive whether charging or discharging.
        var dDischarge = ModelThreeLongRange() with { LoadCurrent_A =  200.0 };
        var dCharge    = ModelThreeLongRange() with { LoadCurrent_A = -200.0 };
        Assert.True(BatteryPackSolver.Solve(dDischarge).PackHeatGeneration_W > 0);
        Assert.True(BatteryPackSolver.Solve(dCharge).PackHeatGeneration_W    > 0);
    }

    [Fact]
    public void ZeroLoad_HeatGenerationIsZero_LoadedEqualsOpenCircuit()
    {
        var d = ModelThreeLongRange() with { LoadCurrent_A = 0.0 };
        var r = BatteryPackSolver.Solve(d);
        Assert.Equal(0.0, r.PackHeatGeneration_W, precision: 9);
        Assert.Equal(r.PackOpenCircuitVoltage_V, r.PackLoadedVoltage_V, precision: 9);
    }

    [Fact]
    public void EmptySoC_StoredEnergyIsZero()
    {
        var d = ModelThreeLongRange() with { StateOfCharge = 0.0, LoadCurrent_A = 0.0 };
        var r = BatteryPackSolver.Solve(d);
        Assert.Equal(0.0, r.PackEnergyStored_Wh, precision: 6);
    }

    [Fact]
    public void PackInternalResistance_ScalesWithSeriesOverParallel()
    {
        // R_pack = (N_s · R_cell) / N_p. Doubling N_p halves R_pack.
        var oneRow  = ModelThreeLongRange() with { ParallelStrings = 46 };
        var twoRows = ModelThreeLongRange() with { ParallelStrings = 92 };
        var r1 = BatteryPackSolver.Solve(oneRow);
        var r2 = BatteryPackSolver.Solve(twoRows);
        Assert.Equal(0.5, r2.PackInternalResistance_Ohm / r1.PackInternalResistance_Ohm,
            precision: 9);
    }

    // ── LFP comparison ───────────────────────────────────────────────────

    [Fact]
    public void LFP_LowerPackVoltage_ThanNMC_AtSameSeriesCount()
    {
        var nmc = ModelThreeLongRange() with { Chemistry = BatteryChemistry.NickelManganeseCobalt };
        var lfp = ModelThreeLongRange() with { Chemistry = BatteryChemistry.LithiumIronPhosphate };
        var rNmc = BatteryPackSolver.Solve(nmc);
        var rLfp = BatteryPackSolver.Solve(lfp);
        Assert.True(rLfp.PackOpenCircuitVoltage_V < rNmc.PackOpenCircuitVoltage_V);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Tesla Model 3 Long-Range pack baseline (96s46p NMC, ~ 82 kWh).
    private static BatteryPackDesign ModelThreeLongRange() => new(
        Chemistry:        BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries:    96,
        ParallelStrings:  46,
        StateOfCharge:    1.0,
        LoadCurrent_A:    0.0);
}
