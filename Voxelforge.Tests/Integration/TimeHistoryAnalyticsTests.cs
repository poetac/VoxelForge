// TimeHistoryAnalyticsTests.cs — Sprint SI.W16 unit tests for
// trapezoidal integration helpers + system power balance reporter.

using System;
using System.Linq;
using Voxelforge.Battery;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class TimeHistoryAnalyticsTests
{
    // ── IntegrateOverTime ────────────────────────────────────────────────

    [Fact]
    public void IntegrateOverTime_ConstantSource_EqualsRateTimesDuration()
    {
        // Accumulator with constant Input_rate = 1 → its
        // Accumulated_total grows linearly. Integrating the rate by
        // hand should be ≈ rate · duration. We use the
        // Accumulated_total output itself (the result of integration),
        // not the rate, so we expect ∫(t)dt = t²/2 over [0, T].
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc", initial: 0.0);
        net.Add(acc);
        net.SetExternalInput("acc", "Input_rate", 1.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("acc", acc);
        var hist = integrator.Run(0.0, 10.0, 1.0);

        // ∫_{0}^{10} t dt = 50 — trapezoidal rule on linear data is
        // exact. Under #553 closed [t0, tEnd] N+1 semantics the history
        // covers 11 ticks at t = 0..10 with Accumulated_total = 0..10,
        // and the trapezoidal integral is 0+1+2+...+9 + 0.5·10 = 50 —
        // bang-on the analytical answer (the closed-interval contract
        // recovers the missing endpoint contribution that the prior
        // half-open form dropped, which had given 40.5).
        double area = TimeHistoryAnalytics.IntegrateOverTime(hist, "acc",
            "Accumulated_total");
        Assert.InRange(area, 49.0, 51.0);
    }

    [Fact]
    public void IntegrateOverTime_SingleSnapshot_ReturnsZero()
    {
        // A history of length 1 cannot be integrated — fall back to 0.
        // Under #553 closed [t0, tEnd] N+1 semantics the smallest
        // history is round((tEnd-t0)/dt)+1 samples. Run(0, 0.5, 1)
        // gives round(0.5)+1 = 1 (banker's rounding floors 0.5) — the
        // genuine single-snapshot degenerate case this test pins.
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc");
        net.Add(acc);
        net.SetExternalInput("acc", "Input_rate", 1.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("acc", acc);
        var hist = integrator.Run(0.0, 0.5, 1.0);   // single snapshot at t=0
        Assert.Single(hist);
        double area = TimeHistoryAnalytics.IntegrateOverTime(hist, "acc",
            "Accumulated_total");
        Assert.Equal(0.0, area, precision: 12);
    }

    // ── MaxOf / MinOf ────────────────────────────────────────────────────

    [Fact]
    public void MaxAndMin_OnDischargingBattery_BracketSoC()
    {
        // Battery SoC drops monotonically under discharge → Max == 1.0,
        // Min == final SoC.
        var net = new ComponentNetwork();
        var battery = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 1.0);
        net.Add(battery);
        net.SetExternalInput("pack", "LoadCurrent_A", 100.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", battery);
        var hist = integrator.Run(0.0, 300.0, 1.0);
        Assert.Equal(1.0,
            TimeHistoryAnalytics.MaxOf(hist, "pack", "StateOfCharge"),
            precision: 6);
        Assert.True(
            TimeHistoryAnalytics.MinOf(hist, "pack", "StateOfCharge")
            < 1.0);
    }

    // ── PowerBalance ─────────────────────────────────────────────────────

    [Fact]
    public void PowerBalance_OnSimpleSourceSink_IsNonNegative()
    {
        // A battery under discharge emits PackElectricalPower_W > 0
        // (source). With no consumer in the network, the imbalance
        // equals the source.
        var net = new ComponentNetwork();
        var battery = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 1.0);
        net.Add(battery);
        net.SetExternalInput("pack", "LoadCurrent_A", 100.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", battery);
        var hist = integrator.Run(0.0, 60.0, 1.0);

        var bal = TimeHistoryAnalytics.PowerBalance(hist);
        Assert.Equal(hist.Count, bal.Count);
        foreach (var tick in bal)
        {
            // Sources >= 0, sinks <= 0, net is the unbalanced source.
            Assert.True(tick.TotalSourcePower_W >= 0.0);
            Assert.True(tick.TotalSinkPower_W   <= 0.0);
            Assert.True(tick.NetPowerImbalance_W >= 0.0);
        }
    }

    [Fact]
    public void PowerBalance_OnEmptyNetwork_RendersZeroBalances()
    {
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc");
        net.Add(acc);
        net.SetExternalInput("acc", "Input_rate", 0.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("acc", acc);
        var hist = integrator.Run(0.0, 3.0, 1.0);

        var bal = TimeHistoryAnalytics.PowerBalance(hist);
        // Accumulator has no "_W" suffixed ports — balance is zero.
        Assert.All(bal, tick => Assert.Equal(0.0, tick.NetPowerImbalance_W,
            precision: 6));
    }

    private static BatteryPackDesign ModelSPack() => new(
        Chemistry:        BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries:    96,
        ParallelStrings:  46,
        StateOfCharge:    1.0,
        LoadCurrent_A:    0.0);
}
