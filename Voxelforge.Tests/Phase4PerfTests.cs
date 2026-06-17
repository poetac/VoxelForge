// Phase4PerfTests.cs — Contract tests for the perf optimisation sprint:
//   • Per-solve quantised cache on fluid.GetState
//   • ConcurrentDictionary cache on PropellantTables.Lookup
//   • Parallel.For + per-iter deterministic RNG on tolerance MC
//   • Pre-allocated axial-conduction scratch buffers
//   • skipMfgAnalysis on the parallel-SA fast path (suppresses
//     ResidualStressAnalysis + gimbal-confidence demotion)
//   • Batched bolt-pattern voxel subtractions (single BoolSubtract
//     per flange — no observable contract change; covered by the
//     existing geometry / mass tests rather than a dedicated guard)
//
// All perf changes are perf-only with bit-identical numerical results
// at the SA-decision level. These tests defend the two contract
// guarantees that matter:
//   1. Determinism: same inputs ⇒ same outputs across runs and across
//      cache states.
//   2. skipMfgAnalysis: when set, drops Residual but preserves every
//      gate-driving analysis so SA selection stays correct.

using Voxelforge.Analysis;
using Voxelforge.Combustion;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

// Issue #311: PropellantLookup_RepeatedCalls_AreBitIdentical observes
// the global PropellantTables.UseEquilibrium flag (cache key includes
// it). Other test classes that mutate UseEquilibrium can flip the
// observed state under us mid-fact when they run in parallel, causing
// the bit-identical assertion to fail with a frozen-vs-equilibrium
// state mismatch. Joining PropellantTablesGlobalStateCollection
// serialises us against the mutators.
[Collection(PropellantTablesGlobalStateCollection.Name)]
public class Phase4PerfTests
{
    // ─────────────────────────────────────────────────────────────────
    //  PropellantTables.Lookup cache
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void PropellantLookup_RepeatedCalls_AreBitIdentical()
    {
        // Cache must return the SAME PropellantState instance (or a
        // value-equal one) for identical inputs. Anything else means
        // we're either bypassing the cache or mutating the entry.
        // Pin UseEquilibrium for the duration so the cache key is
        // stable even if a future #311-style oversight in another
        // class leaves the global flag flipped on entry.
        bool prior = PropellantTables.UseEquilibrium;
        try
        {
            PropellantTables.UseEquilibrium = false;
            var a = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 6.9e6);
            var b = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 6.9e6);
            Assert.Equal(a, b);
        }
        finally
        {
            PropellantTables.UseEquilibrium = prior;
        }
    }

    [Fact]
    public void PropellantLookup_SurvivesMidTestUseEquilibriumFlip()
    {
        // Issue #311 regression test. Pre-fix: a sibling test class
        // mutating PropellantTables.UseEquilibrium during parallel
        // execution could flip the cache key between two Lookup calls
        // in this fact, producing a frozen-vs-equilibrium IsFrozen
        // mismatch. Post-fix: the
        // PropellantTablesGlobalStateCollection xUnit collection
        // serialises every state-mutating class with this one, so the
        // mid-fact flip cannot happen. This test simulates the worst
        // case explicitly to lock the contract.
        bool prior = PropellantTables.UseEquilibrium;
        try
        {
            // Step 1: lookup with frozen tables.
            PropellantTables.UseEquilibrium = false;
            var frozenA = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 6.9e6);

            // Step 2: simulate a sibling class flipping the flag mid-fact.
            PropellantTables.UseEquilibrium = true;
            var equilibrium = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 6.9e6);

            // Step 3: flip back and re-lookup. Must equal step-1 value
            // because the cache key includes the flag.
            PropellantTables.UseEquilibrium = false;
            var frozenB = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 6.9e6);

            Assert.Equal(frozenA, frozenB);
            Assert.True(frozenA.IsFrozen, "frozen-side state must report IsFrozen = true");
            Assert.False(equilibrium.IsFrozen, "equilibrium-corrected state must report IsFrozen = false");
            Assert.NotEqual(frozenA.CStar_ms, equilibrium.CStar_ms);
        }
        finally
        {
            PropellantTables.UseEquilibrium = prior;
            PropellantTables.ClearLookupCacheForTests();
        }
    }

    [Theory]
    [InlineData(PropellantPair.LOX_CH4, 3.3, 6.9e6)]
    [InlineData(PropellantPair.LOX_H2,  4.0, 1.0e7)]
    [InlineData(PropellantPair.LOX_RP1, 2.5, 1.0e7)]
    public void PropellantLookup_AllImplementedPairs_ProduceFiniteState(
        PropellantPair pair, double mr, double pc)
    {
        var s = PropellantTables.Lookup(pair, mr, pc);
        Assert.True(double.IsFinite(s.ChamberTemp_K) && s.ChamberTemp_K > 0);
        Assert.True(double.IsFinite(s.CStar_ms)      && s.CStar_ms      > 0);
        Assert.True(double.IsFinite(s.Gamma)         && s.Gamma         > 1.0);
    }

    // ─────────────────────────────────────────────────────────────────
    //  RegenCoolingSolver determinism with cache + buffers
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RegenSolve_RepeatedRuns_AreBitIdentical()
    {
        // The coolant cache lives only for the duration of one Solve
        // call, so two back-to-back solves with the same inputs must
        // produce identical scalars. This also guards the
        // axial-conduction buffer swap (any aliasing bug would diverge
        // the two runs).
        var (cond, design) = Baseline();
        var g1 = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var g2 = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.Equal(g1.Thermal.PeakGasSideWallT_K,    g2.Thermal.PeakGasSideWallT_K,    precision: 9);
        Assert.Equal(g1.Thermal.CoolantOutletT_K,      g2.Thermal.CoolantOutletT_K,      precision: 9);
        Assert.Equal(g1.Thermal.CoolantPressureDrop_Pa, g2.Thermal.CoolantPressureDrop_Pa, precision: 6);
        Assert.Equal(g1.Thermal.TotalHeatLoad_W,        g2.Thermal.TotalHeatLoad_W,        precision: 6);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Parallel.For tolerance sweep determinism
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ToleranceSweep_PerIterRng_DeterministicAcrossRuns()
    {
        // Per-iteration RNG (seed = RandomSeed + i) must produce
        // identical aggregate quantiles between two runs with the same
        // top-level seed, regardless of Parallel.For ordering.
        var (cond, design) = Baseline();
        var contour = Chamber.ChamberContourGenerator.Generate(
            throatRadius_mm:        Derived(cond, design).ThroatRadius_mm,
            contractionRatio:       design.ContractionRatio,
            expansionRatio:         design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            stationCount:           60);
        var inp = new ToleranceInputs(SampleCount: 80, RandomSeed: 7);

        var r1 = ToleranceAnalysis.Run(contour, cond, design, inp);
        var r2 = ToleranceAnalysis.Run(contour, cond, design, inp);
        Assert.Equal(r1.PeakWallT_K.P50,            r2.PeakWallT_K.P50,            precision: 9);
        Assert.Equal(r1.MinSafetyFactor.P10,        r2.MinSafetyFactor.P10,        precision: 9);
        Assert.Equal(r1.CoolantPressureDrop_Pa.P90, r2.CoolantPressureDrop_Pa.P90, precision: 4);
    }

    [Fact]
    public void ToleranceSweep_DifferentSeeds_ProduceDifferentDraws()
    {
        // Seeding control: different top-level seeds must perturb
        // the per-iter RNG instances differently.
        var (cond, design) = Baseline();
        var contour = Chamber.ChamberContourGenerator.Generate(
            throatRadius_mm:        Derived(cond, design).ThroatRadius_mm,
            contractionRatio:       design.ContractionRatio,
            expansionRatio:         design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            stationCount:           60);
        var rA = ToleranceAnalysis.Run(contour, cond, design,
            new ToleranceInputs(SampleCount: 80, RandomSeed: 1));
        var rB = ToleranceAnalysis.Run(contour, cond, design,
            new ToleranceInputs(SampleCount: 80, RandomSeed: 999));
        // Equal P50s would mean the seed didn't take effect anywhere.
        Assert.NotEqual(rA.PeakWallT_K.P50, rB.PeakWallT_K.P50);
    }

    // ─────────────────────────────────────────────────────────────────
    //  skipMfgAnalysis behaviour
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SkipMfgAnalysis_NullsResidualButKeepsThermalAndStability()
    {
        var (cond, design) = Baseline();
        var full = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true, skipMfgAnalysis: false);
        var fast = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true, skipMfgAnalysis: true);

        // The skipped analyses are null on the fast path.
        Assert.NotNull(full.Residual);
        Assert.Null(fast.Residual);

        // Gate-driving analyses MUST stay populated so SA selection
        // is unchanged.
        Assert.NotNull(fast.Manufacturing);
        Assert.NotNull(fast.Stability);
        Assert.NotNull(fast.Thermal);
        Assert.NotNull(fast.Stress);

        // Thermal numerics must be identical — the skip flag affects
        // ONLY the post-processing analyses, never the physics solver.
        Assert.Equal(full.Thermal.PeakGasSideWallT_K,
                     fast.Thermal.PeakGasSideWallT_K, precision: 9);
        Assert.Equal(full.Stress.MinSafetyFactor,
                     fast.Stress.MinSafetyFactor,    precision: 9);
    }

    [Fact]
    public void SkipMfgAnalysis_SAEvaluate_PicksSameScore()
    {
        // The SA objective reads thermal + structural + manufacturing
        // (for FEATURE_TOO_SMALL gate) + stability (for STABILITY_FAIL).
        // Skipping only the post-processing analyses must NOT change
        // the score that SA sees for any candidate.
        var (cond, design) = Baseline();
        var full = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true, skipMfgAnalysis: false);
        var fast = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true, skipMfgAnalysis: true);
        // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var sFull = RegenChamberOptimization.Evaluate(full, RegenChamberOptimization.Profiles[0]);
        var sFast = RegenChamberOptimization.Evaluate(fast, RegenChamberOptimization.Profiles[0]);
        Assert.Equal(sFull.TotalScore,    sFast.TotalScore,    precision: 6);
        Assert.Equal(sFull.PeakWallT_K,   sFast.PeakWallT_K,   precision: 9);
        Assert.Equal(sFull.MinSafetyFactor, sFast.MinSafetyFactor, precision: 9);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private static (OperatingConditions cond, RegenChamberDesign design) Baseline()
    {
        var cond = new OperatingConditions
        {
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 40,
        };
        return (cond, design);
    }

    private static DerivedValues Derived(OperatingConditions cond, RegenChamberDesign design)
    {
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        return RegenChamberOptimization.ComputeDerived(cond, gas, design);
    }
}
