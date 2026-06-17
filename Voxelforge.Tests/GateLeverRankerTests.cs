// GateLeverRankerTests — issue #347 Phase 2 (Sobol-driven gate-lever ranker).
//
// Covers the pure-data ranker contract: synthetic monotonic /
// dim-dominant evaluators produce the expected ordering. Avoids any
// physics oracle so the test suite stays fast and deterministic.

using System;
using System.Linq;
using Voxelforge.Injector;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class GateLeverRankerTests
{
    // ─── helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Synthetic dim-dominant evaluator: f(x) = α₀·x[0] + α₁·x[1] + ...
    /// where the coefficients decay so dim 0 dominates the variance.
    /// Sobol should return ST descending in dim order.
    /// </summary>
    private static Func<double[], double> DecayingLinear(double[] coeffs)
        => x =>
        {
            double sum = 0;
            for (int i = 0; i < x.Length; i++) sum += coeffs[i] * x[i];
            return sum;
        };

    private static Func<double[], double> Constant(double c)
        => _ => c;

    // ─── argument validation ──────────────────────────────────────────

    [Fact]
    public void Rank_NullConstraintId_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => GateLeverRanker.Rank(null!, Constant(1.0)));
    }

    [Fact]
    public void Rank_NullCallback_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => GateLeverRanker.Rank("WALL_TEMP", null!));
    }

    // ─── empty / unknown gate paths ───────────────────────────────────

    [Fact]
    public void Rank_NpshGate_HasNoSaCoupledVariables_ReturnsEmpty()
    {
        // NPSH_INSUFFICIENT carries an empty CoupledVariables list (Phase 1
        // decision — its physical levers all live on OperatingConditions /
        // pump preset, not the SA vector).
        var result = GateLeverRanker.Rank("NPSH_INSUFFICIENT", Constant(1.0));
        Assert.Empty(result);
    }

    [Fact]
    public void Rank_UncoveredGate_ReturnsEmpty()
    {
        // TURBINE_UNCHOKED is registered in GateRegistry but uncovered by
        // the explainer hint table; the ranker should bail out cleanly.
        var result = GateLeverRanker.Rank("TURBINE_UNCHOKED", Constant(1.0));
        Assert.Empty(result);
    }

    [Fact]
    public void Rank_UnregisteredGate_ReturnsEmpty()
    {
        var result = GateLeverRanker.Rank("MADE_UP_DOES_NOT_EXIST", Constant(1.0));
        Assert.Empty(result);
    }

    // ─── ranking correctness on synthetic dim-dominant evaluator ──────

    [Fact]
    public void Rank_DecayingLinear_OrdersByTotalSensitivityDescending()
    {
        // FEATURE_TOO_SMALL has 3 coupled vars. Use coefficients that put
        // dim 0 ≫ dim 1 ≫ dim 2 in variance contribution.
        var coupled = GateExplainer.GetCoupledVariables("FEATURE_TOO_SMALL");
        Assert.True(coupled.Count >= 3, "test fixture assumes ≥3 coupled vars");

        var coeffs = new[] { 4.0, 1.0, 0.1 };
        var ranker = DecayingLinear(coeffs);

        var ranked = GateLeverRanker.Rank("FEATURE_TOO_SMALL", ranker, N: 256, seed: 7);

        // The largest coefficient (dim 0 in coupled order) should rank #1
        // by total ST. We don't pin which specific variable is first since
        // that's coupled to the Phase 1 hand-authored ordering; we just
        // assert ST is non-increasing.
        Assert.Equal(coupled.Count, ranked.Length);
        for (int i = 0; i < ranked.Length - 1; i++)
        {
            Assert.True(ranked[i].TotalST >= ranked[i + 1].TotalST,
                $"ST not non-increasing at i={i}: {ranked[i].TotalST} vs {ranked[i + 1].TotalST}");
        }
    }

    [Fact]
    public void Rank_ConstantEvaluator_ReturnsZeroIndices()
    {
        var ranked = GateLeverRanker.Rank("WALL_TEMP", Constant(42.0), N: 64, seed: 1);

        Assert.NotEmpty(ranked);
        foreach (var lever in ranked)
        {
            Assert.Equal(0.0, lever.FirstOrderS, 6);
            Assert.Equal(0.0, lever.TotalST,     6);
        }
    }

    // ─── determinism + reproducibility ────────────────────────────────

    [Fact]
    public void Rank_IsDeterministic_GivenSameSeed()
    {
        var f = DecayingLinear(new[] { 2.0, 1.0, 0.5 });
        var a = GateLeverRanker.Rank("FEATURE_TOO_SMALL", f, N: 64, seed: 42);
        var b = GateLeverRanker.Rank("FEATURE_TOO_SMALL", f, N: 64, seed: 42);

        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].VariableName, b[i].VariableName);
            Assert.Equal(a[i].FirstOrderS,  b[i].FirstOrderS,  10);
            Assert.Equal(a[i].TotalST,      b[i].TotalST,      10);
        }
    }

    // ─── SaIndex lookup correctness ───────────────────────────────────

    [Fact]
    public void Rank_PopulatesSaIndex_ForRegenChamberDesignVars()
    {
        var ranked = GateLeverRanker.Rank(
            "WALL_TEMP",
            DecayingLinear(new[] { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 }),
            N: 32, seed: 0);

        // All WALL_TEMP coupled vars are SA-tagged on RegenChamberDesign.
        foreach (var lever in ranked)
        {
            Assert.True(lever.SaIndex >= 0,
                $"{lever.VariableName} should have an SA index ≥ 0; got {lever.SaIndex}");
        }
    }

    [Fact]
    public void Rank_PopulatesSaIndex_ForInjectorPatternVars()
    {
        // INJECTOR_FACE_T_EXCEEDED couples to ElementCount + OuterRowFilmFraction
        // (both on InjectorPattern, SA-tagged) plus three RegenChamberDesign vars.
        var coupled = GateExplainer.GetCoupledVariables("INJECTOR_FACE_T_EXCEEDED");
        Assert.Contains(nameof(InjectorPattern.ElementCount), coupled);

        // Build coefficients matching the coupled-var count.
        var coeffs = Enumerable.Repeat(1.0, coupled.Count).ToArray();
        var ranked = GateLeverRanker.Rank(
            "INJECTOR_FACE_T_EXCEEDED",
            DecayingLinear(coeffs),
            N: 32, seed: 0);

        foreach (var lever in ranked)
        {
            Assert.True(lever.SaIndex >= 0,
                $"{lever.VariableName} should have an SA index ≥ 0; got {lever.SaIndex}");
        }
    }
}
