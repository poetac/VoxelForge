// BB-3 (2026-04-29): SA-vector Pack/Unpack microbenchmarks. Defends
// the source-generator binder shipped via T1.4 / PR #172 — emits a
// compile-time accessor table that should keep Pack hot-path under
// 200 B allocation per call.
//
// Note: the SA vector grew 24 -> 31 dims via PR #81 / #88 / #114
// (film-cooling slot + pintle overrides + per-station wall thickness).
// Bench names use `31dim` to match current truth; the BB-3 acceptance
// criterion ("Pack_24dim_Default <= 200 B") was written when SA was
// 24-dim and applies equivalently to the 31-dim path because the
// generated accessors keep the same per-dim cost shape.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 2)]
public class PackUnpackBench
{
    private RegenChamberDesign _defaultDesign = null!;
    private RegenChamberDesign _patternedDesign = null!;
    private double[] _packedDefault = null!;
    private double[] _packedPatterned = null!;

    [GlobalSetup]
    public void Setup()
    {
        _defaultDesign = new RegenChamberDesign();
        _packedDefault = RegenChamberOptimization.Pack(_defaultDesign);

        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_CH4,
            Thrust_N: 15_000.0,
            ChamberPressure_Pa: 4e6,
            ExpansionRatio: 16.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        _patternedDesign = AutoSeeder.Seed(spec).Design;
        _packedPatterned = RegenChamberOptimization.Pack(_patternedDesign);
    }

    [Benchmark]
    public double[] Pack_31dim_Default()
        => RegenChamberOptimization.Pack(_defaultDesign);

    [Benchmark]
    public double[] Pack_31dim_WithPattern()
        => RegenChamberOptimization.Pack(_patternedDesign);

    [Benchmark]
    public RegenChamberDesign Unpack_31dim_Default()
        => RegenChamberOptimization.Unpack(_packedDefault, _defaultDesign);

    [Benchmark]
    public RegenChamberDesign Unpack_31dim_WithPattern()
        => RegenChamberOptimization.Unpack(_packedPatterned, _patternedDesign);
}
