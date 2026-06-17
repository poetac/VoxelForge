// CumulativeAggregatorTests.cs — Sprint SI.W26 tests for the
// cumulative-network-wide aggregators on TimeHistoryAnalytics.
//
// Pins:
//   • Cumulative energy: trapezoidal integral of net power.
//   • Cumulative mass: trapezoidal integral of net mass flow.
//   • Cumulative charge: trapezoidal integral of net current.
//   • First-tick cumulative is exactly 0 (no prior interval).
//   • Constant-rate analytical match: cumulative at t_end ≈ rate × t_end.
//   • Conservative-network running residual stays at zero.
//   • Empty history → empty cumulative list.
//   • Null guard.

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class CumulativeAggregatorTests
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

    // ── CumulativeEnergy_J ──────────────────────────────────────────────

    [Fact]
    public void CumulativeEnergy_FirstTickIsZero()
    {
        var hist = new[] { MakeSnapshot(0.0, ("a", "P_W", 100.0)) };
        var cum = TimeHistoryAnalytics.CumulativeEnergy_J(hist);
        Assert.Single(cum);
        Assert.Equal(0.0, cum[0].CumulativeEnergy_J);
    }

    [Fact]
    public void CumulativeEnergy_ConstantRate_LinearGrowth()
    {
        // Constant net power of 100 W. After 5 s should be 500 J.
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src", "P_W", 100.0)),
            MakeSnapshot(1.0, ("src", "P_W", 100.0)),
            MakeSnapshot(2.0, ("src", "P_W", 100.0)),
            MakeSnapshot(3.0, ("src", "P_W", 100.0)),
            MakeSnapshot(4.0, ("src", "P_W", 100.0)),
            MakeSnapshot(5.0, ("src", "P_W", 100.0)),
        };
        var cum = TimeHistoryAnalytics.CumulativeEnergy_J(hist);
        Assert.Equal(   0.0, cum[0].CumulativeEnergy_J);
        Assert.Equal( 100.0, cum[1].CumulativeEnergy_J, precision: 9);
        Assert.Equal( 200.0, cum[2].CumulativeEnergy_J, precision: 9);
        Assert.Equal( 500.0, cum[5].CumulativeEnergy_J, precision: 9);
    }

    [Fact]
    public void CumulativeEnergy_RampedPower_TrapezoidalAccurate()
    {
        // Linear ramp from 0 W to 100 W over 1 s. Trapezoidal integral
        // matches analytical (½·base·height = 50 J).
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src", "P_W",   0.0)),
            MakeSnapshot(1.0, ("src", "P_W", 100.0)),
        };
        var cum = TimeHistoryAnalytics.CumulativeEnergy_J(hist);
        Assert.Equal(50.0, cum[1].CumulativeEnergy_J, precision: 9);
    }

    [Fact]
    public void CumulativeEnergy_ConservativeNetwork_StaysNearZero()
    {
        // Source = -Sink at every tick → cumulative residual = 0.
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src", "P_W",  100.0), ("sink", "P_W", -100.0)),
            MakeSnapshot(1.0, ("src", "P_W",  200.0), ("sink", "P_W", -200.0)),
            MakeSnapshot(2.0, ("src", "P_W",  150.0), ("sink", "P_W", -150.0)),
        };
        var cum = TimeHistoryAnalytics.CumulativeEnergy_J(hist);
        foreach (var t in cum)
            Assert.Equal(0.0, t.CumulativeEnergy_J, precision: 9);
    }

    // ── CumulativeMass_kg ───────────────────────────────────────────────

    [Fact]
    public void CumulativeMass_ConstantRate_LinearGrowth()
    {
        // Constant 2 kg/s for 3 s → 6 kg.
        var hist = new[]
        {
            MakeSnapshot(0.0, ("pump", "Flow_kgs", 2.0)),
            MakeSnapshot(1.0, ("pump", "Flow_kgs", 2.0)),
            MakeSnapshot(2.0, ("pump", "Flow_kgs", 2.0)),
            MakeSnapshot(3.0, ("pump", "Flow_kgs", 2.0)),
        };
        var cum = TimeHistoryAnalytics.CumulativeMass_kg(hist);
        Assert.Equal(0.0, cum[0].CumulativeMass_kg);
        Assert.Equal(6.0, cum[3].CumulativeMass_kg, precision: 9);
    }

    [Fact]
    public void CumulativeMass_ConservativeNetwork_StaysNearZero()
    {
        // Source inflow + tank outflow always sum to 0.
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src", "Flow_kgs", 1.0), ("tank", "Out_kgs", -1.0)),
            MakeSnapshot(1.0, ("src", "Flow_kgs", 2.0), ("tank", "Out_kgs", -2.0)),
        };
        var cum = TimeHistoryAnalytics.CumulativeMass_kg(hist);
        foreach (var t in cum)
            Assert.Equal(0.0, t.CumulativeMass_kg, precision: 9);
    }

    // ── CumulativeCharge_C ──────────────────────────────────────────────

    [Fact]
    public void CumulativeCharge_ConstantCurrent_AmpereSecondAccurate()
    {
        // 5 A constant for 2 s → 10 C.
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src", "I_A", 5.0)),
            MakeSnapshot(1.0, ("src", "I_A", 5.0)),
            MakeSnapshot(2.0, ("src", "I_A", 5.0)),
        };
        var cum = TimeHistoryAnalytics.CumulativeCharge_C(hist);
        Assert.Equal(10.0, cum[2].CumulativeCharge_C, precision: 9);
    }

    [Fact]
    public void CumulativeCharge_KirchhoffConservation_StaysNearZero()
    {
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src", "Out_A", 5.0), ("load", "In_A", -5.0)),
            MakeSnapshot(1.0, ("src", "Out_A", 7.0), ("load", "In_A", -7.0)),
        };
        var cum = TimeHistoryAnalytics.CumulativeCharge_C(hist);
        foreach (var t in cum)
            Assert.Equal(0.0, t.CumulativeCharge_C, precision: 9);
    }

    // ── Edge cases ──────────────────────────────────────────────────────

    [Fact]
    public void Cumulative_EmptyHistory_ReturnsEmpty()
    {
        var empty = Array.Empty<TimeHistorySnapshot>();
        Assert.Empty(TimeHistoryAnalytics.CumulativeEnergy_J(empty));
        Assert.Empty(TimeHistoryAnalytics.CumulativeMass_kg(empty));
        Assert.Empty(TimeHistoryAnalytics.CumulativeCharge_C(empty));
    }

    [Fact]
    public void Cumulative_SingleTickHistory_ReturnsSingleZero()
    {
        var hist = new[] { MakeSnapshot(0.0, ("a", "P_W", 100.0)) };
        Assert.Equal(0.0, TimeHistoryAnalytics.CumulativeEnergy_J(hist)[0].CumulativeEnergy_J);
        Assert.Equal(0.0, TimeHistoryAnalytics.CumulativeMass_kg(hist)[0].CumulativeMass_kg);
        Assert.Equal(0.0, TimeHistoryAnalytics.CumulativeCharge_C(hist)[0].CumulativeCharge_C);
    }

    [Fact]
    public void Cumulative_NullHistory_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.CumulativeEnergy_J(null!));
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.CumulativeMass_kg(null!));
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.CumulativeCharge_C(null!));
    }
}
