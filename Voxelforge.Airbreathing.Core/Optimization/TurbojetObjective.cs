// TurbojetObjective.cs — IObjective adapter for the turbojet cycle
// solver. Sibling to RamjetObjective; same engine-family-agnostic
// optimizer surface (the IObjective decoupling).
//
// Sprint 0 / Wave 1 (2026-05-05): per-iteration evaluation routes
// through `EngineObjectiveAdapter` over `AirbreathingEngine.Instance`.
// See RamjetObjective for the canonical migration pattern.
//
// Vector layout (Sprint A7 turbojet, 7 dims):
//   0  InletThroatArea_m2
//   1  CombustorArea_m2
//   2  CombustorLength_m
//   3  NozzleThroatArea_m2
//   4  NozzleExitArea_m2
//   5  EquivalenceRatio
//   6  CompressorPressureRatio   (turbojet-specific)

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Airbreathing.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Optimization;

/// <summary>
/// IObjective adapter for the turbojet cycle solver. Threads through
/// to <see cref="AirbreathingEngine.Instance"/> for
/// <see cref="AirbreathingEngineKind.Turbojet"/> designs.
/// </summary>
public sealed class TurbojetObjective : IObjective
{
    private readonly EngineObjectiveAdapter<AirbreathingEngineDesign, FlightConditions, AirbreathingResult> _inner;

    /// <summary>
    /// Construct an objective that optimises a turbojet design at the
    /// supplied flight conditions, with custom bounds per design
    /// variable.
    /// </summary>
    public TurbojetObjective(
        FlightConditions conditions,
        IReadOnlyList<DesignVariableInfo> variables)
    {
        if (conditions is null) throw new ArgumentNullException(nameof(conditions));
        if (variables is null) throw new ArgumentNullException(nameof(variables));
        if (variables.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Turbojet design vector requires exactly {DefaultVariableNames.Length} variables; got {variables.Count}.",
                nameof(variables));
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
            return new EvaluationResult(
                Score:                    double.PositiveInfinity,
                Violations:               new[] { new FeasibilityViolation("TURBOJET_EVAL_EXCEPTION", ex.Message, double.NaN, double.NaN) },
                EngineSpecificBreakdown:  null);
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
    /// Names of the seven turbojet design vector slots. Order is
    /// load-bearing for <see cref="Pack"/> + <see cref="Unpack"/>.
    /// </summary>
    public static readonly string[] DefaultVariableNames =
    {
        "InletThroatArea_m2",
        "CombustorArea_m2",
        "CombustorLength_m",
        "NozzleThroatArea_m2",
        "NozzleExitArea_m2",
        "EquivalenceRatio",
        "CompressorPressureRatio",
    };

    /// <summary>
    /// Default bounds spanning the J85-class turbojet sea-level
    /// envelope. Tighter equivalence-ratio band than ramjet (turbojets
    /// are turbine-blade-temp-limited, so φ stays lean ~0.10-0.40).
    /// </summary>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("InletThroatArea_m2",        0.05, 0.50),
        new("CombustorArea_m2",          0.05, 0.30),
        new("CombustorLength_m",         0.15, 1.00),
        new("NozzleThroatArea_m2",       0.02, 0.20),
        new("NozzleExitArea_m2",         0.05, 0.30),
        new("EquivalenceRatio",          0.10, 0.40),
        new("CompressorPressureRatio",   3.00, 30.00),
    };

    /// <summary>
    /// Convenience factory.
    /// </summary>
    public static TurbojetObjective WithDefaultBounds(FlightConditions conditions)
        => new(conditions, DefaultBounds);

    /// <summary>
    /// Project a turbojet design record into the SA vector layout.
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
            design.CompressorPressureRatio,
        };
    }

    /// <summary>
    /// Inflate a turbojet SA vector back to a design record. Kind
    /// is fixed to <see cref="AirbreathingEngineKind.Turbojet"/>.
    /// </summary>
    public static AirbreathingEngineDesign Unpack(ReadOnlySpan<double> vector)
    {
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Turbojet vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));
        return UnpackImpl(vector);
    }

    private static AirbreathingEngineDesign UnpackImpl(ReadOnlySpan<double> vector)
        => new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turbojet,
            InletThroatArea_m2:      vector[0],
            CombustorArea_m2:        vector[1],
            CombustorLength_m:       vector[2],
            NozzleThroatArea_m2:     vector[3],
            NozzleExitArea_m2:       vector[4],
            EquivalenceRatio:        vector[5],
            CompressorPressureRatio: vector[6]);
}
