// MpdObjective.cs — IObjective adapter for the Wave-2 Magnetoplasmadynamic
// Thruster. Mirrors GitObjective shape so the SA / CMA-ES / NSGA-II
// portfolio reaches MPD via the same EngineObjectiveAdapter<,,>.
//
// Vector layout (Wave-2, 5 dims, Sprint EP.W2.MPD):
//   0  MpdArcCurrent_A          500    – 8000  A
//   1  PropellantMassFlow_kgs     1e-5 –    5e-4 kg/s   (Li or Ar feed)
//   2  MpdCathodeRadius_mm        3    –   25  mm
//   3  MpdAnodeRadius_mm         30    –  200  mm       (must exceed cathode)
//   4  MpdChamberLength_mm       30    –  250  mm
//
// Score: −Isp_vacuum on feasible solves; +∞ on infeasible (uniform with
// other EP variant objectives).
//
// Bind-time clip: V_arc × J_arc ≤ conditions.BusPower_W_avail. V_arc is a
// derived quantity that depends on geometry; the clip is conservative —
// it pins the upper-J edge using the maximum possible V_arc across the
// design-space (V_anode_drop + V_col · max(L)/min(r_a)).

using System;
using System.Collections.Generic;
using Voxelforge.ElectricPropulsion.Engines;
using Voxelforge.ElectricPropulsion.Solvers;
using Voxelforge.Optimization;

namespace Voxelforge.ElectricPropulsion.Optimization;

/// <summary>
/// Convenience factory for an <see cref="EngineObjectiveAdapter{TDesign,TConditions,TResult}"/>
/// over <see cref="ElectricPropulsionEngine"/> for the MPD variant.
/// </summary>
public static class MpdObjective
{
    /// <summary>
    /// Names of the five MPD design vector slots. Order is load-bearing.
    /// </summary>
    public static readonly string[] DefaultVariableNames =
    {
        "MpdArcCurrent_A",
        "PropellantMassFlow_kgs",
        "MpdCathodeRadius_mm",
        "MpdAnodeRadius_mm",
        "MpdChamberLength_mm",
    };

    /// <summary>
    /// Default 5-dim bounds spanning the LiLFA / NASA-Lewis SF-MPD cluster +
    /// headroom (Polk 1991 / Sovey 1990 envelope).
    /// </summary>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("MpdArcCurrent_A",         500.0,   8000.0),
        new("PropellantMassFlow_kgs",    1e-5,    5e-4),
        new("MpdCathodeRadius_mm",       3.0,    25.0),
        new("MpdAnodeRadius_mm",        30.0,   200.0),
        new("MpdChamberLength_mm",      30.0,   250.0),
    };

    /// <summary>
    /// Build an <see cref="IObjective"/> over the MPD pipeline + given
    /// conditions + baseline design. Bounds default to <see cref="DefaultBounds"/>
    /// with the bind-time bus-power clip applied to dim 0 (MpdArcCurrent_A).
    /// </summary>
    public static IObjective Build(
        ResistojetConditions conditions,
        ElectricPropulsionEngineDesign baseline,
        IReadOnlyList<DesignVariableInfo>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.Kind != ElectricPropulsionEngineKind.MagnetoPlasmaDynamic)
            throw new ArgumentException(
                $"MpdObjective.Build requires baseline.Kind=MagnetoPlasmaDynamic; got {baseline.Kind}.",
                nameof(baseline));

        var vars = variables ?? ApplyBusPowerClip(DefaultBounds, conditions);
        if (vars.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"MPD design vector requires exactly {DefaultVariableNames.Length} variables; got {vars.Count}.",
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
    /// Project an MPD design into the 5-dim SA vector. Inverse of <see cref="Unpack"/>.
    /// </summary>
    public static double[] Pack(ElectricPropulsionEngineDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return new[]
        {
            design.MpdArcCurrent_A,
            design.PropellantMassFlow_kgs,
            design.MpdCathodeRadius_mm,
            design.MpdAnodeRadius_mm,
            design.MpdChamberLength_mm,
        };
    }

    /// <summary>
    /// Inflate an SA vector + baseline design into a concrete MPD design.
    /// Categorical state (Kind, MpdCathodeMaterial) is preserved from
    /// <paramref name="baseline"/>.
    /// </summary>
    public static ElectricPropulsionEngineDesign Unpack(
        double[] vector, ElectricPropulsionEngineDesign baseline)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(baseline);
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"MPD vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));
        return baseline with
        {
            MpdArcCurrent_A         = vector[0],
            PropellantMassFlow_kgs  = vector[1],
            MpdCathodeRadius_mm     = vector[2],
            MpdAnodeRadius_mm       = vector[3],
            MpdChamberLength_mm     = vector[4],
        };
    }

    /// <summary>
    /// Apply the bind-time bus-power clip. V_arc × J_arc ≤ BusPower. V_arc
    /// depends on geometry; pin the worst-case value (max L / min r_a) so
    /// the clip is conservative.
    /// </summary>
    private static DesignVariableInfo[] ApplyBusPowerClip(
        DesignVariableInfo[] defaults,
        ResistojetConditions conditions)
    {
        var clipped = new DesignVariableInfo[defaults.Length];
        Array.Copy(defaults, clipped, defaults.Length);
        if (conditions.BusPower_W_avail > 0)
        {
            // Worst-case (highest) V_arc across the design space:
            //   V_arc_max = V_anode + V_col · (L_max / r_a_min)
            double maxL    = defaults[4].Max;
            double minRa   = defaults[3].Min;
            double V_max   = SelfFieldLorentzModel.AnodeFallVoltage_V
                           + SelfFieldLorentzModel.ArcColumnVoltageCoefficient_V * (maxL / minRa);
            double maxJ_busLimit = conditions.BusPower_W_avail / V_max;
            double newMax = Math.Min(defaults[0].Max, maxJ_busLimit);
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
