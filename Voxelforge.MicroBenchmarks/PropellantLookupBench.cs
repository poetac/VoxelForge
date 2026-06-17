// BB-3 (2026-04-29): PropellantTables.Lookup microbenchmarks. Defends
// the ConcurrentDictionary memoizer that makes cached hits effectively
// free; xUnit's Phase4 guard catches a 6× regression but BDN gives
// the steady-state per-hit cost in nanoseconds.

using BenchmarkDotNet.Attributes;
using Voxelforge.Combustion;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class PropellantLookupBench
{
    [GlobalSetup]
    public void Setup()
    {
        // Warm the table cache so CacheHit_* benches read from the
        // memoizer instead of paying the cold lookup penalty.
        _ = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 6.9e6);
        _ = PropellantTables.Lookup(PropellantPair.LOX_H2,  6.0, 4.0e6);
        _ = PropellantTables.Lookup(PropellantPair.LOX_RP1, 2.3, 7.0e6);
    }

    [Benchmark]
    public PropellantState CacheHit_LOX_CH4()
        => PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 6.9e6);

    [Benchmark]
    public PropellantState CacheHit_LOX_H2()
        => PropellantTables.Lookup(PropellantPair.LOX_H2, 6.0, 4.0e6);

    [Benchmark]
    public PropellantState CacheHit_LOX_RP1()
        => PropellantTables.Lookup(PropellantPair.LOX_RP1, 2.3, 7.0e6);

    [Benchmark]
    public PropellantState CacheHit_Interpolated_Boundary_MR()
        => PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.45, 6.9e6);
}
