// NoyronTierC2Tests.cs — Tier C2 forcing-function suite for the
// preburner + engine-cycle topology.
//
// Coverage
// ────────
//   • EngineCycle enum: StagedCombustion + FullFlow members present.
//   • PreburnerChamber.SuggestPreburnerMr returns propellant-appropriate
//     fuel-rich values; zero for no-preburner cycles.
//   • PreburnerChamber.Size returns null for PressureFed /
//     ElectricPump / OpenExpander.
//   • PreburnerChamber.Size populates every field for a gas-generator
//     LOX/CH4 preburner.
//   • PreburnerChamber.Size emits FFSC simplification warning.
//   • PreburnerChamber.Size throws on non-positive MR / Pc / mdot.
//   • Hot preburner (MR too close to stoichiometric) emits warning.
//   • OperatingConditions round-trips the new PreburnerMrRatio /
//     PreburnerChamberPressure_Pa fields through JSON.
//   • AutoSeeder with EngineCycleOverride populates cond.EngineCycle +
//     cond.PreburnerMrRatio; rationale mentions the cycle.
//   • AutoSeeder with no cycle override leaves cond.EngineCycle at
//     PressureFed.
//
// All tests pure-math — no PicoGK Library initialisation required.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class NoyronTierC2Tests
{
    // ══════════════════════ Enum + helpers ══════════════════════

    [Fact]
    public void EngineCycle_HasStagedCombustion_AndFullFlow()
    {
        var values = System.Enum.GetValues<EngineCycle>();
        Assert.Contains(EngineCycle.StagedCombustion, values);
        Assert.Contains(EngineCycle.FullFlow, values);
    }

    [Theory]
    [InlineData(PropellantPair.LOX_CH4, 0.60)]
    [InlineData(PropellantPair.LOX_H2,  0.80)]
    [InlineData(PropellantPair.LOX_RP1, 0.40)]
    public void SuggestPreburnerMr_ReturnsFuelRich_ByPair(PropellantPair pair, double expected)
    {
        double mr = PreburnerChamber.SuggestPreburnerMr(EngineCycle.GasGenerator, pair);
        Assert.Equal(expected, mr, 6);
    }

    [Theory]
    [InlineData(EngineCycle.PressureFed)]
    [InlineData(EngineCycle.ElectricPump)]
    [InlineData(EngineCycle.OpenExpander)]
    public void SuggestPreburnerMr_ReturnsZero_ForNoPreburnerCycles(EngineCycle cycle)
    {
        double mr = PreburnerChamber.SuggestPreburnerMr(cycle, PropellantPair.LOX_CH4);
        Assert.Equal(0, mr, 6);
    }

    // ══════════════════════ Sizing ══════════════════════

    [Theory]
    [InlineData(EngineCycle.PressureFed)]
    [InlineData(EngineCycle.ElectricPump)]
    [InlineData(EngineCycle.OpenExpander)]
    public void Size_ReturnsNull_ForNoPreburnerCycles(EngineCycle cycle)
    {
        var r = PreburnerChamber.Size(cycle, PropellantPair.LOX_CH4, 0.6, 10e6, 2.0);
        Assert.Null(r);
    }

    [Fact]
    public void Size_GasGenerator_PopulatesFields()
    {
        var r = PreburnerChamber.Size(
            EngineCycle.GasGenerator, PropellantPair.LOX_CH4,
            preburnerMr: 0.6, preburnerPc_Pa: 10e6, turbineMassFlow_kgs: 0.5);
        Assert.NotNull(r);
        Assert.Equal(EngineCycle.GasGenerator, r!.Cycle);
        Assert.Equal(0.6, r.MixtureRatio, 6);
        Assert.Equal(10e6, r.ChamberPressure_Pa, 3);
        Assert.True(r.WarmGasTemperature_K > 0);
        Assert.True(r.WarmGasCStar_ms > 0);
        Assert.InRange(r.WarmGasGamma, 1.0, 2.0);
        Assert.True(r.WarmGasMolecularWeight > 0);
        Assert.Equal(0.5, r.MassFlow_kgs, 6);
        Assert.True(r.CharacteristicLength_m > 0);
        Assert.True(r.ChamberVolume_mm3 > 0);
        Assert.NotEmpty(r.Notes);
    }

    [Theory]
    [InlineData(EngineCycle.StagedCombustion)]
    [InlineData(EngineCycle.FullFlow)]
    public void Size_StagedAndFullFlow_PopulateResults(EngineCycle cycle)
    {
        var r = PreburnerChamber.Size(
            cycle, PropellantPair.LOX_CH4,
            preburnerMr: 0.6, preburnerPc_Pa: 15e6, turbineMassFlow_kgs: 2.0);
        Assert.NotNull(r);
        Assert.Equal(cycle, r!.Cycle);
    }

    [Fact]
    public void Size_FullFlow_EmitsDualPreburnerSimplificationWarning()
    {
        // The FullFlow single-preburner call still emits a warning
        // steering the caller to SizeFfscDual. Exact wording changed
        // from "deferred" to "SizeFfscDual" when the dual-call helper
        // shipped, but the warning must still fire when the
        // single-preburner entry point is used.
        var r = PreburnerChamber.Size(
            EngineCycle.FullFlow, PropellantPair.LOX_CH4,
            preburnerMr: 0.6, preburnerPc_Pa: 15e6, turbineMassFlow_kgs: 2.0);
        Assert.NotNull(r);
        Assert.Contains(r!.Warnings, w =>
            w.Contains("SizeFfscDual", System.StringComparison.Ordinal)
            || w.Contains("dual-preburner", System.StringComparison.OrdinalIgnoreCase)
            || w.Contains("Dual-preburner", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Size_NonPositivePc_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PreburnerChamber.Size(EngineCycle.GasGenerator, PropellantPair.LOX_CH4,
                preburnerMr: 0.6, preburnerPc_Pa: 0, turbineMassFlow_kgs: 0.5));
    }

    [Fact]
    public void Size_NonPositiveMassFlow_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PreburnerChamber.Size(EngineCycle.GasGenerator, PropellantPair.LOX_CH4,
                preburnerMr: 0.6, preburnerPc_Pa: 10e6, turbineMassFlow_kgs: 0));
    }

    [Fact]
    public void Size_NonPositiveMr_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PreburnerChamber.Size(EngineCycle.GasGenerator, PropellantPair.LOX_CH4,
                preburnerMr: 0, preburnerPc_Pa: 10e6, turbineMassFlow_kgs: 0.5));
    }

    [Fact]
    public void Size_HotPreburner_EmitsTurbineWarning()
    {
        // Crank MR toward stoichiometric (LOX/CH4 stoich ≈ 3.99) so
        // T_c exceeds the 1100 K turbine ceiling.
        var r = PreburnerChamber.Size(
            EngineCycle.GasGenerator, PropellantPair.LOX_CH4,
            preburnerMr: 3.3, preburnerPc_Pa: 10e6, turbineMassFlow_kgs: 0.5);
        Assert.NotNull(r);
        Assert.True(r!.WarmGasTemperature_K > PreburnerChamber.TurbineInletTempLimit_K,
            $"expected hot preburner, got T_c = {r.WarmGasTemperature_K}");
        Assert.Contains(r.Warnings, w => w.Contains("turbine-safe", System.StringComparison.OrdinalIgnoreCase));
    }

    // ══════════════════════ OperatingConditions round-trip ══════════════════════

    [Fact]
    public void OperatingConditions_PreburnerFields_DefaultToZero()
    {
        var c = new OperatingConditions();
        Assert.Equal(0.0, c.PreburnerMrRatio, 6);
        Assert.Equal(0.0, c.PreburnerChamberPressure_Pa, 6);
    }

    [Fact]
    public void OperatingConditions_PreburnerFields_RoundTripViaWith()
    {
        var c = new OperatingConditions() with
        {
            EngineCycle                = EngineCycle.StagedCombustion,
            PreburnerMrRatio           = 0.55,
            PreburnerChamberPressure_Pa = 18e6,
        };
        Assert.Equal(EngineCycle.StagedCombustion, c.EngineCycle);
        Assert.Equal(0.55, c.PreburnerMrRatio, 6);
        Assert.Equal(18e6, c.PreburnerChamberPressure_Pa, 3);
    }

    // ══════════════════════ AutoSeeder cycle override ══════════════════════

    [Fact]
    public void AutoSeeder_NoCycleOverride_LeavesPressureFed()
    {
        var spec = new EngineSpec(
            PropellantPair:     PropellantPair.LOX_CH4,
            Thrust_N:           20000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0);
        var seed = AutoSeeder.Seed(spec);
        Assert.Equal(EngineCycle.PressureFed, seed.Conditions.EngineCycle);
        Assert.Equal(0, seed.Conditions.PreburnerMrRatio, 6);
    }

    [Theory]
    [InlineData(EngineCycle.GasGenerator)]
    [InlineData(EngineCycle.StagedCombustion)]
    [InlineData(EngineCycle.FullFlow)]
    public void AutoSeeder_CycleOverride_SetsCycleAndPreburnerMr(EngineCycle cycle)
    {
        var spec = new EngineSpec(
            PropellantPair:      PropellantPair.LOX_CH4,
            Thrust_N:            20000,
            ChamberPressure_Pa:  7e6,
            ExpansionRatio:      15.0,
            EngineCycleOverride: cycle);
        var seed = AutoSeeder.Seed(spec);
        Assert.Equal(cycle, seed.Conditions.EngineCycle);
        Assert.True(seed.Conditions.PreburnerMrRatio > 0);
        Assert.True(seed.Conditions.PreburnerChamberPressure_Pa > 0);
        Assert.Contains(seed.Rationale, r => r.Contains("Engine cycle"));
    }

    [Fact]
    public void AutoSeeder_StagedCombustion_PreburnerPcIs_1_5x_Main()
    {
        var spec = new EngineSpec(
            PropellantPair:      PropellantPair.LOX_CH4,
            Thrust_N:            20000,
            ChamberPressure_Pa:  10e6,
            ExpansionRatio:      15.0,
            EngineCycleOverride: EngineCycle.StagedCombustion);
        var seed = AutoSeeder.Seed(spec);
        Assert.Equal(15e6, seed.Conditions.PreburnerChamberPressure_Pa, 3);
    }

    [Fact]
    public void AutoSeeder_GasGenerator_PreburnerPcIs_1_2x_Main()
    {
        var spec = new EngineSpec(
            PropellantPair:      PropellantPair.LOX_CH4,
            Thrust_N:            20000,
            ChamberPressure_Pa:  10e6,
            ExpansionRatio:      15.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        var seed = AutoSeeder.Seed(spec);
        Assert.Equal(12e6, seed.Conditions.PreburnerChamberPressure_Pa, 3);
    }
}
