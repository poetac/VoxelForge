// PptObjective.cs — IObjective adapter for the Wave-2 PPT variant.
// Mirrors ArcjetObjective shape so the SA / CMA-ES / NSGA-II portfolio reaches
// PPT via the same EngineObjectiveAdapter<,,>.
//
// Vector layout (Wave-2, 6 dims, Sprint EP.W2.PPT):
//   0  CapacitorEnergy_J          0.5  – 50    J
//   1  PulseFrequency_Hz          0.1  – 10    Hz
//   2  PptElectrodeGap_mm         5.0  – 30    mm
//   3  PptPropellantBarLength_mm  5.0  – 30    mm
//   4  PptElectrodeWidth_mm       5.0  – 30    mm
//   5  PptIspCalibration        500.0  – 1500  s   (NaN-init for fixtures; real range for SA)
//
// Score: −Isp_vacuum on feasible solves; +∞ on infeasible (uniform with
// ResistojetObjective / HetObjective / ArcjetObjective).
//
// Bind-time clip: E_cap × f_pulse ≤ conditions.BusPower_W_avail. Implemented
// by clipping dim 0 (CapacitorEnergy_J) given the dim-1 maximum f_pulse —
// the SA inner loop will explore inside the resulting rectangle.

using System;
using System.Collections.Generic;
using Voxelforge.ElectricPropulsion.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.ElectricPropulsion.Optimization;

/// <summary>
/// Convenience factory for an <see cref="EngineObjectiveAdapter{TDesign,TConditions,TResult}"/>
/// over <see cref="ElectricPropulsionEngine"/> for the PPT variant.
/// </summary>
public static class PptObjective
{
    /// <summary>
    /// Names of the six PPT design vector slots. Order is load-bearing.
    /// </summary>
    public static readonly string[] DefaultVariableNames =
    {
        "CapacitorEnergy_J",
        "PulseFrequency_Hz",
        "PptElectrodeGap_mm",
        "PptPropellantBarLength_mm",
        "PptElectrodeWidth_mm",
        "PptIspCalibration",
    };

    /// <summary>
    /// Default 6-dim bounds spanning the Aerojet EO-1 / LES-6 / NRL-class
    /// PPT cluster + headroom (Solbes-Vondra Wave-2 envelope).
    /// </summary>
    /// <remarks>
    /// Dim 5 (<c>PptIspCalibration</c>) is held at NaN by hand-authored
    /// fixtures (cluster-anchor mode). SA cannot sample NaN, so the dim
    /// runs a real range (500–1500 s) when binding the optimizer; SA-driven
    /// designs always end up with a finite calibration value.
    /// </remarks>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("CapacitorEnergy_J",            0.5,    50.0),
        new("PulseFrequency_Hz",            0.1,    10.0),
        new("PptElectrodeGap_mm",           5.0,    30.0),
        new("PptPropellantBarLength_mm",    5.0,    30.0),
        new("PptElectrodeWidth_mm",         5.0,    30.0),
        new("PptIspCalibration",          500.0,  1500.0),
    };

    /// <summary>
    /// Build an <see cref="IObjective"/> over the PPT pipeline + given
    /// conditions + baseline design. Bounds default to <see cref="DefaultBounds"/>
    /// with the bind-time bus-power clip applied to dim 0 (CapacitorEnergy_J).
    /// </summary>
    public static IObjective Build(
        ResistojetConditions conditions,
        ElectricPropulsionEngineDesign baseline,
        IReadOnlyList<DesignVariableInfo>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.Kind != ElectricPropulsionEngineKind.PulsedPlasmaThruster)
            throw new ArgumentException(
                $"PptObjective.Build requires baseline.Kind=PulsedPlasmaThruster; got {baseline.Kind}.",
                nameof(baseline));

        var vars = variables ?? ApplyBusPowerClip(DefaultBounds, conditions);
        if (vars.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"PPT design vector requires exactly {DefaultVariableNames.Length} variables; got {vars.Count}.",
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
    /// Project a PPT design into the 6-dim SA vector. Inverse of <see cref="Unpack"/>.
    /// </summary>
    public static double[] Pack(ElectricPropulsionEngineDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return new[]
        {
            design.CapacitorEnergy_J,
            design.PulseFrequency_Hz,
            design.PptElectrodeGap_mm,
            design.PptPropellantBarLength_mm,
            design.PptElectrodeWidth_mm,
            design.PptIspCalibration,
        };
    }

    /// <summary>
    /// Inflate an SA vector + baseline design into a concrete PPT design.
    /// Categorical state (Kind) is preserved from <paramref name="baseline"/>.
    /// </summary>
    public static ElectricPropulsionEngineDesign Unpack(
        double[] vector, ElectricPropulsionEngineDesign baseline)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(baseline);
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"PPT vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));
        return baseline with
        {
            CapacitorEnergy_J         = vector[0],
            PulseFrequency_Hz         = vector[1],
            PptElectrodeGap_mm        = vector[2],
            PptPropellantBarLength_mm = vector[3],
            PptElectrodeWidth_mm      = vector[4],
            PptIspCalibration         = vector[5],
        };
    }

    /// <summary>
    /// Apply the bind-time bus-power clip. E_cap × f_pulse ≤ BusPower. Clip
    /// dim 0 (E_cap) given the dim-1 max f_pulse so the upper-energy edge
    /// respects bus power. SA's inner loop still explores inside the
    /// resulting rectangle.
    /// </summary>
    private static DesignVariableInfo[] ApplyBusPowerClip(
        DesignVariableInfo[] defaults,
        ResistojetConditions conditions)
    {
        var clipped = new DesignVariableInfo[defaults.Length];
        Array.Copy(defaults, clipped, defaults.Length);
        if (conditions.BusPower_W_avail > 0)
        {
            double maxF = defaults[1].Max;                          // PulseFrequency_Hz max
            double maxE_busLimit = conditions.BusPower_W_avail / maxF;
            double newMax = Math.Min(defaults[0].Max, maxE_busLimit);
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
