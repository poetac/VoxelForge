// Sprint T1.1 (2026-04-25): MultiChainOptimizer tests.
//
// Pins the strict-determinism contract (same baseSeed + chainCount +
// maxIter → identical BestParams) plus migration semantics, Sobol
// warmup, default chain auto-scaling, and barrier-doesn't-deadlock-on-
// early-infeasible-exit.

using System;
using System.Linq;
using System.Threading;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class MultiChainOptimizerTests
{
    // Convex synthetic objective: sum of squares around a known minimum.
    // Minimum at x = 0.5 in each dim, score 0. Used as a deterministic,
    // cheap evaluator for these tests.
    private static (double, object?) ConvexEvaluator(double[] x)
    {
        double sum = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double d = x[i] - 0.5;
            sum += d * d;
        }
        return (sum, null);
    }

    private static (double Min, double Max)[] StandardBounds(int dim)
    {
        var b = new (double, double)[dim];
        for (int i = 0; i < dim; i++) b[i] = (0.0, 1.0);
        return b;
    }

    [Fact]
    public void DefaultChainCount_ScalesWithProcessorCount()
    {
        int n = MultiChainOptimizer.DefaultChainCount();
        Assert.InRange(n, 1, 16);
        // On any machine with ≥ 4 cores, default should be ≥ 2.
        if (Environment.ProcessorCount >= 4)
            Assert.True(n >= 2, $"with {Environment.ProcessorCount} cores expected ≥ 2 chains, got {n}");
    }

    [Fact]
    public void StrictDeterminism_SameSeedAndChains_ProducesIdenticalBest()
    {
        // The determinism contract: same baseSeed + chainCount +
        // maxIter must give bit-identical (BestParams, BestScore)
        // regardless of OS scheduler interleaving.
        var bounds = StandardBounds(8);
        var opt1 = new MultiChainOptimizer(bounds, maxIterations: 200, baseSeed: 42, chainCount: 4);
        var opt2 = new MultiChainOptimizer(bounds, maxIterations: 200, baseSeed: 42, chainCount: 4);

        var r1 = opt1.Run(ConvexEvaluator);
        var r2 = opt2.Run(ConvexEvaluator);

        Assert.Equal(r1.BestScore, r2.BestScore);
        Assert.Equal(r1.BestParams, r2.BestParams);
        Assert.Equal(r1.WinningChain, r2.WinningChain);
    }

    [Fact]
    public void StrictDeterminism_HoldsAcross10Runs()
    {
        // Stress test: run 10 times back-to-back, all must produce
        // identical results. Catches subtle non-determinism (e.g. a
        // Dictionary iteration order leak) that single-pair test misses.
        var bounds = StandardBounds(4);
        var firstResult = new MultiChainOptimizer(bounds, 100, baseSeed: 99, chainCount: 4)
            .Run(ConvexEvaluator);
        for (int i = 0; i < 9; i++)
        {
            var r = new MultiChainOptimizer(bounds, 100, baseSeed: 99, chainCount: 4)
                .Run(ConvexEvaluator);
            Assert.Equal(firstResult.BestScore, r.BestScore);
            Assert.Equal(firstResult.BestParams, r.BestParams);
        }
    }

    // Asymmetric objective for the seed-difference test: minimum is at
    // [0.3, 0.35, 0.4, 0.45, ...] so neither Sobol's index-1 point
    // ([0.5, 0.5, ...]) nor any obvious uniform sample is the global
    // optimum. Without this, post-L3 Sobol warmup hits the convex
    // minimum on iter 1 for any seed, defeating the seed-difference
    // sanity check.
    private static (double, object?) AsymmetricEvaluator(double[] x)
    {
        double sum = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double target = 0.3 + 0.05 * i;
            double d = x[i] - target;
            sum += d * d;
        }
        return (sum, null);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentResults()
    {
        // Sanity check that determinism isn't a no-op — different seeds
        // should explore different states under a partial-convergence
        // iteration budget. AsymmetricEvaluator + a small Sobol warmup
        // (Sobol is seed-independent so a long warmup would mask seed
        // divergence) gives the SA RNGs enough room to walk apart.
        var bounds = StandardBounds(8);
        var r1 = new MultiChainOptimizer(bounds, maxIterations: 200,
                                         baseSeed: 1, chainCount: 4,
                                         sobolWarmupCount: 8).Run(AsymmetricEvaluator);
        var r2 = new MultiChainOptimizer(bounds, maxIterations: 200,
                                         baseSeed: 2, chainCount: 4,
                                         sobolWarmupCount: 8).Run(AsymmetricEvaluator);
        Assert.NotEqual(r1.BestParams, r2.BestParams);
    }

    [Fact]
    public void Run_CancellationDuringEvaluation_ReturnsBestSoFar_DoesNotThrow()
    {
        // Regression: a CancellationToken tripped *inside* the evaluator —
        // EngineObjectiveAdapter.Evaluate calls ThrowIfCancellationRequested,
        // so the objective itself throws OperationCanceledException
        // mid-evaluate — used to escape the internal Parallel.For as an
        // AggregateException, so Run THREW instead of honouring the
        // documented "returns best-so-far on cancellation (after ≥1
        // iteration)" contract. It intermittently reddened airbreathing-tests
        // (AirbreathingOptimizeTests.Cancellation_HonouredWithinReasonableTime).
        //
        // The evaluator below lets a few evaluations land (so best-so-far is
        // populated), then cancels and throws OCE mid-evaluate — exactly the
        // race, but deterministic. Run must swallow it and return.
        var bounds = StandardBounds(4);
        using var cts = new CancellationTokenSource();
        int calls = 0;

        (double, object?) CancellingEvaluator(double[] x)
        {
            if (Interlocked.Increment(ref calls) >= 4)
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            }
            return ConvexEvaluator(x);
        }

        var opt = new MultiChainOptimizer(bounds, maxIterations: 10_000, baseSeed: 42, chainCount: 2);

        object? result = null;
        var ex = Record.Exception(() =>
        {
            result = opt.Run(CancellingEvaluator, cancellationToken: cts.Token);
        });

        Assert.Null(ex);        // pre-fix: AggregateException(OperationCanceledException)
        Assert.NotNull(result); // best-so-far Result, not a throw
    }

    [Fact]
    public void MultiChain_ConvergesCloseToKnownMinimum_OnConvexObjective()
    {
        // Smoke check: 4 chains × 500 iterations should land within ~0.1
        // of the true minimum (x = 0.5 in each dim, score 0). Multi-chain
        // converges faster than single-chain on convex problems thanks
        // to migration + Sobol warmup.
        var bounds = StandardBounds(4);
        var opt = new MultiChainOptimizer(bounds, maxIterations: 500, baseSeed: 7, chainCount: 4);
        var r = opt.Run(ConvexEvaluator);
        Assert.True(r.BestScore < 0.1,
            $"expected best score < 0.1 after 4×500 iters, got {r.BestScore:F4}");
    }

    [Fact]
    public void SingleChain_DegenerateChainCount1_StillWorks()
    {
        // chainCount=1 should degenerate to pre-T1.1 single-chain SA
        // (no migration, no Parallel.For overhead beyond a single task).
        var bounds = StandardBounds(4);
        var opt = new MultiChainOptimizer(bounds, maxIterations: 200, baseSeed: 5, chainCount: 1);
        var r = opt.Run(ConvexEvaluator);
        Assert.Equal(1, r.ChainCount);
        Assert.True(r.BestScore < 0.5);
    }

    [Fact]
    public void SobolWarmup_FirstKEvaluationsUseSobolPoints()
    {
        // Capture the first K candidates each chain evaluates. With Sobol
        // warmup enabled (default), the first 64 candidates per chain
        // should be from the Sobol sequence (deterministic, low-
        // discrepancy), NOT random uniform from the SA's RandomCandidate.
        // We can't directly observe internal candidates without
        // instrumentation, but we CAN verify that the same Sobol-warmup
        // configuration produces deterministic results AND differs from
        // useSobolSeeding: false runs.
        var bounds = StandardBounds(8);
        var withSobol = new MultiChainOptimizer(bounds, 100, baseSeed: 17, chainCount: 4,
            useSobolSeeding: true).Run(ConvexEvaluator);
        var withoutSobol = new MultiChainOptimizer(bounds, 100, baseSeed: 17, chainCount: 4,
            useSobolSeeding: false).Run(ConvexEvaluator);
        // Different seeding strategy → different candidate trajectory →
        // different best (in general).
        Assert.NotEqual(withSobol.BestParams, withoutSobol.BestParams);
    }

    [Fact]
    public void Result_ReportsAllChainSummaries()
    {
        var bounds = StandardBounds(4);
        var opt = new MultiChainOptimizer(bounds, 100, baseSeed: 11, chainCount: 4);
        var r = opt.Run(ConvexEvaluator);

        Assert.Equal(4, r.Chains.Count);
        Assert.Equal(4, r.ChainCount);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i, r.Chains[i].ChainIndex);
            Assert.Equal(11 + i, r.Chains[i].Seed);
            Assert.True(r.Chains[i].IterationsCompleted > 0);
        }
        // Winning chain's BestScore must equal the global BestScore.
        Assert.Equal(r.BestScore, r.Chains[r.WinningChain].BestScore);
    }

    [Fact]
    public void TotalIterations_SumsAcrossChains()
    {
        var bounds = StandardBounds(4);
        var r = new MultiChainOptimizer(bounds, 100, baseSeed: 3, chainCount: 4).Run(ConvexEvaluator);
        int summed = r.Chains.Sum(c => c.IterationsCompleted);
        Assert.Equal(summed, r.TotalIterations);
    }

    [Fact]
    public async System.Threading.Tasks.Task EarlyInfeasibleExit_DoesNotDeadlockBarrier()
    {
        // Failure mode this test guards: if one chain trips infeasible-exit
        // at iter 60 (Optimizer's MaxConsecutiveInfeasibleBeforeExit
        // default), the OTHER chains might wait forever at the next
        // migration barrier (every 100 iters). MultiChainOptimizer must
        // call Barrier.RemoveParticipant when a chain finishes early.
        var bounds = StandardBounds(4);
        // Score = +Inf on every candidate → all chains trip infeasible-exit.
        Func<double[], (double, object?)> infeasibleEvaluator = _ => (double.PositiveInfinity, null);
        var opt = new MultiChainOptimizer(bounds, maxIterations: 1000, baseSeed: 13, chainCount: 4,
            migrationCadence: 100);
        var task = System.Threading.Tasks.Task.Run(() => opt.Run(infeasibleEvaluator));
        var winner = await System.Threading.Tasks.Task.WhenAny(task, System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.Same(task, winner);  // task completed, not the timeout
        var result = await task;
        Assert.True(result.BestScore == double.PositiveInfinity || result.BestScore == 0);
    }

    [Fact]
    public void Constructor_RejectsInvalidArgs()
    {
        var bounds = StandardBounds(4);
        Assert.Throws<ArgumentNullException>(() =>
            new MultiChainOptimizer(bounds: (((double Min, double Max)[])null!), 100, 0));
        Assert.Throws<ArgumentException>(() =>
            new MultiChainOptimizer(Array.Empty<(double, double)>(), 100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MultiChainOptimizer(bounds, maxIterations: 0, baseSeed: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MultiChainOptimizer(bounds, maxIterations: 100, baseSeed: 0, migrationCadence: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MultiChainOptimizer(bounds, maxIterations: 100, baseSeed: 0, sobolWarmupCount: -1));
    }

    [Fact]
    public void Run_ThrowsOnNullEvaluator()
    {
        var bounds = StandardBounds(4);
        var opt = new MultiChainOptimizer(bounds, 100, 0);
        Assert.Throws<ArgumentNullException>(() =>
            opt.Run(evaluator: ((Func<double[], (double, object?)>)null!)));
    }

    [Fact]
    public void Run_BroadcastsInitialCandidateToAllChains()
    {
        // When initialCandidate is provided, every chain starts from it.
        // Verifying this directly requires instrumentation; we verify
        // indirectly: with a near-optimum initial candidate, the result
        // best should be very close to optimum even with few iterations.
        var bounds = StandardBounds(4);
        var initial = new[] { 0.5, 0.5, 0.5, 0.5 };  // exact minimum
        var opt = new MultiChainOptimizer(bounds, maxIterations: 50, baseSeed: 19, chainCount: 4,
            useSobolSeeding: false);  // disable Sobol to make initial candidate the actual start
        var r = opt.Run(ConvexEvaluator, initialCandidate: initial);
        Assert.True(r.BestScore < 0.01,
            $"expected best ~0 with optimum initial candidate, got {r.BestScore:F4}");
    }

    [Fact]
    public void MigrateFrom_OnSimulatedAnnealingOptimizer_ValidatesDimMismatch()
    {
        var bounds = StandardBounds(4);
        var opt = new SimulatedAnnealingOptimizer(bounds, 100, 0);
        Assert.Throws<ArgumentException>(() =>
            opt.MigrateFrom(new[] { 0.5, 0.5 }, 1.0));   // wrong dim
    }

    [Fact]
    public void MigrateFrom_DoesNotResetBest_IfMigratedScoreIsWorse()
    {
        // Migration replaces _current but not _best (unless migrated
        // score beats the local best). This preserves chain diversity:
        // chains keep memory of their own best even after receiving
        // migrants.
        var bounds = StandardBounds(2);
        var opt = new SimulatedAnnealingOptimizer(bounds, 100, 0);
        opt.SetInitialCandidate(new[] { 0.5, 0.5 });
        // Drive the chain to find a local best at score 0.
        opt.ReportScore(new[] { 0.5, 0.5 }, 0.0, null);
        Assert.Equal(0.0, opt.BestScore);

        // Migrate in a worse candidate. Best should NOT change.
        opt.MigrateFrom(new[] { 0.9, 0.9 }, migratedScore: 5.0);
        Assert.Equal(0.0, opt.BestScore);
        Assert.Equal(new[] { 0.5, 0.5 }, opt.BestParams);
    }

    [Fact]
    public void MigrateFrom_UpdatesBest_IfMigratedScoreIsBetter()
    {
        var bounds = StandardBounds(2);
        var opt = new SimulatedAnnealingOptimizer(bounds, 100, 0);
        opt.SetInitialCandidate(new[] { 0.5, 0.5 });
        opt.ReportScore(new[] { 0.5, 0.5 }, 5.0, null);

        // Migrate in a better candidate.
        opt.MigrateFrom(new[] { 0.7, 0.7 }, migratedScore: 1.0);
        Assert.Equal(1.0, opt.BestScore);
        Assert.Equal(new[] { 0.7, 0.7 }, opt.BestParams);
    }

    // Z2 #11 / M3a (2026-04-29): pin the correctness invariant for T1.5
    // pre-screen. A candidate that pre-screen rejects with +Inf must
    // produce the same +Inf score the full evaluator would have on its
    // own. The SA optimizer's RNG trajectory depends only on the returned
    // score (not on the breakdown object), so running multi-chain SA with
    // vs without a pre-screen wrapper on the same baseSeed must produce
    // identical (BestParams, BestScore, WinningChain, TotalIterations).
    //
    // This is the synthetic-corpus version of the audit's "100-iter SA on
    // a wide-open corpus" check. Production gates that PreScreen catches
    // (CONTRACTION_RATIO_OUT_OF_BAND / L_STAR_BELOW_PROPELLANT_MIN /
    // TPMS_CELL_FEATURE_TOO_SMALL) are a strict subset of the gates the
    // full Evaluate pipeline checks, so this invariant transfers: as
    // long as PreScreen and Evaluate agree on +Inf, the SA trajectory is
    // bit-identical regardless of which path discovered the rejection.
    //
    // Companion: T1_5PreScreenTests.PreScreen_TpmsStrutFormula_AgreesWithFullEval
    // pins the formula-direction invariant (sign-of-rejection parity);
    // this test pins the SA-trajectory invariant (score-equivalence under
    // the optimizer).
    [Fact]
    public void PreScreen_DeterminismInvariant_WithVsWithoutProducesIdenticalSA()
    {
        var bounds = StandardBounds(8);

        // Full evaluator: convex sum-of-squares with a band reject on
        // x[0] mimicking the CONTRACTION_RATIO_OUT_OF_BAND gate that
        // both PreScreen and Evaluate would catch. The standard convex
        // bounds [0.0, 1.0] put a meaningful fraction of random samples
        // outside [0.1, 0.9], so the reject path is exercised on real
        // SA candidates (not just the trivial all-feasible case).
        static (double, object?) FullEval(double[] x)
        {
            if (x[0] < 0.1 || x[0] > 0.9)
                return (double.PositiveInfinity, "BAND_REJECT");
            double sum = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double d = x[i] - 0.5;
                sum += d * d;
            }
            return (sum, "OK");
        }

        // Pre-screened evaluator: catches the same condition early and
        // returns the same +Inf score. The breakdown tag differs to
        // match production semantics (pre-screen path emits a distinct
        // diagnostic), but the optimizer's trajectory depends only on
        // the returned score.
        static (double, object?) PreScreenedEval(double[] x)
        {
            if (x[0] < 0.1 || x[0] > 0.9)
                return (double.PositiveInfinity, "PRE_SCREEN");
            return FullEval(x);
        }

        var rWith = new MultiChainOptimizer(bounds, maxIterations: 100,
                                            baseSeed: 42, chainCount: 4)
            .Run(PreScreenedEval);
        var rWithout = new MultiChainOptimizer(bounds, maxIterations: 100,
                                               baseSeed: 42, chainCount: 4)
            .Run(FullEval);

        Assert.Equal(rWithout.BestScore, rWith.BestScore);
        Assert.Equal(rWithout.BestParams, rWith.BestParams);
        Assert.Equal(rWithout.WinningChain, rWith.WinningChain);
        Assert.Equal(rWithout.TotalIterations, rWith.TotalIterations);
    }

    // Companion to PreScreen_DeterminismInvariant_*: pins the same
    // invariant under the single-chain (chainCount=1) degenerate path.
    // Multi-chain has a barrier-protected migration step every 100
    // iters; single-chain doesn't. A pre-screen change that broke
    // single-chain determinism without breaking multi-chain (or vice
    // versa) would slip past one of the two; covering both is cheap.
    [Fact]
    public void PreScreen_DeterminismInvariant_HoldsOnSingleChainDegenerate()
    {
        var bounds = StandardBounds(8);

        static (double, object?) FullEval(double[] x)
        {
            if (x[0] < 0.1 || x[0] > 0.9)
                return (double.PositiveInfinity, "BAND_REJECT");
            double sum = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double d = x[i] - 0.5;
                sum += d * d;
            }
            return (sum, "OK");
        }
        static (double, object?) PreScreenedEval(double[] x)
        {
            if (x[0] < 0.1 || x[0] > 0.9)
                return (double.PositiveInfinity, "PRE_SCREEN");
            return FullEval(x);
        }

        var rWith    = new MultiChainOptimizer(bounds, 100, baseSeed: 42, chainCount: 1)
            .Run(PreScreenedEval);
        var rWithout = new MultiChainOptimizer(bounds, 100, baseSeed: 42, chainCount: 1)
            .Run(FullEval);

        Assert.Equal(rWithout.BestScore, rWith.BestScore);
        Assert.Equal(rWithout.BestParams, rWith.BestParams);
    }

    // Sanity check that the band reject is actually being exercised — if
    // the convex objective somehow stays inside [0.1, 0.9] on x[0] for
    // every SA candidate, the determinism test above degenerates to
    // "FullEval == FullEval" and proves nothing. Count rejections
    // directly; require ≥ 1 (the reject path was hit at least once).
    [Fact]
    public void PreScreen_DeterminismHarness_ActuallyExercisesRejectPath()
    {
        var bounds = StandardBounds(8);
        int rejectCount = 0;
        var lck = new object();
        (double, object?) Eval(double[] x)
        {
            if (x[0] < 0.1 || x[0] > 0.9)
            {
                lock (lck) { rejectCount++; }
                return (double.PositiveInfinity, "BAND_REJECT");
            }
            double sum = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double d = x[i] - 0.5;
                sum += d * d;
            }
            return (sum, "OK");
        }

        new MultiChainOptimizer(bounds, maxIterations: 100,
            baseSeed: 42, chainCount: 4).Run(Eval);

        Assert.True(rejectCount > 0,
            $"determinism harness expected ≥ 1 band-reject hit; got {rejectCount}. "
          + "If this fails, the harness no longer exercises the reject path and "
          + "the companion determinism tests degenerate to no-ops.");
    }
}
