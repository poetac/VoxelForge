// StatefulComponentStateAccessBench.cs — Issue #611 bench (originally),
// repurposed under #738 Phase 3.
//
// Originally measured GetCurrentState/SetState allocation footprint
// across the 6 stateful component types. After #557 item 1 Phase 3
// (#738) the IStatefulComponent surface is span-based, so this bench
// is now an end-to-end zero-allocation verification: 10 000 ticks ×
// 10 components × round-trip should report 0 B allocated per op.

using System;
using BenchmarkDotNet.Attributes;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class StatefulComponentStateAccessBench
{
    private const int TicksPerOp = 10_000;
    private IStatefulComponent[] _components = null!;

    [GlobalSetup]
    public void Setup()
    {
        _components = new IStatefulComponent[]
        {
            new AccumulatorComponent("acc0"),
            new AccumulatorComponent("acc1"),
            new AccumulatorComponent("acc2"),
            new AccumulatorComponent("acc3"),
            new AccumulatorComponent("acc4"),
            new AccumulatorComponent("acc5"),
            new AccumulatorComponent("acc6"),
            new AccumulatorComponent("acc7"),
            new PidControllerComponent("pid0", proportionalGain: 1.0, integralGain: 0.1),
            new PidControllerComponent("pid1", proportionalGain: 1.0, integralGain: 0.1),
        };
        Span<double> tmp = stackalloc double[1];
        for (int i = 0; i < _components.Length; i++)
        {
            _components[i].GetInitialState(tmp);
            _components[i].SetState(tmp);
        }
    }

    /// <summary>
    /// Per op: 10 000 ticks × 10 components × (GetCurrentState + indexed
    /// read + SetState round-trip). Phase 3 (#738) surface is span-based
    /// so allocations per op should be 0 B.
    /// </summary>
    [Benchmark]
    public double TickRoundTrip()
    {
        double sink = 0.0;
        Span<double> buf = stackalloc double[1];
        for (int tick = 0; tick < TicksPerOp; tick++)
        {
            for (int i = 0; i < _components.Length; i++)
            {
                _components[i].GetCurrentState(buf);
                sink += buf[0];
                _components[i].SetState(buf);
            }
        }
        return sink;
    }
}
