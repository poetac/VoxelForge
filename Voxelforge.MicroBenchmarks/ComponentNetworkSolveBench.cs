// ComponentNetworkSolveBench.cs — Issue #491 (Tier 1 perf) bench.
// Measures the per-Solve wall-clock + allocation footprint on a
// representative SI.W12-class diurnal-microgrid analog (PV + diesel-
// gen + DC-DC converter + battery + 2 loads = 6 components, 6 wires,
// 4 external inputs). Tracks the regression of "someone re-introduced
// `new Dictionary<...>()` inside the Solve hot loop".
//
// The components are intentionally pillar-free (no PV / battery math
// dragged in) so the bench measures the ComponentNetwork orchestration
// cost — gather inputs, evaluate, propagate outputs, return — without
// being swamped by domain-physics solve time. The audit's per-tick
// allocation hot path is what we're shrinking; the actual Evaluate
// calls already pay no allocation in the pillar math itself.
//
// Read alongside `ComponentNetworkAllocationTests` which pins a
// regression threshold (3 KB per Solve on a 6-component network) via
// `GC.GetAllocatedBytesForCurrentThread()`. The bench surfaces the
// underlying numbers + the ns/op trend on each `dotnet run -c Release`.

using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Voxelforge.Integration;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class ComponentNetworkSolveBench
{
    private ComponentNetwork _network = null!;

    [GlobalSetup]
    public void Setup()
    {
        _network = BuildSixComponentNetwork();
        // Prime the input pool + per-destination connection cache so
        // the measured Solve loop reflects the steady-state hot path
        // (matches how the TimeStepIntegrator calls Solve from tick 2
        // onward).
        for (int i = 0; i < 10; i++) _network.Solve();
    }

    [Benchmark]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Solve()
        => _network.Solve();

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

    // Local minimal SystemComponent — keeps the bench self-contained
    // and free of any pillar-specific overhead. Reflects only the
    // network orchestration cost we're shrinking.
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
            double sum = 0.0;
            foreach (var port in InputPorts) sum += inputs[port];
            int i = 0;
            foreach (var port in OutputPorts) { outputs[port] = sum + i; i++; }
        }
    }
}
