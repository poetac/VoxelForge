// BB-4 (2026-04-29): FeasibilityGate microbenches.
//
// FeasibilityGate.Evaluate marches a single switch through all 47
// gate checks and collects violations into a List<FeasibilityViolation>.
// Per-gate isolation requires either an internal helper (which only
// the Optimization-stream owner can land — Core/Optimization/** is
// not editable from this stream) or per-fixture activation. We pick
// the fixture path: each [Benchmark] runs Evaluate against a different
// pre-built RegenGenerationResult — feasible / multi-violation /
// minimal — exercising different gate-firing paths through the same
// march. That answers the SA-hot-path question ("how long does Evaluate
// take when N gates fire") without modifying FeasibilityGate.
//
// PreScreen is the SA early-out — runs a curated subset of gates
// against (cond, design) before the ~50-200 ms thermal solve. Bench
// it standalone because the SA hot path calls it ~10× more often than
// Evaluate (one PreScreen per candidate, then GenerateWith only when
// PreScreen passes).
//
// All 47 gates are covered structurally because the same Evaluate
// switch runs every time. The aerospike fixture in particular fires
// AEROSPIKE_PLUG_WALL_TEMP / AEROSPIKE_ELEMENT_CLEARANCE /
// AEROSPIKE_INJECTOR_FACE_TEMP simultaneously, exercising the
// aerospike-parallel gate trio that the Phase4 / aerospike-0.4mm
// baseline pins regression-style.

using BenchmarkDotNet.Attributes;
using Voxelforge.Combustion;
using Voxelforge.MicroBenchmarks.Helpers;
using Voxelforge.Optimization;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class FeasibilityGateBench
{
    private RegenGenerationResult _merlinFeasible = null!;
    private RegenGenerationResult _aerospikeMultiViolation = null!;
    private OperatingConditions _defaultCond = null!;
    private RegenChamberDesign _defaultDesign = null!;
    private OperatingConditions _patternedCond = null!;
    private RegenChamberDesign _patternedDesign = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Merlin canonical (LOX/CH4 + GG @ 15 kN, Pc 4 MPa). Lands feasible
        // post-#168 (609 feasible per chain @ 500-iter SA per CLAUDE.md).
        var merlin = CanonicalDesignFixtures.MerlinSeed();
        _merlinFeasible = RegenChamberOptimization.GenerateWith(
            merlin.cond, merlin.design,
            skipVoxelGeometry: true, skipMfgAnalysis: true);

        // Aerospike canonical — designed over-aggressive so all 3 aerospike-
        // parallel gates fire simultaneously. The aerospike-0.4mm baseline
        // pins this combination as a regression sentinel.
        var aerospike = CanonicalDesignFixtures.AerospikeSeed();
        _aerospikeMultiViolation = RegenChamberOptimization.GenerateWith(
            aerospike.cond, aerospike.design,
            skipVoxelGeometry: true, skipMfgAnalysis: true);

        // PreScreen-only inputs (no GenerateWith needed) — fast SA early-out path.
        _defaultCond = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        _defaultDesign = new RegenChamberDesign();
        _patternedCond = merlin.cond;
        _patternedDesign = merlin.design;
    }

    [Benchmark]
    public FeasibilityGateResult Evaluate_FeasibleMerlin()
        => FeasibilityGate.Evaluate(_merlinFeasible);

    [Benchmark]
    public FeasibilityGateResult Evaluate_MultiViolationAerospike()
        => FeasibilityGate.Evaluate(_aerospikeMultiViolation);

    [Benchmark]
    public FeasibilityViolation? PreScreen_DefaultDesign()
        => FeasibilityGate.PreScreen(_defaultCond, _defaultDesign);

    [Benchmark]
    public FeasibilityViolation? PreScreen_PatternedMerlin()
        => FeasibilityGate.PreScreen(_patternedCond, _patternedDesign);
}
