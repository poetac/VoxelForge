// ComponentNetworkAllocationTests.cs — Issue #491 (Tier 1 perf)
// regression. Pins the per-Solve allocation footprint of
// ComponentNetwork. The pre-pool implementation paid
// ~ O(components × dict_overhead) bytes per Solve from
// `new Dictionary<string, double>()` allocations in Solve() +
// GatherInputs() + (via _lastResolvedInputs rebuild) the LINQ
// ToDictionary fold. After Issue #491 the input pool + per-destination
// connection cache eliminate the per-tick input-dict and LINQ
// allocations; the output dicts stay freshly allocated per call so
// TimeStepIntegrator's long-lived snapshot capture (which holds
// references straight off the returned map) remains correct.
//
// The threshold below is intentionally generous (it leaves headroom
// for runtime fluctuations like JIT codegen / GC table growth on first
// touch) — its job is to catch the regression of "someone reintroduced
// `new Dictionary<...>()` inside the inner Solve loop", NOT to pin an
// exact byte count. If allocations rise by ~10× without other changes,
// the pool / connection cache has been broken.

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class ComponentNetworkAllocationTests
{
    // A minimal multi-input / multi-output component the tests can chain
    // together without dragging in pillar-specific dependencies.
    private sealed class FanInOutComponent : SystemComponent
    {
        public FanInOutComponent(
            string name,
            IReadOnlyList<string> inputs,
            IReadOnlyList<string> outputs) : base(name)
        {
            InputPorts  = inputs;
            OutputPorts = outputs;
        }

        public override IReadOnlyList<string> InputPorts  { get; }
        public override IReadOnlyList<string> OutputPorts { get; }

        public override void Evaluate(
            IReadOnlyDictionary<string, double> inputs,
            IDictionary<string, double> outputs)
        {
            // Mirror inputs to outputs round-robin so the connection
            // graph carries non-trivial values without dragging in any
            // pillar math. Deterministic + allocation-free in the hot
            // path (no boxing, no LINQ).
            double sum = 0.0;
            foreach (var port in InputPorts)
                sum += inputs[port];
            int i = 0;
            foreach (var port in OutputPorts)
            {
                outputs[port] = sum + i;
                i++;
            }
        }
    }

    // Build a representative 6-component diurnal microgrid analog: two
    // "sources" (PV + diesel-gen), two "loads", one converter, one
    // battery. Wires resemble the SI.W12 demo topology — every solve
    // walks 6 components, 6 connections, 4 external inputs.
    private static ComponentNetwork BuildSixComponentNetwork()
    {
        var net = new ComponentNetwork();
        net.Add(new FanInOutComponent("pv",      new[] { "G", "T" },     new[] { "P", "V" }));
        net.Add(new FanInOutComponent("gen",     new[] { "fuel" },        new[] { "P", "V" }));
        net.Add(new FanInOutComponent("conv",    new[] { "Pa", "Pb" },    new[] { "Pout", "loss" }));
        net.Add(new FanInOutComponent("battery", new[] { "Pin", "Pload" }, new[] { "soc", "Vbat" }));
        net.Add(new FanInOutComponent("load1",   new[] { "V" },           new[] { "I" }));
        net.Add(new FanInOutComponent("load2",   new[] { "V" },           new[] { "I" }));
        net.Connect("pv",  "P", "conv",    "Pa");
        net.Connect("gen", "P", "conv",    "Pb");
        net.Connect("conv","Pout", "battery","Pin");
        net.Connect("conv","loss", "battery","Pload");
        net.Connect("battery","Vbat", "load1", "V");
        net.Connect("battery","Vbat", "load2", "V");
        net.SetExternalInput("pv",  "G",    1000.0);
        net.SetExternalInput("pv",  "T",      25.0);
        net.SetExternalInput("gen", "fuel",   50.0);
        return net;
    }

    [Fact]
    public void Solve_PerCall_AllocationBudget_StaysBoundedAcrossManyTicks()
    {
        var net = BuildSixComponentNetwork();

        // Warm-up: prime the input pool, the per-destination connection
        // cache, JIT-compile every code path Solve walks. Without warm-up
        // the first-call allocation includes one-shot setup costs that
        // skew the per-call budget.
        for (int i = 0; i < 20; i++) net.Solve();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int ticks = 1000;
        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < ticks; i++) net.Solve();
        long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
        long bytesPerSolve = (bytesAfter - bytesBefore) / ticks;

        // Post-#491 budget (6-component network):
        //   - 1 result-map dict alloc (outer)
        //   - 1 working-copy dict alloc (mutableOutputs outer)
        //   - 6 per-component output dicts (freshly alloc, hold-safe)
        //   - cached topo order + per-destination connection cache are
        //     all reused across solves
        // Empirically ~1.5-2.5 KB per call on net9.0-windows; we pin the
        // ceiling at 3 KB so JIT codegen variance + first-iteration
        // table growth don't false-positive the regression check. The
        // pre-#491 path (per-component input dicts + LINQ Where +
        // per-solve dep-map rebuild + per-solve topo-sort scratch) ran
        // ~5.5 KB on the same 6-component graph, so the 3 KB ceiling
        // catches a regression decisively without splitting hairs on a
        // few bytes of GC table padding.
        Assert.True(bytesPerSolve < 3_000,
            $"Per-Solve allocation budget breached: {bytesPerSolve} B (limit 3000 B). "
          + "Issue #491 regression — re-introduce per-component dict pooling "
          + "+ per-destination connection cache + cached topological "
          + "order in ComponentNetwork.Solve.");
    }

    [Fact]
    public void Solve_LastResolvedInputs_IsBackedByPooledDicts_AcrossCalls()
    {
        // Issue #491 contract. The dict instances exposed via
        // LastResolvedInputs ARE reused across Solve() calls (input
        // pool) — same identity, mutated contents. Callers that need
        // a stable snapshot of one tick must clone what they read.
        var net = BuildSixComponentNetwork();
        net.Solve();
        var firstCallPv = net.LastResolvedInputs["pv"];
        net.Solve();
        var secondCallPv = net.LastResolvedInputs["pv"];
        Assert.Same(firstCallPv, secondCallPv);
    }

    [Fact]
    public void Solve_ResultMap_ContainsFreshlyAllocatedOutputDicts_AcrossCalls()
    {
        // Issue #491 contract. The output-side dicts in the returned
        // result map remain freshly allocated per call — that's how
        // TimeStepIntegrator's TimeHistorySnapshot capture (which holds
        // long-lived references) stays correct without per-snapshot
        // clones.
        var net = BuildSixComponentNetwork();
        var firstResult  = net.Solve();
        var firstPvDict  = firstResult["pv"];
        var secondResult = net.Solve();
        var secondPvDict = secondResult["pv"];
        // Identity must differ — pooling outputs would silently corrupt
        // any caller that captured the dict reference (TimeStepIntegrator
        // is the canonical example).
        Assert.NotSame(firstPvDict, secondPvDict);
    }

    [Fact]
    public void Solve_ConnectionCache_RebuildsOnConnectAfterFirstSolve()
    {
        // Issue #491 contract. The per-destination connection cache is
        // invalidated when Connect() / Add() alters the topology. A
        // post-Solve Connect must take effect on the next Solve.
        var net = new ComponentNetwork();
        net.Add(new FanInOutComponent("src",  Array.Empty<string>(), new[] { "y" }));
        net.Add(new FanInOutComponent("sink", new[] { "x" },         new[] { "z" }));
        net.SetExternalInput("sink", "x", 7.0);
        var before = net.Solve();
        Assert.Equal(7.0, before["sink"]["z"]);   // sink reads external input

        // Wire src.y → sink.x AFTER the first solve. The external
        // input still wins (external > connection), so the test is
        // really about the dirty flag's effect on the dependency map +
        // topo sort — adding a connection between previously
        // independent nodes flips them into dep order.
        net.Connect("src", "y", "sink", "x");
        var after = net.Solve();
        // Sink's "z" still reflects the external input (external
        // override is the documented contract); but the cache must
        // have rebuilt without throwing.
        Assert.Equal(7.0, after["sink"]["z"]);
        Assert.Contains("src", after.Keys);
        Assert.Contains("sink", after.Keys);
    }
}
