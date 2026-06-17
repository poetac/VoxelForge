// BB-5 (2026-04-29): cycle-solver registry microbench. Exercises
// `CycleSolvers.Get(cycle)` per Sprint 21 ICycleSolver registry — one
// row per EngineCycle value so the dispatch + property-bag access cost
// is measured for every cycle the SA solver may select. The actual
// cycle-balance physics is integrated into
// `RegenChamberOptimization.GenerateWith` and benched at a higher level
// via the GenerateWith family of benches.

using BenchmarkDotNet.Attributes;
using Voxelforge.FeedSystem;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class EngineCyclesBench
{
    [Benchmark]
    public ICycleSolver Get_PressureFed()      => CycleSolvers.Get(EngineCycle.PressureFed);

    [Benchmark]
    public ICycleSolver Get_GasGenerator()     => CycleSolvers.Get(EngineCycle.GasGenerator);

    [Benchmark]
    public ICycleSolver Get_ElectricPump()     => CycleSolvers.Get(EngineCycle.ElectricPump);

    [Benchmark]
    public ICycleSolver Get_OpenExpander()     => CycleSolvers.Get(EngineCycle.OpenExpander);

    [Benchmark]
    public ICycleSolver Get_ClosedExpander()   => CycleSolvers.Get(EngineCycle.ClosedExpander);

    [Benchmark]
    public ICycleSolver Get_StagedCombustion() => CycleSolvers.Get(EngineCycle.StagedCombustion);

    [Benchmark]
    public ICycleSolver Get_FullFlow()         => CycleSolvers.Get(EngineCycle.FullFlow);

    [Benchmark]
    public ICycleSolver Get_ORSC()             => CycleSolvers.Get(EngineCycle.ORSC);

    [Benchmark]
    public ICycleSolver Get_TapOff()           => CycleSolvers.Get(EngineCycle.TapOff);
}
