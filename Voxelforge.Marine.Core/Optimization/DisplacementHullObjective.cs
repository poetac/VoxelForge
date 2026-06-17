// DisplacementHullObjective.cs — IObjective for displacement hull AUV optimisation.
//
// Seven-dimensional search vector that SA / CMA-ES / NSGA-II consume without
// seeing any marine-specific types. Score = DragForce_N at cruise speed;
// infeasible candidates (any hard gate violation) return +∞.
//
// Vector layout (7 dims):
//   0  Length_m                0.5 – 5.0 m
//   1  Diameter_m              0.05 – 1.0 m
//   2  NoseFairingFraction     0.10 – 0.35 [-]
//   3  TailFairingFraction     0.15 – 0.40 [-]
//   4  WallThickness_m         0.002 – 0.020 m
//   5  MaterialIndex           0.0 – 2.0  (rounded to int: 0=Ti, 1=Al, 2=SS)
//   6  DepthRating_m           10 – 500 m
//
// MaterialIndex encodes a categorical choice in a continuous SA slot — same
// pattern as the rocket optimizer's categorical dims (ElementType, WallMaterial).
// The slot is sampled as a float in [0, 2]; Unpack rounds to the nearest integer
// before building the MarineDesign record, so round-trips are bit-identical for
// feasible designs.
//
// References:
//   Plan: docs/pillar-specs/marine-displacement.md §Design variables
//   Pattern: Voxelforge.Airbreathing.Core/Optimization/RamjetObjective.cs

using System;
using System.Collections.Generic;
using System.Threading;
using Voxelforge.Optimization;

namespace Voxelforge.Marine.Optimization;

/// <summary>
/// IObjective adapter for displacement hull AUV design. Minimises hull drag at
/// cruise speed; infeasible candidates return <see cref="double.PositiveInfinity"/>.
/// Thread-safe and deterministic per the IObjective contract.
/// </summary>
public sealed class DisplacementHullObjective : IObjective
{
    private readonly MarineConditions _conditions;
    private readonly DesignVariableInfo[] _variables;
    private readonly HullFamily _hullFamily;

    /// <summary>
    /// Construct with fixed operating conditions, per-dimension bounds, and hull family.
    /// Use <see cref="WithDefaultBounds"/> for sensible AUV-range defaults.
    /// </summary>
    public DisplacementHullObjective(
        MarineConditions conditions,
        IReadOnlyList<DesignVariableInfo> variables,
        HullFamily hullFamily = HullFamily.Myring)
    {
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        if (variables is null) throw new ArgumentNullException(nameof(variables));
        if (variables.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"DisplacementHull objective requires {DefaultVariableNames.Length} variables; got {variables.Count}.",
                nameof(variables));
        _variables = new DesignVariableInfo[variables.Count];
        for (int i = 0; i < variables.Count; i++) _variables[i] = variables[i];
        _hullFamily = hullFamily;
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
            design = Unpack(vector, _hullFamily);
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

    /// <summary>
    /// Names of the seven design vector slots. Order is load-bearing —
    /// <see cref="Pack"/> and <see cref="Unpack"/> depend on it.
    /// </summary>
    public static readonly string[] DefaultVariableNames =
    {
        "Length_m",
        "Diameter_m",
        "NoseFairingFraction",
        "TailFairingFraction",
        "WallThickness_m",
        "MaterialIndex",
        "DepthRating_m",
    };

    /// <summary>
    /// Default bounds spanning the AUV design range (0.5–5 m hull length).
    /// </summary>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("Length_m",              0.5,   5.0),
        new("Diameter_m",            0.05,  1.0),
        new("NoseFairingFraction",   0.10,  0.35),
        new("TailFairingFraction",   0.15,  0.40),
        new("WallThickness_m",       0.002, 0.020),
        new("MaterialIndex",         0.0,   2.0),
        new("DepthRating_m",        10.0,  500.0),
    };

    /// <summary>
    /// Convenience factory: default bounds + supplied operating conditions + optional hull family.
    /// </summary>
    public static DisplacementHullObjective WithDefaultBounds(
        MarineConditions conditions,
        HullFamily hullFamily = HullFamily.Myring)
        => new(conditions, DefaultBounds, hullFamily);

    /// <summary>
    /// Pack a <see cref="MarineDesign"/> record into the seven-element SA vector.
    /// Inverse of <see cref="Unpack"/>.
    /// </summary>
    public static double[] Pack(MarineDesign design)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        return new double[]
        {
            design.Length_m,
            design.Diameter_m,
            design.NoseFairingFraction,
            design.TailFairingFraction,
            design.WallThickness_m,
            (double)design.MaterialIndex,
            design.DepthRating_m,
        };
    }

    /// <summary>
    /// Inflate an SA vector to a <see cref="MarineDesign"/> record (Myring family).
    /// Kind is fixed to <see cref="MarineKind.AuvMidBody"/>.
    /// MaterialIndex is rounded to the nearest integer (categorical slot).
    /// </summary>
    public static MarineDesign Unpack(ReadOnlySpan<double> vector)
        => Unpack(vector, HullFamily.Myring);

    /// <summary>
    /// Inflate an SA vector to a <see cref="MarineDesign"/> record with the given hull family.
    /// </summary>
    public static MarineDesign Unpack(ReadOnlySpan<double> vector, HullFamily hullFamily)
    {
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Marine vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));

        int matIdx = (int)Math.Round(Math.Clamp(vector[5], 0.0, 2.0));

        return new MarineDesign(
            Kind:                MarineKind.AuvMidBody,
            Length_m:            vector[0],
            Diameter_m:          vector[1],
            NoseFairingFraction: vector[2],
            TailFairingFraction: vector[3],
            WallThickness_m:     vector[4],
            MaterialIndex:       matIdx,
            DepthRating_m:       vector[6],
            HullFamily:          hullFamily);
    }
}
