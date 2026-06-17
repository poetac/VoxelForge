// EngineObjectiveAdapter.cs — Sprint A Phase 2 (2026-05-04).
//
// Engine-family-agnostic IObjective adapter that closes over an
// IEngine<TDesign, TConditions, TResult> instance. Eliminates the
// per-family boilerplate that RamjetObjective, TurbojetObjective,
// etc. share: each stores conditions + variables + unpack + evaluate.
//
// Usage:
//   var adapter = new EngineObjectiveAdapter<
//       RegenChamberDesign, OperatingConditions, RocketEngineResult>(
//       engine:    RocketEngine.Instance,
//       conditions: myConditions,
//       baseline:   myDesign,
//       variables:  variableInfoArray,
//       unpack:    (vec, baseline) => RegenChamberOptimization.Unpack(vec, baseline),
//       evaluate:  result => {
//           var score = RegenChamberOptimization.Evaluate(result.Generation);
//           return new EvaluationResult(score.TotalScore, result.Violations, score);
//       });
//
// Thread-safety: the adapter itself is stateless after construction.
// Thread-safety of Evaluate calls depends on the supplied delegates;
// both the rocket and airbreathing pipelines are documented thread-safe.
//
// Cancellation: CancellationToken is checked before the engine call.
// Long-running engine Evaluate calls do not observe the token internally
// (neither GenerateWith nor the airbreathing solver does); cancellation
// applies at the IObjective granularity, not sub-step.

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Engines;

namespace Voxelforge.Optimization;

/// <summary>
/// Generic <see cref="IObjective"/> adapter over an
/// <see cref="IEngine{TDesign,TConditions,TResult}"/> instance.
/// Supplies the SA vector unpack + result-to-score mapping via
/// injected delegates; the engine handles the physics evaluation.
/// Thread-safe and deterministic when all supplied delegates are.
/// </summary>
public sealed class EngineObjectiveAdapter<TDesign, TConditions, TResult>
    : IObjective
    where TDesign    : IEngineDesign
    where TConditions: IEngineConditions
    where TResult    : IEngineResult
{
    private readonly IEngine<TDesign, TConditions, TResult> _engine;
    private readonly TConditions                            _conditions;
    private readonly TDesign                                _baseline;
    private readonly IReadOnlyList<DesignVariableInfo>      _variables;
    private readonly Func<double[], TDesign, TDesign>       _unpack;
    private readonly Func<TResult, EvaluationResult>        _evaluate;

    /// <inheritdoc />
    public int DimensionCount => _variables.Count;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _variables;

    /// <summary>
    /// Construct a generic engine objective adapter.
    /// </summary>
    /// <param name="engine">Engine instance to call for physics evaluation.</param>
    /// <param name="conditions">Fixed operating / flight conditions.</param>
    /// <param name="baseline">
    /// Baseline design. Categorical state (cycle, propellant pair, topology,
    /// injector pattern) that the optimizer must not perturb is preserved here
    /// and forwarded to <paramref name="unpack"/> on every call.
    /// </param>
    /// <param name="variables">
    /// Variable metadata (name + bounds), one entry per SA dimension. Length
    /// must equal the vector length passed to <see cref="Evaluate"/>.
    /// </param>
    /// <param name="unpack">
    /// Projects a candidate SA vector plus the baseline design onto a concrete
    /// design record. Called on every <see cref="Evaluate"/> invocation; must
    /// be thread-safe and allocation-efficient for the SA hot path.
    /// </param>
    /// <param name="evaluate">
    /// Maps the engine result to a scored <see cref="EvaluationResult"/>.
    /// Responsible for extracting the scalar score, violation list, and any
    /// engine-specific breakdown the caller wants on the result.
    /// </param>
    public EngineObjectiveAdapter(
        IEngine<TDesign, TConditions, TResult> engine,
        TConditions                            conditions,
        TDesign                                baseline,
        IReadOnlyList<DesignVariableInfo>      variables,
        Func<double[], TDesign, TDesign>       unpack,
        Func<TResult, EvaluationResult>        evaluate)
    {
        _engine     = engine     ?? throw new ArgumentNullException(nameof(engine));
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        _baseline   = baseline   ?? throw new ArgumentNullException(nameof(baseline));
        _variables  = variables  ?? throw new ArgumentNullException(nameof(variables));
        _unpack     = unpack     ?? throw new ArgumentNullException(nameof(unpack));
        _evaluate   = evaluate   ?? throw new ArgumentNullException(nameof(evaluate));
    }

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        if (vector.Length != _variables.Count)
            throw new ArgumentException(
                $"Vector length {vector.Length} does not match DimensionCount {_variables.Count}.",
                nameof(vector));

        ct.ThrowIfCancellationRequested();

        // Materialise the span — engine Evaluate APIs take double[].
        // Keeping the span boundary at IObjective prevents callers from
        // paying the allocation on partial evaluations (e.g. pre-screen).
        var vec    = vector.ToArray();
        var design = _unpack(vec, _baseline);
        var result = _engine.Evaluate(design, _conditions);
        return _evaluate(result);
    }
}
