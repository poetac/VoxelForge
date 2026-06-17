// BB-5 (2026-04-29): ToleranceAnalysis.Run microbench. Ports the
// xUnit `Phase4PerfBenchmarks.Bench_ToleranceSweep_100Samples`
// soft-ceiling test to BDN with parameterised sample counts. The
// xUnit version stays as the fast CI smoke guard; this is the high-
// fidelity measurement.
//
// Sample counts trade off bench time vs noise: 100 samples runs in
// ~500-1000 ms, 1000 samples in ~5-10 s. BDN auto-sizes iteration
// count so the suite total stays manageable.

using System.Threading;
using BenchmarkDotNet.Attributes;
using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.MicroBenchmarks.Helpers;
using Voxelforge.Optimization;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class ToleranceSweepBench
{
    private ChamberContour _contour = null!;
    private OperatingConditions _cond = null!;
    private RegenChamberDesign _design = null!;

    [GlobalSetup]
    public void Setup()
    {
        var (cond, design, contour, _) = SolverInputsFactory.MakeSolverInputs(stationCount: 60);
        _contour = contour;
        _cond = cond;
        _design = design;

        // Warm JIT + threadpool — first Parallel.For pays a one-time
        // worker spinup that can dominate small-N timings.
        _ = ToleranceAnalysis.Run(_contour, _cond, _design,
            new ToleranceInputs(SampleCount: 50, RandomSeed: 1));
    }

    [Benchmark]
    public ToleranceResult Sweep_100Samples()
        => ToleranceAnalysis.Run(_contour, _cond, _design,
            new ToleranceInputs(SampleCount: 100, RandomSeed: 1));

    [Benchmark]
    public ToleranceResult Sweep_500Samples()
        => ToleranceAnalysis.Run(_contour, _cond, _design,
            new ToleranceInputs(SampleCount: 500, RandomSeed: 1));

    [Benchmark]
    public ToleranceResult Sweep_1000Samples()
        => ToleranceAnalysis.Run(_contour, _cond, _design,
            new ToleranceInputs(SampleCount: 1000, RandomSeed: 1));
}
