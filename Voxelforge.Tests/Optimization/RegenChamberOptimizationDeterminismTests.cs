// RegenChamberOptimizationDeterminismTests.cs — PR 4 / issue #551.
//
// Pins the static-state-freedom of RegenChamberOptimization.Evaluate(gen,
// profile) after the refactor that removed the global _profileIndex
// surface. The contract: Evaluate is a pure function of (gen, profile);
// the same arguments must always return the same RegenScoreResult,
// regardless of what other state exists in the process — including
// interleaved calls with different profiles, parallel calls from many
// threads, and interleaved calls with different RegenGenerationResult
// fixtures. If any of these tests ever fails, a static surface has
// crept back in.
//
// Fixture conventions match Voxelforge.Tests/RegenObjectiveTests.cs:
//   • Small LOX/CH4 baseline (2.224 kN, 6.9 MPa, MR 3.3).
//   • GenerateWith(... skipVoxelGeometry: true, skipMfgAnalysis: true)
//     so the test never needs PicoGK and stays under 100 ms per Evaluate.

using System.Threading.Tasks;
using Voxelforge.Combustion;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Optimization;

public sealed class RegenChamberOptimizationDeterminismTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Shared baseline (mirrors RegenObjectiveTests.cs conventions).
    // ─────────────────────────────────────────────────────────────────

    private static readonly OperatingConditions BaselineConditions = new()
    {
        Thrust_N                = 2224.0,
        ChamberPressure_Pa      = 6.9e6,
        MixtureRatio            = 3.3,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex       = 0,   // GRCop-42
        PropellantPair          = PropellantPair.LOX_CH4,
    };

    private static readonly RegenChamberDesign BaselineDesign = new()
    {
        IncludeManifolds      = false,
        IncludePorts          = false,
        IncludeInjectorFlange = false,
        ContourStationCount   = 60,
    };

    // A second condition set that produces a distinct generation result.
    // Different thrust + MR puts it on a different point in the search
    // space, so its Evaluate output is necessarily different from the
    // baseline's — that distinctness is what makes the cross-contamination
    // test meaningful.
    private static readonly OperatingConditions AltConditions = new()
    {
        Thrust_N                = 4448.0,   // 2× baseline thrust
        ChamberPressure_Pa      = 6.9e6,
        MixtureRatio            = 2.8,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex       = 0,
        PropellantPair          = PropellantPair.LOX_CH4,
    };

    private static RegenGenerationResult Generate(OperatingConditions cond) =>
        RegenChamberOptimization.GenerateWith(
            cond, BaselineDesign,
            voxelSize_mm: 0.0,
            skipVoxelGeometry: true,
            skipMfgAnalysis: true);

    /// <summary>
    /// Bit-equality comparison for two RegenScoreResults. We compare the
    /// numeric fields that scoring computes — TotalScore is the primary
    /// invariant, but if any leaked state shifted a sub-score the
    /// composite would also drift, so we pin the components too.
    /// </summary>
    private static void AssertBitEqual(RegenScoreResult expected, RegenScoreResult actual)
    {
        Assert.Equal(expected.TotalScore,         actual.TotalScore);
        Assert.Equal(expected.PeakWallT_K,        actual.PeakWallT_K);
        Assert.Equal(expected.WallTMargin_K,      actual.WallTMargin_K);
        Assert.Equal(expected.CoolantDP_Pa,       actual.CoolantDP_Pa);
        Assert.Equal(expected.CoolantDP_Fraction, actual.CoolantDP_Fraction);
        Assert.Equal(expected.CoolantTOut_K,      actual.CoolantTOut_K);
        Assert.Equal(expected.TotalHeatLoad_W,    actual.TotalHeatLoad_W);
        Assert.Equal(expected.ThroatHeatFlux_Wm2, actual.ThroatHeatFlux_Wm2);
        Assert.Equal(expected.Mass_g,             actual.Mass_g);
        Assert.Equal(expected.MinFeatureSize_mm,  actual.MinFeatureSize_mm);
        Assert.Equal(expected.MinSafetyFactor,    actual.MinSafetyFactor);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Tests
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_WithInterleavedProfiles_ProducesIsolatedScores()
    {
        // Pins: interleaving Evaluate(gen, Profiles[0]) and
        // Evaluate(gen, Profiles[1]) cannot leak between calls. If a
        // static field were still tracking the most-recently-used
        // profile, every other Profiles[0] result would be polluted by
        // the intervening Profiles[1] call.
        var gen = Generate(BaselineConditions);
        var p0 = RegenChamberOptimization.Profiles[0];
        var p1 = RegenChamberOptimization.Profiles[1];

        var p0Reference = RegenChamberOptimization.Evaluate(gen, p0);
        var p1Reference = RegenChamberOptimization.Evaluate(gen, p1);

        for (int i = 0; i < 10; i++)
        {
            var p0Result = RegenChamberOptimization.Evaluate(gen, p0);
            var p1Result = RegenChamberOptimization.Evaluate(gen, p1);
            AssertBitEqual(p0Reference, p0Result);
            AssertBitEqual(p1Reference, p1Result);
        }
    }

    [Fact]
    public void Evaluate_FromParallelThreads_ProducesIdenticalResults()
    {
        // Pins: Evaluate must be safe to call concurrently from N
        // threads with the same (gen, profile). Any residual thread-
        // local or shared-mutable state — even one cached intermediate
        // — would surface here as a torn result on at least one
        // iteration.
        var gen = Generate(BaselineConditions);
        var profile = RegenChamberOptimization.Profiles[0];

        var reference = RegenChamberOptimization.Evaluate(gen, profile);

        const int N = 100;
        var results = new RegenScoreResult[N];
        Parallel.For(0, N, i =>
        {
            results[i] = RegenChamberOptimization.Evaluate(gen, profile);
        });

        for (int i = 0; i < N; i++)
            AssertBitEqual(reference, results[i]);
    }

    [Fact]
    public void Evaluate_AcrossDifferentGens_NoCrossContamination()
    {
        // Pins: interleaving Evaluate(gen1, p) and Evaluate(gen2, p)
        // cannot cache any per-gen state across calls. If Evaluate
        // memoised on its `gen` argument (or its conditions, or its
        // contour) and forgot to key the cache properly, the second
        // call onward in each alternation would return the wrong gen's
        // score.
        var gen1 = Generate(BaselineConditions);
        var gen2 = Generate(AltConditions);
        var profile = RegenChamberOptimization.Profiles[0];

        var gen1Reference = RegenChamberOptimization.Evaluate(gen1, profile);
        var gen2Reference = RegenChamberOptimization.Evaluate(gen2, profile);

        // Sanity: the two fixtures must produce distinct results,
        // otherwise the cross-contamination check has no teeth. (At
        // least one score field must differ.)
        bool fixturesAreDistinct =
            gen1Reference.TotalScore        != gen2Reference.TotalScore     ||
            gen1Reference.PeakWallT_K       != gen2Reference.PeakWallT_K    ||
            gen1Reference.Mass_g            != gen2Reference.Mass_g         ||
            gen1Reference.TotalHeatLoad_W   != gen2Reference.TotalHeatLoad_W;
        Assert.True(fixturesAreDistinct,
            "Test fixtures must produce distinct scores or this test has no teeth.");

        for (int i = 0; i < 10; i++)
        {
            var gen1Result = RegenChamberOptimization.Evaluate(gen1, profile);
            var gen2Result = RegenChamberOptimization.Evaluate(gen2, profile);
            AssertBitEqual(gen1Reference, gen1Result);
            AssertBitEqual(gen2Reference, gen2Result);
        }
    }
}
