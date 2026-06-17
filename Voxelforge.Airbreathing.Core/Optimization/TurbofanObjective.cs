// TurbofanObjective.cs — IObjective adapter for the turbofan cycle
// solver. Sibling to TurbojetObjective; same engine-family-agnostic
// optimizer surface (the IObjective decoupling).
//
// Sprint 0 / Wave 1 (2026-05-05): per-iteration evaluation routes
// through `EngineObjectiveAdapter` over `AirbreathingEngine.Instance`.
// See RamjetObjective for the canonical migration pattern.
//
// Vector layout (Sprint A8 turbofan, 8 dims):
//   0  InletThroatArea_m2
//   1  CombustorArea_m2
//   2  CombustorLength_m
//   3  NozzleThroatArea_m2
//   4  NozzleExitArea_m2
//   5  EquivalenceRatio
//   6  CompressorPressureRatio
//   7  BypassRatio                (turbofan-specific)
//
// π_fan is derived from π_c via TurbofanCycleSolver.DefaultFanPressureRatio
// rather than added as its own SA dim — keeps the slot count at 8 and
// keeps the optimizer focused on knobs the user is genuinely free to
// tune. Promoting π_fan to a 9th SA dim is a Stream B follow-on that
// can ship without touching the Pack/Unpack contract here (the new
// slot would extend the array, leaving slots 0-7 stable).

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Airbreathing.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Optimization;

/// <summary>
/// IObjective adapter for the turbofan cycle solver. Threads through
/// to <see cref="AirbreathingEngine.Instance"/> for
/// <see cref="AirbreathingEngineKind.Turbofan"/> designs.
/// </summary>
public sealed class TurbofanObjective : IObjective
{
    private readonly EngineObjectiveAdapter<AirbreathingEngineDesign, FlightConditions, AirbreathingResult> _inner;

    /// <summary>
    /// Construct an objective that optimises a turbofan design at the
    /// supplied flight conditions, with custom bounds per design
    /// variable.
    /// </summary>
    public TurbofanObjective(
        FlightConditions conditions,
        IReadOnlyList<DesignVariableInfo> variables)
    {
        if (conditions is null) throw new ArgumentNullException(nameof(conditions));
        if (variables is null) throw new ArgumentNullException(nameof(variables));
        if (variables.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Turbofan design vector requires exactly {DefaultVariableNames.Length} variables; got {variables.Count}.",
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
                Violations:               new[] { new FeasibilityViolation("TURBOFAN_EVAL_EXCEPTION", ex.Message, double.NaN, double.NaN) },
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
    /// Names of the eight turbofan design vector slots. Order is
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
        "BypassRatio",
    };

    /// <summary>
    /// Default bounds spanning the F404-class low-bypass turbofan
    /// envelope. EquivalenceRatio band matches turbojet (turbine-blade-
    /// temp-limited). BypassRatio band matches the
    /// <c>BYPASS_RATIO_OUT_OF_BAND</c> gate's [0.10, 2.00] envelope —
    /// the single-spool model's validity range.
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
        new("BypassRatio",               0.10, 2.00),
    };

    /// <summary>
    /// Convenience factory.
    /// </summary>
    public static TurbofanObjective WithDefaultBounds(FlightConditions conditions)
        => new(conditions, DefaultBounds);

    /// <summary>
    /// Project a turbofan design record into the SA vector layout.
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
            design.BypassRatio,
        };
    }

    /// <summary>
    /// Inflate a turbofan SA vector back to a design record. Kind is
    /// fixed to <see cref="AirbreathingEngineKind.Turbofan"/>.
    /// </summary>
    public static AirbreathingEngineDesign Unpack(ReadOnlySpan<double> vector)
    {
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Turbofan vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));
        return UnpackImpl(vector);
    }

    private static AirbreathingEngineDesign UnpackImpl(ReadOnlySpan<double> vector)
        => new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:      vector[0],
            CombustorArea_m2:        vector[1],
            CombustorLength_m:       vector[2],
            NozzleThroatArea_m2:     vector[3],
            NozzleExitArea_m2:       vector[4],
            EquivalenceRatio:        vector[5],
            CompressorPressureRatio: vector[6],
            BypassRatio:             vector[7]);
}
