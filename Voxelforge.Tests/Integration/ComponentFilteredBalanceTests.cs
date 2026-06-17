// ComponentFilteredBalanceTests.cs — Sprint SI.W27 tests for the
// subsystem-filtered balance reports on TimeHistoryAnalytics.

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class ComponentFilteredBalanceTests
{
    private static TimeHistorySnapshot MakeSnapshot(
        double time,
        params (string Component, string Port, double Value)[] entries)
    {
        var byComponent = new Dictionary<string, IReadOnlyDictionary<string, double>>();
        foreach (var group in System.Linq.Enumerable.GroupBy(entries, e => e.Component))
        {
            var portMap = new Dictionary<string, double>();
            foreach (var e in group) portMap[e.Port] = e.Value;
            byComponent[group.Key] = portMap;
        }
        return new TimeHistorySnapshot(
            Time_s:      time,
            PortValues:  byComponent,
            StateValues: new Dictionary<string, IReadOnlyDictionary<string, double>>());
    }

    [Fact]
    public void PowerBalanceFor_OnlyCountsFilteredComponents()
    {
        var hist = new[]
        {
            MakeSnapshot(0.0,
                ("ev_battery",     "Out_W",    1000.0),
                ("ev_motor",       "In_W",     -800.0),
                ("therm_radiator", "Reject_W", -200.0),
                ("therm_pump",     "Draw_W",    -50.0)),
        };
        var ev = TimeHistoryAnalytics.PowerBalanceFor(hist,
            c => c.StartsWith("ev_", System.StringComparison.Ordinal));
        Assert.Equal( 1000.0, ev[0].TotalSourcePower_W,   precision: 9);
        Assert.Equal( -800.0, ev[0].TotalSinkPower_W,     precision: 9);
        Assert.Equal(  200.0, ev[0].NetPowerImbalance_W,  precision: 9);

        var therm = TimeHistoryAnalytics.PowerBalanceFor(hist,
            c => c.StartsWith("therm_", System.StringComparison.Ordinal));
        Assert.Equal(   0.0, therm[0].TotalSourcePower_W, precision: 9);
        Assert.Equal(-250.0, therm[0].TotalSinkPower_W,   precision: 9);
        Assert.Equal(-250.0, therm[0].NetPowerImbalance_W, precision: 9);
    }

    [Fact]
    public void PowerBalanceFor_EmptyFilter_ProducesAllZeros()
    {
        var hist = new[] { MakeSnapshot(0.0, ("a", "P_W", 100.0)) };
        var b = TimeHistoryAnalytics.PowerBalanceFor(hist, _ => false);
        Assert.Single(b);
        Assert.Equal(0.0, b[0].TotalSourcePower_W);
        Assert.Equal(0.0, b[0].TotalSinkPower_W);
        Assert.Equal(0.0, b[0].NetPowerImbalance_W);
    }

    [Fact]
    public void PowerBalanceFor_PassAllFilter_MatchesUnfilteredPowerBalance()
    {
        var hist = new[]
        {
            MakeSnapshot(0.0, ("a", "P_W",  100.0), ("b", "P_W", -100.0)),
            MakeSnapshot(1.0, ("a", "P_W",  200.0), ("b", "P_W", -200.0)),
        };
        var unfiltered = TimeHistoryAnalytics.PowerBalance(hist);
        var filtered   = TimeHistoryAnalytics.PowerBalanceFor(hist, _ => true);
        Assert.Equal(unfiltered.Count, filtered.Count);
        for (int i = 0; i < unfiltered.Count; i++)
        {
            Assert.Equal(unfiltered[i].TotalSourcePower_W,  filtered[i].TotalSourcePower_W);
            Assert.Equal(unfiltered[i].TotalSinkPower_W,    filtered[i].TotalSinkPower_W);
            Assert.Equal(unfiltered[i].NetPowerImbalance_W, filtered[i].NetPowerImbalance_W);
        }
    }

    [Fact]
    public void MassFlowBalanceFor_OnlyCountsFilteredComponents()
    {
        var hist = new[]
        {
            MakeSnapshot(0.0,
                ("propellant_pump", "Flow_kgs",    2.0),
                ("propellant_tank", "Drain_kgs",  -2.0),
                ("coolant_pump",    "Flow_kgs",    0.5)),
        };
        var prop = TimeHistoryAnalytics.MassFlowBalanceFor(hist,
            c => c.StartsWith("propellant_", System.StringComparison.Ordinal));
        Assert.Equal( 2.0, prop[0].TotalInflow_kgs);
        Assert.Equal(-2.0, prop[0].TotalOutflow_kgs);
        Assert.Equal( 0.0, prop[0].NetMassFlow_kgs);

        var cool = TimeHistoryAnalytics.MassFlowBalanceFor(hist,
            c => c.StartsWith("coolant_", System.StringComparison.Ordinal));
        Assert.Equal(0.5, cool[0].TotalInflow_kgs);
        Assert.Equal(0.0, cool[0].TotalOutflow_kgs);
    }

    [Fact]
    public void CurrentBalanceFor_OnlyCountsFilteredComponents()
    {
        var hist = new[]
        {
            MakeSnapshot(0.0,
                ("ev_battery", "Out_A",  10.0),
                ("ev_motor",   "In_A",   -8.0),
                ("avionics",   "Draw_A", -1.0)),
        };
        var ev = TimeHistoryAnalytics.CurrentBalanceFor(hist,
            c => c.StartsWith("ev_", System.StringComparison.Ordinal));
        Assert.Equal(10.0, ev[0].TotalSourceCurrent_A);
        Assert.Equal(-8.0, ev[0].TotalSinkCurrent_A);
        Assert.Equal( 2.0, ev[0].NetCurrentImbalance_A);
    }

    [Fact]
    public void BalanceFor_RejectsNullFilter()
    {
        var hist = new[] { MakeSnapshot(0.0, ("a", "P_W", 100.0)) };
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.PowerBalanceFor(hist, null!));
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.MassFlowBalanceFor(hist, null!));
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.CurrentBalanceFor(hist, null!));
    }

    [Fact]
    public void BalanceFor_RejectsNullHistory()
    {
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.PowerBalanceFor(null!, _ => true));
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.MassFlowBalanceFor(null!, _ => true));
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.CurrentBalanceFor(null!, _ => true));
    }
}
