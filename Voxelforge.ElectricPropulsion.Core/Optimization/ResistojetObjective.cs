// ResistojetObjective.cs — IObjective adapter wiring the resistojet
// physics into the engine-family-agnostic SA / CMA-ES / NSGA-II
// optimizer surface.
//
// First IObjective in the codebase wired through `EngineObjectiveAdapter`
// from day one (the rocket-side RegenObjective + airbreathing-side
// RamjetObjective predate the adapter and still close over the
// engine directly; both will migrate when convenient). Per-pillar
// boilerplate is therefore minimal — just bounds + unpack + score
// projection.
//
// Vector layout (Wave-1, 6 dims, matches pillar spec §2):
//   0  HeaterPower_W
//   1  PropellantMassFlow_kgs
//   2  NozzleThroatRadius_mm
//   3  NozzleAreaRatio
//   4  HeaterChamberLength_mm
//   5  HeaterChamberRadius_mm
//
// Score: −Isp_vacuum on feasible solves; +∞ on infeasible (canonical
// IObjective infeasibility sentinel; SA's MultiChainOptimizer treats
// +∞ as "never accepted as new best").
//
// Bind-time clip per pillar spec §2 + ADR-026 §3: HeaterPower_W upper
// bound is dynamically clipped to min(3000, conditions.BusPower_W_avail)
// so the optimizer never wastes evaluations on candidates above the
// spacecraft's available bus power.

using System;
using System.Collections.Generic;
using Voxelforge.ElectricPropulsion.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.ElectricPropulsion.Optimization;

/// <summary>
/// Convenience factory for an <see cref="EngineObjectiveAdapter{TDesign,TConditions,TResult}"/>
/// over <see cref="ElectricPropulsionEngine"/>. Wave-1 covers the 6-dim
/// resistojet vector layout per pillar spec §2.
/// </summary>
public static class ResistojetObjective
{
    /// <summary>
    /// Names of the six resistojet design vector slots. Order is
    /// load-bearing — <see cref="Pack"/> + <see cref="Unpack"/> depend on it.
    /// </summary>
    public static readonly string[] DefaultVariableNames =
    {
        "HeaterPower_W",
        "PropellantMassFlow_kgs",
        "NozzleThroatRadius_mm",
        "NozzleAreaRatio",
        "HeaterChamberLength_mm",
        "HeaterChamberRadius_mm",
    };

    /// <summary>
    /// Default 6-dim bounds spanning the flown-resistojet cluster + headroom
    /// per pillar spec §2.
    /// </summary>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("HeaterPower_W",            200.0,    3000.0),
        new("PropellantMassFlow_kgs",     1.0e-5,    5.0e-4),
        new("NozzleThroatRadius_mm",      0.1,       2.0),
        new("NozzleAreaRatio",           25.0,     150.0),
        new("HeaterChamberLength_mm",     5.0,      50.0),
        new("HeaterChamberRadius_mm",     2.0,      15.0),
    };

    /// <summary>
    /// Build an <see cref="IObjective"/> over the resistojet engine + given
    /// conditions + baseline design. Bounds default to <see cref="DefaultBounds"/>
    /// with the bind-time bus-power clip applied to dim 0 (HeaterPower_W).
    /// </summary>
    /// <param name="conditions">Operating conditions (vacuum, propellant, bus power).</param>
    /// <param name="baseline">
    /// Baseline design — categorical state (HeaterMaterial, ChamberEmissivity,
    /// ChamberWallThickness_mm, RadiativelyCooledNozzle) the optimizer must
    /// preserve across iterations.
    /// </param>
    /// <param name="variables">Optional bound override; defaults to <see cref="DefaultBounds"/>.</param>
    public static IObjective Build(
        ResistojetConditions conditions,
        ElectricPropulsionEngineDesign baseline,
        IReadOnlyList<DesignVariableInfo>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(baseline);

        var vars = variables ?? ApplyBusPowerClip(DefaultBounds, conditions);
        if (vars.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Resistojet design vector requires exactly {DefaultVariableNames.Length} variables; got {vars.Count}.",
                nameof(variables));

        return new EngineObjectiveAdapter<ElectricPropulsionEngineDesign, ResistojetConditions, ElectricPropulsionResult>(
            engine:     ElectricPropulsionEngine.Instance,
            conditions: conditions,
            baseline:   baseline,
            variables:  vars,
            unpack:     Unpack,
            evaluate:   ScoreNegativeIsp);
    }

    /// <summary>
    /// Project an <see cref="ElectricPropulsionEngineDesign"/> into the SA
    /// vector layout. Inverse of <see cref="Unpack"/>.
    /// </summary>
    public static double[] Pack(ElectricPropulsionEngineDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return new[]
        {
            design.HeaterPower_W,
            design.PropellantMassFlow_kgs,
            design.NozzleThroatRadius_mm,
            design.NozzleAreaRatio,
            design.HeaterChamberLength_mm,
            design.HeaterChamberRadius_mm,
        };
    }

    /// <summary>
    /// Inflate an SA vector + baseline design into a concrete design record.
    /// The Kind, HeaterMaterial, ChamberEmissivity, ChamberWallThickness_mm,
    /// and RadiativelyCooledNozzle fields are preserved from
    /// <paramref name="baseline"/> per the categorical-state rule
    /// (RegenChamberDesign.Unpack pattern).
    /// </summary>
    public static ElectricPropulsionEngineDesign Unpack(
        double[] vector, ElectricPropulsionEngineDesign baseline)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(baseline);
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Resistojet vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));
        return baseline with
        {
            HeaterPower_W           = vector[0],
            PropellantMassFlow_kgs  = vector[1],
            NozzleThroatRadius_mm   = vector[2],
            NozzleAreaRatio         = vector[3],
            HeaterChamberLength_mm  = vector[4],
            HeaterChamberRadius_mm  = vector[5],
        };
    }

    /// <summary>
    /// Apply the bind-time bus-power clip to dim 0 (HeaterPower_W). Used by
    /// <see cref="Build"/> when the caller doesn't supply custom variables.
    /// </summary>
    private static DesignVariableInfo[] ApplyBusPowerClip(
        DesignVariableInfo[] defaults,
        ResistojetConditions conditions)
    {
        var clipped = new DesignVariableInfo[defaults.Length];
        Array.Copy(defaults, clipped, defaults.Length);
        // Dim 0 = HeaterPower_W. Cap upper at min(default, BusPower).
        if (conditions.BusPower_W_avail > 0)
        {
            double newMax = Math.Min(defaults[0].Max, conditions.BusPower_W_avail);
            // Guard against impossibly small bus power (default min is 200 W).
            double newMin = Math.Min(defaults[0].Min, newMax);
            clipped[0] = new DesignVariableInfo(defaults[0].Name, newMin, newMax);
        }
        return clipped;
    }

    /// <summary>
    /// Score projection: minimise −Isp_vacuum on feasible solves; +∞ on infeasible.
    /// </summary>
    private static EvaluationResult ScoreNegativeIsp(ElectricPropulsionResult result)
    {
        double score = result.IsFeasible
            ? -result.IspVacuum_s
            : double.PositiveInfinity;
        return new EvaluationResult(
            Score:                   score,
            Violations:              result.Violations,
            EngineSpecificBreakdown: result);
    }
}
