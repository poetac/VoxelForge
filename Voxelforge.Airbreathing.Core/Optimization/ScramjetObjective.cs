// ScramjetObjective.cs — IObjective adapter for the scramjet cycle
// solver. Sibling to RamjetObjective and TurbojetObjective; same
// engine-family-agnostic optimizer surface (architecture-greenfield-
// memo.md rec #4).
//
// Vector layout (Sprint A10 scramjet, 7 dims):
//   0  InletThroatArea_m2
//   1  CombustorArea_m2
//   2  CombustorLength_m
//   3  NozzleThroatArea_m2
//   4  NozzleExitArea_m2
//   5  EquivalenceRatio
//   6  IsolatorLength_m         (scramjet-specific)

// Sprint 0 / Wave 1 (2026-05-05): per-iteration evaluation routes
// through `EngineObjectiveAdapter` over `AirbreathingEngine.Instance`.
// See RamjetObjective for the canonical migration pattern.

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Airbreathing.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Optimization;

/// <summary>
/// IObjective adapter for the scramjet cycle solver. Threads through
/// to <see cref="AirbreathingEngine.Instance"/> for
/// <see cref="AirbreathingEngineKind.Scramjet"/> designs.
/// </summary>
public sealed class ScramjetObjective : IObjective
{
    private readonly EngineObjectiveAdapter<AirbreathingEngineDesign, FlightConditions, AirbreathingResult> _inner;

    /// <summary>
    /// Construct an objective that optimises a scramjet design at the
    /// supplied flight conditions, with custom bounds per design variable.
    /// </summary>
    public ScramjetObjective(
        FlightConditions conditions,
        IReadOnlyList<DesignVariableInfo> variables)
    {
        if (conditions is null) throw new ArgumentNullException(nameof(conditions));
        if (variables is null) throw new ArgumentNullException(nameof(variables));
        if (variables.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Scramjet design vector requires exactly {DefaultVariableNames.Length} variables; got {variables.Count}.",
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
                Score:                   double.PositiveInfinity,
                Violations:              new[] { new FeasibilityViolation("SCRAMJET_EVAL_EXCEPTION", ex.Message, double.NaN, double.NaN) },
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
    /// Names of the seven scramjet design vector slots. Order is
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
        "IsolatorLength_m",
    };

    /// <summary>
    /// Default bounds spanning a hydrogen-fuelled scramjet design
    /// envelope at M ∈ [4, 12], altitude ∈ [20, 30] km. Lean-biased
    /// equivalence-ratio band (scramjets are residence-time limited;
    /// φ &gt; 1 risks incomplete combustion and duct heating).
    /// </summary>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("InletThroatArea_m2",  0.05, 0.50),
        new("CombustorArea_m2",    0.10, 1.00),
        new("CombustorLength_m",   0.20, 2.50),
        new("NozzleThroatArea_m2", 0.05, 0.50),
        new("NozzleExitArea_m2",   0.20, 2.00),
        new("EquivalenceRatio",    0.30, 1.00),
        new("IsolatorLength_m",    0.30, 2.50),
    };

    /// <summary>
    /// Convenience factory. Nominal design point M = 8, altitude 25 km,
    /// H2 fuel — Mattingly §17 reference flight condition.
    /// </summary>
    public static ScramjetObjective WithDefaultBounds(FlightConditions conditions)
        => new(conditions, DefaultBounds);

    /// <summary>
    /// Convenience factory with the nominal Mattingly §17 design point.
    /// </summary>
    public static ScramjetObjective AtNominalConditions()
        => WithDefaultBounds(new FlightConditions(
            Altitude_m:  25_000.0,
            MachNumber:  8.0,
            Fuel:        AirbreathingFuel.H2));

    /// <summary>
    /// Project a scramjet design record into the SA vector layout.
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
        };
    }

    /// <summary>
    /// Inflate a scramjet SA vector back to a design record. Kind is
    /// fixed to <see cref="AirbreathingEngineKind.Scramjet"/>.
    /// </summary>
    public static AirbreathingEngineDesign Unpack(ReadOnlySpan<double> vector)
    {
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Scramjet vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));
        return UnpackImpl(vector);
    }

    private static AirbreathingEngineDesign UnpackImpl(ReadOnlySpan<double> vector)
        => new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Scramjet,
            InletThroatArea_m2:      vector[0],
            CombustorArea_m2:        vector[1],
            CombustorLength_m:       vector[2],
            NozzleThroatArea_m2:     vector[3],
            NozzleExitArea_m2:       vector[4],
            EquivalenceRatio:        vector[5],
            CompressorPressureRatio: 1.0,
            IsolatorLength_m:        vector[6]);
}
