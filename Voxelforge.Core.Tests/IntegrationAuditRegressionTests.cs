// IntegrationAuditRegressionTests.cs — regression guards for red-team round-2
// findings in the System-Integration + Economics layers. PicoGK-free → runs on
// the Linux CI 'core' leg (uses InternalsVisibleTo for the internal types).

using System.Collections.Generic;
using System.Globalization;
using Voxelforge.Economics;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class IntegrationAuditRegressionTests
{
    // ── Finding 1: EnergyDelivered_J partial-window mis-integration ──────────
    //
    // The exporter clipped the interval width but applied the FULL-interval
    // endpoint power average over that clipped width, double-counting an
    // edge-aligned partial window. Now it interpolates power to the clip points.

    private static TimeHistorySnapshot PowerSnap(double t, double netPower_W)
        => new(t,
            new Dictionary<string, IReadOnlyDictionary<string, double>>
            {
                ["src"] = new Dictionary<string, double> { ["P_W"] = netPower_W },
            },
            new Dictionary<string, IReadOnlyDictionary<string, double>>());

    [Fact]
    public void EnergyDelivered_EdgeAlignedPartialWindow_IntegratesExactly()
    {
        // Power ramps 0 W → 100 W linearly over [0, 10] s. Energy over the
        // leading half [0, 5] is ∫₀⁵ 10t dt = 125 J. The old code returned
        // 0.5·(0+100)·5 = 250 J (2× high) using the interval-end power 100 W.
        var hist = new[] { PowerSnap(0.0, 0.0), PowerSnap(10.0, 100.0) };
        double e = TimeHistoryAnalytics.EnergyDelivered_J(hist, 0.0, 5.0);
        Assert.Equal(125.0, e, 6);
    }

    [Fact]
    public void EnergyDelivered_FullWindow_Unchanged()
    {
        // Full window must be the exact trapezoid 0.5·(0+100)·10 = 500 J — the
        // fix leaves the (only previously-pinned) full-window case identical.
        var hist = new[] { PowerSnap(0.0, 0.0), PowerSnap(10.0, 100.0) };
        Assert.Equal(500.0, TimeHistoryAnalytics.EnergyDelivered_J(hist, 0.0, 10.0), 6);
    }

    // ── Finding 2: adaptive integrators' final snapshot used stale inputs ────
    //
    // The in-loop ticks refresh time-varying external inputs before solving,
    // but the post-loop final snapshot at t = tEnd solved without refreshing,
    // so its port values echoed the previous tick's inputs while Time_s = tEnd.

    private sealed class PassthroughComponent : SystemComponent
    {
        public PassthroughComponent(string name) : base(name) { }
        public override IReadOnlyList<string> InputPorts { get; } = new[] { "In_W" };
        public override IReadOnlyList<string> OutputPorts { get; } = new[] { "Out_W" };
        public override void Evaluate(
            IReadOnlyDictionary<string, double> inputs, IDictionary<string, double> outputs)
            => outputs["Out_W"] = inputs.TryGetValue("In_W", out var v) ? v : 0.0;
    }

    private static double FinalOutW(IReadOnlyList<TimeHistorySnapshot> hist)
        => hist[hist.Count - 1].PortValues["p"]["Out_W"];

    [Fact]
    public void AdaptiveCrankNicolson_FinalSnapshot_ReflectsInputAtEndTime()
    {
        var net = new ComponentNetwork();
        net.Add(new PassthroughComponent("p"));
        net.SetTimeVaryingExternalInput("p", "In_W", t => t);   // input(t) = t
        var hist = new TimeStepIntegrator(net)
            .RunAdaptiveCrankNicolson(0.0, 1.0, 0.25, 0.01, 0.25);

        // Final snapshot is stamped Time_s = 1.0; its port value must be the
        // input at t = 1.0, not the last in-loop tick (0.75 on the old code).
        Assert.Equal(1.0, hist[hist.Count - 1].Time_s, 9);
        Assert.Equal(1.0, FinalOutW(hist), 9);
    }

    [Fact]
    public void AdaptiveCashKarp45_FinalSnapshot_ReflectsInputAtEndTime()
    {
        var net = new ComponentNetwork();
        net.Add(new PassthroughComponent("p"));
        net.SetTimeVaryingExternalInput("p", "In_W", t => t);
        var hist = new TimeStepIntegrator(net)
            .RunAdaptiveCashKarp45(0.0, 1.0, 0.25, 0.01, 0.25);

        Assert.Equal(1.0, hist[hist.Count - 1].Time_s, 9);
        Assert.Equal(1.0, FinalOutW(hist), 9);
    }

    // ── Finding 3: SystemCostBreakdown.ToTable culture leak ──────────────────

    [Fact]
    public void SystemCostBreakdown_ToTable_UsesInvariantCulture()
    {
        var bd = new SystemCostBreakdown(
            new[] { new CostEstimate("pack", 1234.5, 6789.0, 42.0) },
            TotalMass_kg: 1234.5, TotalCapitalCost_USD: 6789.0, TotalEmbodiedCO2_kgCO2eq: 42.0);

        var prev = CultureInfo.CurrentCulture;
        try
        {
            // de-DE renders the decimal separator as ',' — the old code would
            // emit "1234,5"; the invariant form must emit "1234.5".
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string table = bd.ToTable();
            Assert.Contains("1234.5", table);
            Assert.DoesNotContain("1234,5", table);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }
}
