// SiDiagnosticsTests.cs — Sprint SI.W29 tests for PeakPowerImbalance,
// ConservationResidual* helpers, and EnergyDelivered_J window integral.

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class SiDiagnosticsTests
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

    // ── PeakPowerImbalance ─────────────────────────────────────────────

    [Fact]
    public void PeakPowerImbalance_FindsMaximumByMagnitude()
    {
        // Net imbalances: +100, -300, +50.  Peak by |val| is -300 at t=1.
        var hist = new[]
        {
            MakeSnapshot(0.0, ("a", "P_W",  100.0)),
            MakeSnapshot(1.0, ("a", "P_W", -300.0)),
            MakeSnapshot(2.0, ("a", "P_W",   50.0)),
        };
        var peak = TimeHistoryAnalytics.PeakPowerImbalance(hist);
        Assert.Equal(1.0,    peak.Time_s);
        Assert.Equal(-300.0, peak.PeakValue_W);
        Assert.False(peak.IsSurplus);   // negative ⇒ sink surplus
    }

    [Fact]
    public void PeakPowerImbalance_PositivePeak_IsSurplus()
    {
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src", "P_W",  500.0)),
            MakeSnapshot(1.0, ("src", "P_W",  100.0)),
        };
        var peak = TimeHistoryAnalytics.PeakPowerImbalance(hist);
        Assert.True(peak.IsSurplus);
        Assert.Equal(500.0, peak.PeakValue_W);
    }

    [Fact]
    public void PeakPowerImbalance_EmptyHistory_ReturnsZero()
    {
        var peak = TimeHistoryAnalytics.PeakPowerImbalance(Array.Empty<TimeHistorySnapshot>());
        Assert.Equal(0.0, peak.Time_s);
        Assert.Equal(0.0, peak.PeakValue_W);
        Assert.True(peak.IsSurplus);   // tie-break default = surplus
    }

    [Fact]
    public void PeakPowerImbalance_RejectsNullHistory()
    {
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.PeakPowerImbalance(null!));
    }

    // ── ConservationResidual* helpers ──────────────────────────────────

    [Fact]
    public void ConservationResidualEnergy_ConservativeNetwork_NearZero()
    {
        // Source +100 W cancelled by sink -100 W over 5 seconds.
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src", "P_W",  100.0), ("sink", "P_W", -100.0)),
            MakeSnapshot(1.0, ("src", "P_W",  100.0), ("sink", "P_W", -100.0)),
            MakeSnapshot(5.0, ("src", "P_W",  100.0), ("sink", "P_W", -100.0)),
        };
        double residual = TimeHistoryAnalytics.ConservationResidualEnergy_J(hist);
        Assert.Equal(0.0, residual, precision: 9);
    }

    [Fact]
    public void ConservationResidualEnergy_OpenNetwork_DriftsAccordingly()
    {
        // Unbalanced source: 100 W net over 5 seconds → +500 J residual.
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src", "P_W", 100.0)),
            MakeSnapshot(5.0, ("src", "P_W", 100.0)),
        };
        double residual = TimeHistoryAnalytics.ConservationResidualEnergy_J(hist);
        Assert.Equal(500.0, residual, precision: 9);
    }

    [Fact]
    public void ConservationResidualMass_TracksMassFlow()
    {
        // 1 kg/s for 3 s → 3 kg drift.
        var hist = new[]
        {
            MakeSnapshot(0.0, ("pump", "Flow_kgs", 1.0)),
            MakeSnapshot(3.0, ("pump", "Flow_kgs", 1.0)),
        };
        Assert.Equal(3.0,
            TimeHistoryAnalytics.ConservationResidualMass_kg(hist), precision: 9);
    }

    [Fact]
    public void ConservationResidualCharge_KirchhoffCheck()
    {
        // Source 5 A balanced by 5 A sink → cumulative = 0.
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src",  "Out_A",  5.0), ("load", "In_A", -5.0)),
            MakeSnapshot(2.0, ("src",  "Out_A",  5.0), ("load", "In_A", -5.0)),
        };
        Assert.Equal(0.0,
            TimeHistoryAnalytics.ConservationResidualCharge_C(hist), precision: 9);
    }

    [Fact]
    public void ConservationResidual_EmptyHistory_ReturnsZero()
    {
        var empty = Array.Empty<TimeHistorySnapshot>();
        Assert.Equal(0.0, TimeHistoryAnalytics.ConservationResidualEnergy_J(empty));
        Assert.Equal(0.0, TimeHistoryAnalytics.ConservationResidualMass_kg(empty));
        Assert.Equal(0.0, TimeHistoryAnalytics.ConservationResidualCharge_C(empty));
    }

    // ── EnergyDelivered_J window integral ──────────────────────────────

    [Fact]
    public void EnergyDelivered_FullWindow_MatchesCumulativeAtEnd()
    {
        // Constant 100 W over [0, 5] → 500 J full-window.
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src", "P_W", 100.0)),
            MakeSnapshot(5.0, ("src", "P_W", 100.0)),
        };
        double e = TimeHistoryAnalytics.EnergyDelivered_J(hist, 0.0, 5.0);
        Assert.Equal(500.0, e, precision: 9);
    }

    [Fact]
    public void EnergyDelivered_RejectsInvertedWindow()
    {
        var hist = new[]
        {
            MakeSnapshot(0.0, ("src", "P_W", 100.0)),
            MakeSnapshot(5.0, ("src", "P_W", 100.0)),
        };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TimeHistoryAnalytics.EnergyDelivered_J(hist, 5.0, 0.0));
    }

    [Fact]
    public void EnergyDelivered_RejectsNullHistory()
    {
        Assert.Throws<ArgumentNullException>(
            () => TimeHistoryAnalytics.EnergyDelivered_J(null!, 0.0, 1.0));
    }
}
