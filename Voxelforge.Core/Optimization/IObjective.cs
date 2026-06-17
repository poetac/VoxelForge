// IObjective.cs — Engine-family-agnostic optimizer evaluator boundary.
//
// Promotes MultiChainOptimizer's Func<double[], (score, breakdown)>
// shape to a real interface so each engine family (regen rocket,
// aerospike, ramjet, turbojet, gas turbine, ...) can ship its own
// IObjective implementation, and the optimizer + voxelforge-eval
// subprocess oracle stay engine-agnostic.
//
// Per the IObjective decoupling design (2026-04-28): "the
// optimizer thinks it's pluggable, but the wiring around it is
// hardcoded." The Func<>-based path remains supported as the legacy
// route for callers that don't have an IObjective; the IObjective
// route is the recommended shape for all new optimizer integrations
// (CMA-ES, NSGA-II, Bayesian opt via the subprocess oracle).
//
// Threading contract — implementations MUST be thread-safe.
// MultiChainOptimizer.Run invokes Evaluate concurrently from N chain
// tasks. The pre-existing Regen pipeline is already pure over
// immutable RegenChamberDesign records (ADR-011) + uses per-call
// fluid-state caches (P22 fix), so its IObjective wrapper is
// trivially thread-safe.
//
// Determinism contract — the Score returned for a given
// (vector, deterministic-state) pair must be reproducible. SA's
// strict-determinism guarantee depends on this: two MultiChainOptimizer
// runs against the same IObjective + same baseSeed + same chainCount
// must produce bit-identical (BestParams, BestScore). Any non-determinism
// inside Evaluate (DateTime.Now, unseeded Random, Dictionary iteration
// order on FP keys) silently breaks the contract.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Voxelforge.Optimization;

/// <summary>
/// Result of one <see cref="IObjective.Evaluate"/> call. The
/// <see cref="Score"/> is the scalar the optimizer minimises;
/// <see cref="Violations"/> surfaces hard-constraint failures as
/// first-class data (vs. having to peek at engine-specific
/// breakdown fields); <see cref="EngineSpecificBreakdown"/> carries
/// the engine-family-specific score record (e.g.
/// <c>RegenScoreResult</c>) for downstream UI / report consumers.
/// </summary>
/// <param name="Score">
/// Scalar the optimizer minimises. <see cref="double.PositiveInfinity"/>
/// is the canonical infeasible sentinel — SA never accepts a +Inf
/// candidate as a new best.
/// </param>
/// <param name="Violations">
/// Hard-constraint violations from the engine family's feasibility
/// gate set. Empty when feasible. Populated even when
/// <see cref="Score"/> is finite (advisory gates that don't trigger
/// infeasibility), so consumers can distinguish "feasible with
/// warnings" from "feasible clean."
/// </param>
/// <param name="EngineSpecificBreakdown">
/// Opaque engine-family-specific score breakdown. For the regen
/// rocket family this is a <c>RegenScoreResult</c>; for aerospike
/// it's an aerospike-shaped breakdown. The <see cref="MultiChainOptimizer"/>
/// stores this on its <c>BestBreakdown</c> for the winning chain so
/// the UI / report layer can downcast and render engine-specific
/// detail.
/// </param>
public readonly record struct EvaluationResult(
    double Score,
    IReadOnlyList<FeasibilityViolation> Violations,
    object? EngineSpecificBreakdown);

/// <summary>
/// Engine-family-agnostic optimizer evaluator. Each engine family
/// (regen rocket, aerospike, ramjet, turbojet, ...) ships an
/// <c>IObjective</c> implementation; the optimizer and external
/// tooling consume the interface without knowing what kind of engine
/// is on the other side.
/// <para>
/// Implementations MUST be thread-safe (<see cref="MultiChainOptimizer"/>
/// invokes <see cref="Evaluate"/> concurrently from N chain tasks)
/// and deterministic in the score-vs-vector mapping (the optimizer's
/// strict-determinism contract depends on it).
/// </para>
/// </summary>
public interface IObjective
{
    /// <summary>
    /// Number of dimensions in the search vector. Equal to
    /// <c>Variables.Count</c>. Exposed separately as a
    /// hot-path-friendly fast accessor.
    /// </summary>
    int DimensionCount { get; }

    /// <summary>
    /// Per-dimension descriptors: name + bounds. The optimizer reads
    /// <c>(Min, Max)</c> directly to drive sampling; reporting tools
    /// read <see cref="DesignVariableInfo.Name"/> for axis labels and
    /// gate-violation context. Length must equal
    /// <see cref="DimensionCount"/>.
    /// </summary>
    IReadOnlyList<DesignVariableInfo> Variables { get; }

    /// <summary>
    /// Evaluate one candidate vector. Length of <paramref name="vector"/>
    /// must equal <see cref="DimensionCount"/>; implementations should
    /// throw <see cref="ArgumentException"/> on mismatch.
    /// <para>
    /// Cancellation should be honoured at natural boundaries (between
    /// solver stations, between gate evaluations) where practical.
    /// Tight per-iteration loops are not required to poll the token —
    /// the optimizer's outer loop polls cancellation at every iteration
    /// boundary, so per-Evaluate cancellation is a courtesy, not a
    /// correctness invariant.
    /// </para>
    /// </summary>
    EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default);
}
