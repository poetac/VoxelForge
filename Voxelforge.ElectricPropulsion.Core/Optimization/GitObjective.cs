// GitObjective.cs — IObjective adapter for the Wave-2 Gridded-Ion Thruster.
// Mirrors PptObjective shape so the SA / CMA-ES / NSGA-II portfolio reaches
// GIT via the same EngineObjectiveAdapter<,,>.
//
// Vector layout (Wave-2, 6 dims, Sprint EP.W2.GIT):
//   0  BeamVoltage_V                500    – 1500   V
//   1  BeamCurrent_A                  0.5  –    3.0 A
//   2  ScreenGridRadius_mm           50    –  200   mm
//   3  AccelGridGap_mm                0.5  –    3.0 mm
//   4  NeutralizerCathodeCurrent_A    0.1  –    3.5 A
//   5  GitMassUtilizationOverride     0.85 –    0.95 (NaN-init for fixtures; real range for SA)
//
// Score: −Isp_vacuum on feasible solves; +∞ on infeasible (uniform with
// ResistojetObjective / HetObjective / ArcjetObjective / PptObjective).
//
// Bind-time clip: V_b × J_b ≤ conditions.BusPower_W_avail. Implemented by
// clipping dim 1 (BeamCurrent_A) given the dim-0 maximum BeamVoltage_V —
// the SA inner loop will explore inside the resulting rectangle.

using System;
using System.Collections.Generic;
using Voxelforge.ElectricPropulsion.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.ElectricPropulsion.Optimization;

/// <summary>
/// Convenience factory for an <see cref="EngineObjectiveAdapter{TDesign,TConditions,TResult}"/>
/// over <see cref="ElectricPropulsionEngine"/> for the GIT variant.
/// </summary>
public static class GitObjective
{
    /// <summary>
    /// Names of the six GIT design vector slots. Order is load-bearing.
    /// </summary>
    public static readonly string[] DefaultVariableNames =
    {
        "BeamVoltage_V",
        "BeamCurrent_A",
        "ScreenGridRadius_mm",
        "AccelGridGap_mm",
        "NeutralizerCathodeCurrent_A",
        "GitMassUtilizationOverride",
    };

    /// <summary>
    /// Default 6-dim bounds spanning the NSTAR / NEXT cluster + headroom
    /// (Goebel &amp; Katz §5 Wave-2 envelope).
    /// </summary>
    /// <remarks>
    /// Dim 5 (<c>GitMassUtilizationOverride</c>) is held at NaN by hand-
    /// authored fixtures (cluster-anchor mode at η_m = 0.90). SA cannot
    /// sample NaN, so the dim runs a narrow real range (0.85–0.95) when
    /// binding the optimizer; SA-driven designs always end up with a finite
    /// override value.
    /// </remarks>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("BeamVoltage_V",                500.0, 1500.0),
        new("BeamCurrent_A",                  0.5,    3.0),
        new("ScreenGridRadius_mm",           50.0,  200.0),
        new("AccelGridGap_mm",                0.5,    3.0),
        new("NeutralizerCathodeCurrent_A",    0.1,    3.5),
        new("GitMassUtilizationOverride",     0.85,   0.95),
    };

    /// <summary>
    /// Build an <see cref="IObjective"/> over the GIT pipeline + given
    /// conditions + baseline design. Bounds default to <see cref="DefaultBounds"/>
    /// with the bind-time bus-power clip applied to dim 1 (BeamCurrent_A).
    /// </summary>
    public static IObjective Build(
        ResistojetConditions conditions,
        ElectricPropulsionEngineDesign baseline,
        IReadOnlyList<DesignVariableInfo>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.Kind != ElectricPropulsionEngineKind.GriddedIon)
            throw new ArgumentException(
                $"GitObjective.Build requires baseline.Kind=GriddedIon; got {baseline.Kind}.",
                nameof(baseline));

        var vars = variables ?? ApplyBusPowerClip(DefaultBounds, conditions);
        if (vars.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"GIT design vector requires exactly {DefaultVariableNames.Length} variables; got {vars.Count}.",
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
    /// Project a GIT design into the 6-dim SA vector. Inverse of <see cref="Unpack"/>.
    /// </summary>
    public static double[] Pack(ElectricPropulsionEngineDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return new[]
        {
            design.BeamVoltage_V,
            design.BeamCurrent_A,
            design.ScreenGridRadius_mm,
            design.AccelGridGap_mm,
            design.NeutralizerCathodeCurrent_A,
            design.GitMassUtilizationOverride,
        };
    }

    /// <summary>
    /// Inflate an SA vector + baseline design into a concrete GIT design.
    /// Categorical state (Kind) is preserved from <paramref name="baseline"/>.
    /// </summary>
    public static ElectricPropulsionEngineDesign Unpack(
        double[] vector, ElectricPropulsionEngineDesign baseline)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(baseline);
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"GIT vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));
        return baseline with
        {
            BeamVoltage_V               = vector[0],
            BeamCurrent_A               = vector[1],
            ScreenGridRadius_mm         = vector[2],
            AccelGridGap_mm             = vector[3],
            NeutralizerCathodeCurrent_A = vector[4],
            GitMassUtilizationOverride  = vector[5],
        };
    }

    /// <summary>
    /// Apply the bind-time bus-power clip. V_b × J_b ≤ BusPower. Clip dim 1
    /// (J_b) given the dim-0 max V_b so the upper-current edge respects bus
    /// power. SA's inner loop still explores inside the resulting rectangle.
    /// </summary>
    private static DesignVariableInfo[] ApplyBusPowerClip(
        DesignVariableInfo[] defaults,
        ResistojetConditions conditions)
    {
        var clipped = new DesignVariableInfo[defaults.Length];
        Array.Copy(defaults, clipped, defaults.Length);
        if (conditions.BusPower_W_avail > 0)
        {
            double maxV = defaults[0].Max;                          // BeamVoltage_V max
            double maxJ_busLimit = conditions.BusPower_W_avail / maxV;
            double newMax = Math.Min(defaults[1].Max, maxJ_busLimit);
            double newMin = Math.Min(defaults[1].Min, newMax);
            clipped[1] = new DesignVariableInfo(defaults[1].Name, newMin, newMax);
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
