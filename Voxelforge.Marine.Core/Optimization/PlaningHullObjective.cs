// PlaningHullObjective.cs — IObjective for SurfaceHull (planing) optimisation.
//
// Sprint M.W3 sister to DisplacementHullObjective for the planing variant.
// Five-dimensional search vector that SA / CMA-ES / NSGA-II consume without
// seeing any marine-specific types. Score = TotalResistance_N at cruise speed;
// infeasible candidates (any hard gate violation) return +∞.
//
// Vector layout (5 dims):
//   0  Length_m                   5.0 – 25.0 m   (LWL)
//   1  BeamMidship_m              1.5 – 6.0 m
//   2  DeadriseAngle_deg          5.0 – 25.0 °
//   3  MassDisplacement_kg      500   – 50 000 kg
//   4  LongitudinalCgFraction     0.40 – 0.60 [-]
//
// Note: FreeboardHeight_m is held at a sensible default (0.6 m) outside the SA
// loop because it doesn't enter the Savitsky resistance fit at this fidelity
// — only the gates (e.g. seakeeping) consume it. Adding it as a 6th SA dim
// is straightforward when a freeboard-dependent gate becomes binding.

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;

namespace Voxelforge.Marine.Optimization;

/// <summary>
/// IObjective adapter for SurfaceHull (planing) design. Minimises total
/// hull resistance at cruise speed; infeasible candidates return
/// <see cref="double.PositiveInfinity"/>. Thread-safe and deterministic per
/// the IObjective contract.
/// </summary>
public sealed class PlaningHullObjective : IObjective
{
    private readonly MarineConditions _conditions;
    private readonly DesignVariableInfo[] _variables;
    private readonly double _freeboardHeight_m;

    /// <summary>
    /// Construct with fixed operating conditions, per-dimension bounds, and
    /// freeboard height (held outside the SA loop).
    /// </summary>
    public PlaningHullObjective(
        MarineConditions conditions,
        IReadOnlyList<DesignVariableInfo> variables,
        double freeboardHeight_m = 0.6)
    {
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        if (variables is null) throw new ArgumentNullException(nameof(variables));
        if (variables.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"PlaningHull objective requires {DefaultVariableNames.Length} variables; got {variables.Count}.",
                nameof(variables));
        if (freeboardHeight_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(freeboardHeight_m),
                $"freeboardHeight_m must be positive; got {freeboardHeight_m}.");
        _variables = new DesignVariableInfo[variables.Count];
        for (int i = 0; i < variables.Count; i++) _variables[i] = variables[i];
        _freeboardHeight_m = freeboardHeight_m;
    }

    /// <inheritdoc />
    public int DimensionCount => _variables.Length;

    /// <inheritdoc />
    public IReadOnlyList<DesignVariableInfo> Variables => _variables;

    /// <inheritdoc />
    public EvaluationResult Evaluate(ReadOnlySpan<double> vector, CancellationToken ct = default)
    {
        if (vector.Length != _variables.Length)
            throw new ArgumentException(
                $"Vector length {vector.Length} does not match dimension count {_variables.Length}.",
                nameof(vector));

        MarineDesign design;
        try
        {
            design = Unpack(vector, _freeboardHeight_m);
            design.ValidateSelf();
        }
        catch (ArgumentException ex)
        {
            return new EvaluationResult(
                Score:                   double.PositiveInfinity,
                Violations:              new[] { new FeasibilityViolation("MARINE_DESIGN_INVALID", ex.Message, double.NaN, double.NaN) },
                EngineSpecificBreakdown: null);
        }

        MarineResult result;
        try
        {
            result = MarineOptimization.GenerateWith(design, _conditions);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new EvaluationResult(
                Score:                   double.PositiveInfinity,
                Violations:              new[] { new FeasibilityViolation("MARINE_EVAL_EXCEPTION", ex.Message, double.NaN, double.NaN) },
                EngineSpecificBreakdown: null);
        }

        double score = result.IsFeasible
            ? result.DragForce_N
            : double.PositiveInfinity;

        return new EvaluationResult(
            Score:                   score,
            Violations:              result.Violations,
            EngineSpecificBreakdown: result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Static helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Names of the five planing design vector slots. Order is load-bearing.</summary>
    public static readonly string[] DefaultVariableNames =
    {
        "Length_m",
        "BeamMidship_m",
        "DeadriseAngle_deg",
        "MassDisplacement_kg",
        "LongitudinalCgFraction",
    };

    /// <summary>
    /// Default bounds spanning the recreational planing-yacht cluster
    /// (5–25 m LWL, 1.5–6 m beam, 5–25° deadrise, 0.5–50 t displacement).
    /// </summary>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("Length_m",                  5.0,    25.0),
        new("BeamMidship_m",             1.5,     6.0),
        new("DeadriseAngle_deg",         5.0,    25.0),
        new("MassDisplacement_kg",     500.0, 50000.0),
        new("LongitudinalCgFraction",    0.40,    0.60),
    };

    /// <summary>
    /// Convenience factory: default bounds + supplied conditions + 0.6 m
    /// default freeboard.
    /// </summary>
    public static PlaningHullObjective WithDefaultBounds(MarineConditions conditions)
        => new(conditions, DefaultBounds);

    /// <summary>
    /// Pack a planing <see cref="MarineDesign"/> into the 5-element SA vector.
    /// </summary>
    public static double[] Pack(MarineDesign design)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        return new double[]
        {
            design.Length_m,
            design.BeamMidship_m,
            design.DeadriseAngle_deg,
            design.MassDisplacement_kg,
            design.LongitudinalCgFraction,
        };
    }

    /// <summary>
    /// Inflate an SA vector to a planing <see cref="MarineDesign"/>. Kind is
    /// fixed to <see cref="MarineKind.SurfaceHull"/>; HullFamily to
    /// <see cref="HullFamily.Planing"/>. AUV-positional fields take placeholder
    /// values that pass <see cref="MarineDesign.ValidateSelf"/>'s planing branch
    /// (which ignores them).
    /// </summary>
    public static MarineDesign Unpack(ReadOnlySpan<double> vector, double freeboardHeight_m = 0.6)
    {
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Planing vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));

        return new MarineDesign(
            Kind:                MarineKind.SurfaceHull,
            Length_m:            vector[0],
            // The five AUV-positional fields below are ignored by the planing
            // ValidateSelf branch; they sit at sentinel values rather than
            // NaN because the positional ctor demands non-default doubles
            // for round-tripped JSON to work.
            Diameter_m:          1.0,
            NoseFairingFraction: 0.25,
            TailFairingFraction: 0.25,
            WallThickness_m:     0.005,
            MaterialIndex:       0,
            DepthRating_m:       1.0,
            HullFamily:          HullFamily.Planing)
        {
            BeamMidship_m          = vector[1],
            DeadriseAngle_deg      = vector[2],
            MassDisplacement_kg    = vector[3],
            FreeboardHeight_m      = freeboardHeight_m,
            LongitudinalCgFraction = vector[4],
        };
    }
}
