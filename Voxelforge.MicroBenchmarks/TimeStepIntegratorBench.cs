// TimeStepIntegratorBench.cs — Issue #610 bench.
// Measures CN / RK4 / Cash-Karp per-step allocation footprint on a
// representative 10-stateful-component network. Before #610: each
// stepper allocated fresh `Dictionary<string, Dictionary<string,
// double>>` outer + per-component inner dicts on every k-stage /
// every fixed-point iteration. After #610: hoisted to integrator-
// owned buffer fields, reused across ticks.
//
// Acceptance scenario (per #610): stiff 10-component network for
// 100 CN ticks → ≥70% Gen0 reduction vs baseline. The Cash-Karp +
// RK4 paths share the same allocation surface and benefit identically.

using BenchmarkDotNet.Attributes;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class TimeStepIntegratorBench
{
    private ComponentNetwork _network = null!;
    private TimeStepIntegrator _integrator = null!;

    [GlobalSetup]
    public void Setup()
    {
        _network = new ComponentNetwork();
        var accs = new AccumulatorComponent[10];
        for (int i = 0; i < 10; i++)
        {
            accs[i] = new AccumulatorComponent($"acc{i}", initial: 0.0);
            _network.Add(accs[i]);
            _network.SetExternalInput($"acc{i}", "Input_rate", 1.0);
        }
        _integrator = new TimeStepIntegrator(_network);
        for (int i = 0; i < 10; i++)
            _integrator.RegisterStateful($"acc{i}", accs[i]);
        // Prime by running one Crank-Nicolson tick so the buffer pool +
        // ComponentNetwork caches are warm; the measured loop reflects
        // steady-state behaviour.
        _integrator.Run(0.0, 0.01, 0.01,
            method: IntegrationMethod.CrankNicolson);
    }

    /// <summary>
    /// 100 Crank-Nicolson ticks. The CN inner fixed-point loop is the
    /// largest per-tick allocation source; this bench is the primary
    /// acceptance number for #610.
    /// </summary>
    [Benchmark]
    public int CrankNicolson_100Ticks()
    {
        var hist = _integrator.Run(0.0, 1.0, 0.01,
            method: IntegrationMethod.CrankNicolson);
        return hist.Count;
    }

    [Benchmark]
    public int Rk4_100Ticks()
    {
        var hist = _integrator.Run(0.0, 1.0, 0.01,
            method: IntegrationMethod.Rk4);
        return hist.Count;
    }

    [Benchmark]
    public int CashKarp_100Ticks()
    {
        var hist = _integrator.RunAdaptiveCashKarp45(
            t0_s: 0.0, tEnd_s: 1.0,
            dtInitial_s: 0.01, dtMin_s: 1e-6, dtMax_s: 0.05);
        return hist.Count;
    }
}
