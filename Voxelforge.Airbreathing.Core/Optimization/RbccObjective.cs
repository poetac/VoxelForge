// RbccObjective.cs — IObjective adapter for the RBCC cycle solver.
// Sprint A11 (sub-step 1e). Sibling to RamjetObjective, TurbojetObjective,
// ScramjetObjective; same engine-family-agnostic optimizer surface
// (the IObjective decoupling).
//
// Sprint 0 / Wave 1 (2026-05-05): per-iteration evaluation routes
// through `EngineObjectiveAdapter` over `AirbreathingEngine.Instance`.
// See RamjetObjective for the canonical migration pattern. The adapter
// must capture this objective's `_mode` field, so its `unpack` lambda
// is non-static (single allocation captured at ctor time, not per-call).
//
// Vector layout (8 dims):
//   0  InletThroatArea_m2
//   1  CombustorArea_m2
//   2  CombustorLength_m
//   3  NozzleThroatArea_m2
//   4  NozzleExitArea_m2
//   5  EquivalenceRatio
//   6  IsolatorLength_m          (scramjet-mode knob; ignored in ramjet/ducted-rocket)
//   7  EjectorEntrainmentRatio   (ducted-rocket-mode knob; ignored in ramjet/scramjet)
//
// RbccMode is fixed at construction time — it is a flight-envelope design
// choice, not an SA variable. The optimizer searches within one mode at a
// time. Cross-mode sizing is a Stream B follow-on.

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Airbreathing.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Optimization;

/// <summary>
/// IObjective adapter for the RBCC cycle solver. Threads through to
/// <see cref="AirbreathingEngine.Instance"/> for
/// <see cref="AirbreathingEngineKind.Rbcc"/> designs.
/// </summary>
public sealed class RbccObjective : IObjective
{
    private readonly EngineObjectiveAdapter<AirbreathingEngineDesign, FlightConditions, AirbreathingResult> _inner;

    /// <summary>
    /// Construct an RBCC objective at the supplied flight conditions
    /// and operating mode, with custom bounds per design variable.
    /// </summary>
    public RbccObjective(
        FlightConditions conditions,
        RbccOperatingMode mode,
        IReadOnlyList<DesignVariableInfo> variables)
    {
        if (conditions is null) throw new ArgumentNullException(nameof(conditions));
        if (variables is null) throw new ArgumentNullException(nameof(variables));
        if (variables.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"RBCC design vector requires exactly {DefaultVariableNames.Length} variables; got {variables.Count}.",
                nameof(variables));
        var capturedMode = mode; // closure capture (one-time at ctor)
        var baseline = UnpackImpl(stackalloc double[DefaultVariableNames.Length], capturedMode);
        _inner = new EngineObjectiveAdapter<AirbreathingEngineDesign, FlightConditions, AirbreathingResult>(
            engine:     AirbreathingEngine.Instance,
            conditions: conditions,
            baseline:   baseline,
            variables:  variables,
            unpack:     (vec, _) => UnpackImpl(vec, capturedMode),
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
            return new EvaluationResult(
                Score:                   double.PositiveInfinity,
                Violations:              new[] { new FeasibilityViolation("RBCC_EVAL_EXCEPTION", ex.Message, double.NaN, double.NaN) },
                EngineSpecificBreakdown: null);
        }
    }

    private static EvaluationResult ScoreFromResult(AirbreathingResult result)
    {
        double score = result.IsFeasible
            ? -result.Stations.SpecificImpulse_s
            : double.PositiveInfinity;
        return new EvaluationResult(
            Score:                   score,
            Violations:              result.Violations,
            EngineSpecificBreakdown: result);
    }

    /// <summary>
    /// Names of the eight RBCC design vector slots. Order is load-bearing
    /// for <see cref="Pack"/> and <see cref="Unpack"/>.
    /// </summary>
    public static readonly string[] DefaultVariableNames =
    {
        "InletThroatArea_m2",
        "CombustorArea_m2",
        "CombustorLength_m",
        "NozzleThroatArea_m2",
        "NozzleExitArea_m2",
        "EquivalenceRatio",
        "IsolatorLength_m",
        "EjectorEntrainmentRatio",
    };

    /// <summary>
    /// Default bounds spanning the RBCC design envelope across all three
    /// operating modes. Dim 6 (IsolatorLength) is only active in scramjet
    /// mode; dim 7 (EjectorEntrainmentRatio) is only active in ducted-
    /// rocket mode. Bounds are chosen wide enough that SA can explore both
    /// usefully and symmetrically.
    /// </summary>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("InletThroatArea_m2",       0.05, 0.50),
        new("CombustorArea_m2",         0.10, 1.00),
        new("CombustorLength_m",        0.20, 2.50),
        new("NozzleThroatArea_m2",      0.05, 0.50),
        new("NozzleExitArea_m2",        0.20, 2.00),
        new("EquivalenceRatio",         0.30, 1.20),
        new("IsolatorLength_m",         0.30, 2.50),
        new("EjectorEntrainmentRatio",  0.50, 3.00),
    };

    /// <summary>
    /// Convenience factory. Creates an RBCC objective at the supplied
    /// flight conditions and operating mode with the default bounds.
    /// </summary>
    public static RbccObjective WithDefaultBounds(
        FlightConditions conditions,
        RbccOperatingMode mode = RbccOperatingMode.Ramjet)
        => new(conditions, mode, DefaultBounds);

    /// <summary>
    /// Project an RBCC design record into the SA vector layout.
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
            design.IsolatorLength_m,
            design.EjectorEntrainmentRatio,
        };
    }

    /// <summary>
    /// Inflate an RBCC SA vector back to a design record. Kind is fixed
    /// to <see cref="AirbreathingEngineKind.Rbcc"/>; <paramref name="mode"/>
    /// is stamped onto <see cref="AirbreathingEngineDesign.RbccMode"/>.
    /// </summary>
    public static AirbreathingEngineDesign Unpack(
        ReadOnlySpan<double> vector,
        RbccOperatingMode mode = RbccOperatingMode.Ramjet)
    {
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"RBCC vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));
        return UnpackImpl(vector, mode);
    }

    private static AirbreathingEngineDesign UnpackImpl(ReadOnlySpan<double> vector, RbccOperatingMode mode)
        => new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Rbcc,
            InletThroatArea_m2:      vector[0],
            CombustorArea_m2:        vector[1],
            CombustorLength_m:       vector[2],
            NozzleThroatArea_m2:     vector[3],
            NozzleExitArea_m2:       vector[4],
            EquivalenceRatio:        vector[5],
            CompressorPressureRatio: 1.0,
            BypassRatio:             0.0,
            IsolatorLength_m:        vector[6],
            RbccMode:                mode,
            EjectorEntrainmentRatio: vector[7]);
}
