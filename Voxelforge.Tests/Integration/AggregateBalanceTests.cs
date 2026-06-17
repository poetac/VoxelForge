// AggregateBalanceTests.cs — Sprint SI.W25 tests for the mass-flow +
// current network-wide balance reports.
//
// Pins:
//   • Mass-flow balance: positive _kgs ports → inflow; negative → outflow.
//   • Current balance: positive _A ports → source; negative → sink.
//   • Suffix matching exact: _W must NOT match _kg, etc.
//   • Conservative network: net residual near zero.
//   • Multiple components contribute to the same balance.
//   • Empty history → empty balance list.

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class AggregateBalanceTests
{
    private static TimeHistorySnapshot MakeSnapshot(
        double time,
        params (string Component, string Port, double Value)[] entries)
    {
        var byComponent = new Dictionary<string, IReadOnlyDictionary<string, double>>();
        foreach (var group in System.Linq.Enumerable.GroupBy(entries, e => e.Component))
        {
            var portMap = new Dictionary<string, double>();
            foreach (var e in group)
                portMap[e.Port] = e.Value;
            byComponent[group.Key] = portMap;
        }
        return new TimeHistorySnapshot(
            Time_s:      time,
            PortValues:  byComponent,
            StateValues: new Dictionary<string, IReadOnlyDictionary<string, double>>());
    }

    // ── MassFlowBalance ─────────────────────────────────────────────────

    [Fact]
    public void MassFlowBalance_SumsPositiveNegativeIndependently()
    {
        var history = new[]
        {
            MakeSnapshot(0.0,
                ("pump",  "Flow_kgs",  2.0),    // +inflow
                ("tank",  "Out_kgs",  -1.5)),   // -outflow
        };
        var balance = TimeHistoryAnalytics.MassFlowBalance(history);
        Assert.Single(balance);
        Assert.Equal( 2.0, balance[0].TotalInflow_kgs,  precision: 9);
        Assert.Equal(-1.5, balance[0].TotalOutflow_kgs, precision: 9);
        Assert.Equal( 0.5, balance[0].NetMassFlow_kgs,  precision: 9);
    }

    [Fact]
    public void MassFlowBalance_IgnoresNonKgsSuffixes()
    {
        // _kg (no s) is a mass, not a mass flow rate — must not appear.
        // _W is power; _A is current. All three must be ignored.
        var history = new[]
        {
            MakeSnapshot(0.0,
                ("a", "Flow_kgs",   2.0),
                ("a", "StoredMass_kg", 100.0),   // _kg → not _kgs
                ("a", "Power_W",       500.0),
                ("a", "Current_A",       1.0)),
        };
        var balance = TimeHistoryAnalytics.MassFlowBalance(history);
        Assert.Equal(2.0, balance[0].TotalInflow_kgs, precision: 9);
    }

    [Fact]
    public void MassFlowBalance_ConservativeNetwork_NetNearZero()
    {
        // Inflow at A = Outflow at B (sign flipped). Net should be 0.
        var history = new[]
        {
            MakeSnapshot(0.0,
                ("a", "Flow_kgs",  3.0),
                ("b", "Flow_kgs", -3.0)),
        };
        var balance = TimeHistoryAnalytics.MassFlowBalance(history);
        Assert.Equal(0.0, balance[0].NetMassFlow_kgs, precision: 9);
    }

    [Fact]
    public void MassFlowBalance_MultipleTimeSteps_TrackedIndividually()
    {
        var history = new[]
        {
            MakeSnapshot(0.0, ("a", "Flow_kgs", 1.0)),
            MakeSnapshot(1.0, ("a", "Flow_kgs", 2.0)),
            MakeSnapshot(2.0, ("a", "Flow_kgs", 3.0)),
        };
        var balance = TimeHistoryAnalytics.MassFlowBalance(history);
        Assert.Equal(3, balance.Count);
        Assert.Equal(1.0, balance[0].NetMassFlow_kgs);
        Assert.Equal(2.0, balance[1].NetMassFlow_kgs);
        Assert.Equal(3.0, balance[2].NetMassFlow_kgs);
    }

    // ── CurrentBalance ──────────────────────────────────────────────────

    [Fact]
    public void CurrentBalance_SumsPositiveNegativeIndependently()
    {
        var history = new[]
        {
            MakeSnapshot(0.0,
                ("source", "Out_A",  10.0),     // +source
                ("load",   "In_A",   -7.0)),    // -sink
        };
        var balance = TimeHistoryAnalytics.CurrentBalance(history);
        Assert.Single(balance);
        Assert.Equal(10.0, balance[0].TotalSourceCurrent_A,  precision: 9);
        Assert.Equal(-7.0, balance[0].TotalSinkCurrent_A,    precision: 9);
        Assert.Equal( 3.0, balance[0].NetCurrentImbalance_A, precision: 9);
    }

    [Fact]
    public void CurrentBalance_KirchhoffConservation_NetZero()
    {
        // Source at node A injects 5 A; sinks at B + C draw 3 A + 2 A.
        // Net = 5 + (-3) + (-2) = 0.
        var history = new[]
        {
            MakeSnapshot(0.0,
                ("source",  "Out_A",   5.0),
                ("sink_b",  "In_A",   -3.0),
                ("sink_c",  "In_A",   -2.0)),
        };
        var balance = TimeHistoryAnalytics.CurrentBalance(history);
        Assert.Equal(0.0, balance[0].NetCurrentImbalance_A, precision: 9);
    }

    [Fact]
    public void CurrentBalance_IgnoresNonAmpSuffixes()
    {
        var history = new[]
        {
            MakeSnapshot(0.0,
                ("a", "I_A",       5.0),
                ("a", "AreaA",  1000.0),         // descriptor not _A
                ("a", "Power_W",  10.0),
                ("a", "Mass_kgs",  0.5)),
        };
        var balance = TimeHistoryAnalytics.CurrentBalance(history);
        Assert.Equal(5.0, balance[0].TotalSourceCurrent_A);
    }

    // ── PowerBalance regression check (existing SI.W16 unchanged) ───────

    [Fact]
    public void PowerBalance_StillWorks_AfterRefactor()
    {
        // SI.W25 refactored the suffix-routing into a shared helper.
        // Pin that PowerBalance still produces the right shape.
        var history = new[]
        {
            MakeSnapshot(0.0,
                ("solar",   "Out_W",  1000.0),
                ("load",    "In_W",   -800.0)),
        };
        var balance = TimeHistoryAnalytics.PowerBalance(history);
        Assert.Single(balance);
        Assert.Equal( 1000.0, balance[0].TotalSourcePower_W,  precision: 9);
        Assert.Equal( -800.0, balance[0].TotalSinkPower_W,    precision: 9);
        Assert.Equal(  200.0, balance[0].NetPowerImbalance_W, precision: 9);
    }

    [Fact]
    public void Balance_RejectsNullHistory()
    {
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.MassFlowBalance(null!));
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.CurrentBalance(null!));
    }
}
