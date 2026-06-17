// RamjetObjective.cs — IObjective adapter wiring the air-breathing
// pillar's ramjet cycle solver into the engine-family-agnostic SA /
// CMA-ES / NSGA-II optimizer surface (Voxelforge.Optimization
// namespace, shared with the rocket pillar).
//
// Per the `IObjective` decoupling design (shipped 2026-04-28 via
// PR #155): each engine family ships an
// IObjective; the optimizer never sees a kind-shaped record. This
// objective is the air-breathing pillar's first such adapter.
//
// Sprint 0 / Wave 1 (2026-05-05): the per-iteration evaluation now
// goes through `EngineObjectiveAdapter` over `AirbreathingEngine.Instance`
// rather than calling `AirbreathingOptimization.GenerateWith` directly.
// The adapter takes care of the (vector → unpack → engine.Evaluate →
// score) pipeline; this wrapper keeps the public surface
// (DefaultVariableNames + DefaultBounds + WithDefaultBounds + Pack /
// Unpack) plus the exception-to-infeasible mapping that wraps NaN-y
// designs into +∞ infeasible results rather than crashing the optimizer.
//
// Vector layout (Sprint A5 ramjet, 6 dims):
//   0  InletThroatArea_m2
//   1  CombustorArea_m2
//   2  CombustorLength_m
//   3  NozzleThroatArea_m2
//   4  NozzleExitArea_m2
//   5  EquivalenceRatio
//
// Bounds are user-supplied (constructor); a default factory
// `WithDefaultBounds` ships a sensible range for the Mattingly
// synthetic ramjet flight envelope.
//
// Score = −SpecificImpulse_s on feasible solves; +∞ on infeasible
// (the canonical IObjective infeasibility sentinel SA recognises via
// `MultiChainOptimizer`'s strict-determinism contract).

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Airbreathing.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Optimization;

/// <summary>
/// IObjective adapter for the ramjet cycle solver. Threads through
/// to <see cref="AirbreathingEngine.Instance"/> via
/// <see cref="EngineObjectiveAdapter{TDesign,TConditions,TResult}"/>
/// and produces a scalar score the engine-family-agnostic optimizers
/// (SA / CMA-ES / NSGA-II) consume.
/// </summary>
public sealed class RamjetObjective : IObjective
{
    private readonly EngineObjectiveAdapter<AirbreathingEngineDesign, FlightConditions, AirbreathingResult> _inner;

    /// <summary>
    /// Construct an objective that optimises a ramjet design at the
    /// supplied flight conditions, with custom bounds per design
    /// variable.
    /// </summary>
    /// <param name="conditions">Flight envelope the design is sized at.</param>
    /// <param name="variables">
    /// Length-6 array describing the ramjet design vector slots.
    /// Names should match <see cref="DefaultVariableNames"/>; bounds
    /// are user-supplied. Use <see cref="WithDefaultBounds"/> for a
    /// sensible Mattingly-flight-envelope starting set.
    /// </param>
    public RamjetObjective(
        FlightConditions conditions,
        IReadOnlyList<DesignVariableInfo> variables)
    {
        if (conditions is null) throw new ArgumentNullException(nameof(conditions));
        if (variables is null) throw new ArgumentNullException(nameof(variables));
        if (variables.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Ramjet design vector requires exactly {DefaultVariableNames.Length} variables; got {variables.Count}.",
                nameof(variables));
        // The vector layout is self-contained for ramjets — there is no
        // baseline categorical state to preserve across Unpack calls
        // (Kind is hard-coded to Ramjet in UnpackImpl). We still pass a
        // baseline to satisfy EngineObjectiveAdapter's not-null guard.
        var baseline = UnpackImpl(stackalloc double[DefaultVariableNames.Length]);
        _inner = new EngineObjectiveAdapter<AirbreathingEngineDesign, FlightConditions, AirbreathingResult>(
            engine:     AirbreathingEngine.Instance,
            conditions: conditions,
            baseline:   baseline,
            variables:  variables,
            unpack:     static (vec, _) => UnpackImpl(vec),
            evaluate:   ScoreFromResult);
    }

    /// <inheritdoc />
    public int DimensionCount => _inner.DimensionCount;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _inner.Variables;

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        try
        {
            return _inner.Evaluate(vector, ct);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException
                                      or NotSupportedException
                                      or InvalidOperationException)
        {
            // Out-of-band design (e.g. φ = 0 produces zero combustion
            // and may NaN out downstream). Treat as +∞ infeasible
            // rather than crashing the optimizer.
            return new EvaluationResult(
                Score:                    double.PositiveInfinity,
                Violations:               new[] { new FeasibilityViolation("RAMJET_EVAL_EXCEPTION", ex.Message, double.NaN, double.NaN) },
                EngineSpecificBreakdown:  null);
        }
    }

    private static EvaluationResult ScoreFromResult(AirbreathingResult result)
    {
        // Score: maximize Isp ⇒ minimize −Isp. Infeasibility ⇒ +∞.
        double score = result.IsFeasible
            ? -result.Stations.SpecificImpulse_s
            : double.PositiveInfinity;
        return new EvaluationResult(
            Score:                   score,
            Violations:              result.Violations,
            EngineSpecificBreakdown: result);
    }

    /// <summary>
    /// Names of the six ramjet design vector slots. Order is
    /// load-bearing — <see cref="Pack"/> + <see cref="Unpack"/>
    /// depend on it.
    /// </summary>
    public static readonly string[] DefaultVariableNames =
    {
        "InletThroatArea_m2",
        "CombustorArea_m2",
        "CombustorLength_m",
        "NozzleThroatArea_m2",
        "NozzleExitArea_m2",
        "EquivalenceRatio",
    };

    /// <summary>
    /// Default bounds spanning the Mattingly synthetic ramjet flight
    /// envelope. Wide enough to reach feasibility for M=2 / 12 km
    /// designs without being so wide that SA wastes evaluations on
    /// pathological geometries.
    /// </summary>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("InletThroatArea_m2",   0.05, 0.50),
        new("CombustorArea_m2",     0.10, 1.00),
        new("CombustorLength_m",    0.20, 2.00),
        new("NozzleThroatArea_m2",  0.02, 0.30),
        new("NozzleExitArea_m2",    0.05, 1.00),
        new("EquivalenceRatio",     0.20, 1.50),
    };

    /// <summary>
    /// Convenience factory: build a RamjetObjective with the default
    /// six-dimensional bounds.
    /// </summary>
    public static RamjetObjective WithDefaultBounds(FlightConditions conditions)
        => new(conditions, DefaultBounds);

    /// <summary>
    /// Project a design record into the SA vector layout. Inverse of
    /// <see cref="Unpack"/>.
    /// </summary>
    public static double[] Pack(AirbreathingEngineDesign design)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        return new[]
        {
            design.InletThroatArea_m2,
            design.CombustorArea_m2,
            design.CombustorLength_m,
            design.NozzleThroatArea_m2,
            design.NozzleExitArea_m2,
            design.EquivalenceRatio,
        };
    }

    /// <summary>
    /// Inflate an SA vector back to a design record. The Kind is
    /// fixed to <see cref="AirbreathingEngineKind.Ramjet"/> — the
    /// vector layout is ramjet-specific.
    /// </summary>
    public static AirbreathingEngineDesign Unpack(ReadOnlySpan<double> vector)
    {
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Ramjet vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));
        return UnpackImpl(vector);
    }

    // Internal Unpack with no length check — the EngineObjectiveAdapter
    // already validates vector length against DimensionCount before
    // invoking the unpack delegate.
    private static AirbreathingEngineDesign UnpackImpl(ReadOnlySpan<double> vector)
        => new AirbreathingEngineDesign(
            Kind:                AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:  vector[0],
            CombustorArea_m2:    vector[1],
            CombustorLength_m:   vector[2],
            NozzleThroatArea_m2: vector[3],
            NozzleExitArea_m2:   vector[4],
            EquivalenceRatio:    vector[5]);
}
