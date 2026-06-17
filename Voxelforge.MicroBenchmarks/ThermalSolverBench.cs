// BB-3 (2026-04-29): RegenCoolingSolver microbenchmarks.
//
// Sized to span the SA hot-path budget — the Phase4 xUnit guard has
// `WarmSolve_80stations` running 30-80 ms; BDN re-measures with full
// statistical rigor (warmup-discarded, multi-iteration, allocator
// tracked). The 160-station bench detects O(N²) regressions on the
// gauntlet; aerospike + preburner cover the two non-bell solver
// paths.

using BenchmarkDotNet.Attributes;
using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;
using Voxelforge.MicroBenchmarks.Helpers;
using Voxelforge.Optimization;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class ThermalSolverBench
{
    private RegenSolverInputs _inputs80 = null!;
    private RegenSolverInputs _inputs160 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _inputs80  = SolverInputsFactory.MakeSolverInputs(stationCount: 80).inputs;
        _inputs160 = SolverInputsFactory.MakeSolverInputs(stationCount: 160).inputs;

        // Warm property tables + station-march caches once. ColdSolve_*
        // is intentionally NOT pre-warmed — it captures the first-call
        // miss penalty under JIT + cache-cold conditions, which is what
        // SA pays per fresh chain start.
        _ = RegenCoolingSolver.Solve(_inputs80);
        _ = RegenCoolingSolver.Solve(_inputs160);
    }

    [Benchmark]
    public RegenSolverOutputs ColdSolve_80stations()
    {
        // Mutate the inputs slightly so caching can't fold the call.
        // Each iteration sees a unique (rib_thickness, channel_count)
        // combination triggering a fresh march.
        var jittered = _inputs80 with
        {
            Channels = _inputs80.Channels with
            {
                RibThickness_mm = _inputs80.Channels.RibThickness_mm + 1e-9,
            },
        };
        return RegenCoolingSolver.Solve(jittered);
    }

    [Benchmark]
    public RegenSolverOutputs WarmSolve_80stations()
        => RegenCoolingSolver.Solve(_inputs80);

    [Benchmark]
    public RegenSolverOutputs WarmSolve_160stations()
        => RegenCoolingSolver.Solve(_inputs160);
}
