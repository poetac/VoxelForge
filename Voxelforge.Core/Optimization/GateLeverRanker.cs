// GateLeverRanker.cs — OOB-13 part 2 Phase 2 (issue #347).
//
// Ranks the SA-tunable design variables coupled to a feasibility gate
// by Sobol-decomposition sensitivity to the gate's metric value.
// Consumes the gate→variable coupling map shipped in Phase 1
// (GateExplainer.GetCoupledVariables) and the Sobol Saltelli estimator
// moved from Voxelforge.Benchmarks → Voxelforge.Core in this PR.
//
// API design: takes a Func<double[], double> callback that evaluates
// the gate's metric on a unit-hypercube perturbation. The caller is
// responsible for mapping the hypercube point to a perturbed
// RegenChamberDesign + running the physics oracle + extracting the
// metric. This keeps Core dependency-free of the orchestrator and
// matches IObjective's strategy seam (PR #155, ADR-021).
//
// Ordering: results are returned sorted by total Sobol index (ST_i)
// descending — the variable with the largest cumulative contribution
// to gate-metric variance comes first. Ties broken by first-order
// index (S_i) descending, then by SA index ascending (deterministic).

using System;
using System.Linq;

namespace Voxelforge.Optimization;

/// <summary>
/// One ranked lever entry: the SA-tunable design variable, its first-
/// order and total Sobol indices, and (in a future slice) the
/// recommended direction. Returned by
/// <see cref="GateLeverRanker.Rank"/> sorted by
/// <see cref="TotalST"/> descending.
/// </summary>
/// <param name="VariableName">Property name on
/// <see cref="RegenChamberDesign"/> or
/// <see cref="Voxelforge.Injector.InjectorPattern"/> (matches
/// <c>nameof()</c> output for the SA-tagged property).</param>
/// <param name="SaIndex">The SA-vector slot index for this variable
/// (from <see cref="SaDesignVariableAttribute.Index"/>), or <c>-1</c>
/// when not tagged.</param>
/// <param name="FirstOrderS">Saltelli first-order Sobol index
/// (S_i) — share of metric variance explained by this variable
/// alone.</param>
/// <param name="TotalST">Saltelli total Sobol index (ST_i) — share
/// of metric variance involving this variable, including
/// interactions with other coupled variables.</param>
internal sealed record RankedLever(
    string VariableName,
    int    SaIndex,
    double FirstOrderS,
    double TotalST);

/// <summary>
/// Sobol-driven ranker for the SA-tunable levers coupled to a
/// feasibility gate. Phase 2 of issue #347 — consumes the Phase 1
/// gate→variable coupling map.
/// </summary>
internal static class GateLeverRanker
{
    /// <summary>
    /// Rank the SA-tunable variables coupled to a gate by Sobol
    /// sensitivity to the supplied gate-metric callback.
    /// </summary>
    /// <param name="constraintId">Stable gate ID (matches
    /// <see cref="FeasibilityViolation.ConstraintId"/>).</param>
    /// <param name="evalGateMetric">Callback: takes a unit-hypercube
    /// point of length <c>k</c> (where <c>k</c> is the number of
    /// coupled variables for the gate) and returns the gate's metric
    /// value at the corresponding perturbed design. Must be
    /// deterministic for a given input.</param>
    /// <param name="N">Saltelli sample size; total callback evals
    /// = N(k+2). Default 64 ≈ 5 s wall-clock at ~50 ms/eval for a
    /// gate with 5 coupled vars.</param>
    /// <param name="seed">RNG seed for reproducibility.</param>
    /// <returns>One <see cref="RankedLever"/> per coupled variable,
    /// sorted by total Sobol index descending (then S_i, then SA
    /// index for deterministic tie-break). Empty array when the gate
    /// has no SA-tunable coupled variables (e.g.,
    /// <c>NPSH_INSUFFICIENT</c>).</returns>
    public static RankedLever[] Rank(
        string constraintId,
        Func<double[], double> evalGateMetric,
        int N = 64,
        int seed = 42)
    {
        if (constraintId is null) throw new ArgumentNullException(nameof(constraintId));
        if (evalGateMetric is null) throw new ArgumentNullException(nameof(evalGateMetric));

        var coupled = GateExplainer.GetCoupledVariables(constraintId);
        if (coupled.Count == 0) return Array.Empty<RankedLever>();

        var sobolIndices = SobolSensitivity.Compute(
            evalGateMetric,
            coupled.Count,
            N: N,
            seed: seed,
            dimNames: coupled);

        // Deterministic ordering: ST descending → S descending → SA index ascending.
        // SA-index lookup is O(D) per call where D is the SA-vector size; cheap
        // enough for the small coupled-variable lists this method handles.
        var saIndexLookup = BuildSaIndexLookup();
        return sobolIndices
            .Select(idx => new RankedLever(
                VariableName: idx.DimName,
                SaIndex:      saIndexLookup.TryGetValue(idx.DimName, out var ix) ? ix : -1,
                FirstOrderS:  idx.FirstOrder,
                TotalST:      idx.Total))
            .OrderByDescending(r => r.TotalST)
            .ThenByDescending(r => r.FirstOrderS)
            .ThenBy(r => r.SaIndex)
            .ToArray();
    }

    private static System.Collections.Generic.Dictionary<string, int> BuildSaIndexLookup()
    {
        var d = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var v in DesignVariableRegistry.For(typeof(RegenChamberDesign)))
            d[v.MemberName] = v.Index;
        foreach (var v in DesignVariableRegistry.For(typeof(Voxelforge.Injector.InjectorPattern)))
            d[v.MemberName] = v.Index;
        return d;
    }
}
