// HetObjective.cs — IObjective adapter for the Wave-2 Hall-Effect
// Thruster. Mirrors ResistojetObjective shape exactly so the SA /
// CMA-ES / NSGA-II portfolio reaches HET via the same
// EngineObjectiveAdapter<,,>.
//
// Vector layout (Wave-2, 6 dims, ADR-029 D5 / pillar-spec §11):
//   0  DischargeVoltage_V        200 – 400 V
//   1  DischargeCurrent_A        5   – 25  A
//   2  MagneticField_T           0.01 – 0.03 T
//   3  AnodeRadius_mm            20 – 60 mm
//   4  ChannelLength_mm          15 – 40 mm
//   5  XenonMassFlow_kgs         5e-6 – 3e-5 kg/s
//
// Score: −Isp_vacuum on feasible solves; +∞ on infeasible (same projection
// as ResistojetObjective so the IObjective contract is uniform across
// EP variants).
//
// Bind-time clip per ADR-029 D5: V_d × I_d ≤ conditions.BusPower_W_avail.
// Implemented by clipping dim 1 (DischargeCurrent_A) given the dim-0
// V_d midpoint — the SA inner loop will explore inside the resulting
// rectangle. Goebel & Katz §3 cluster bounds are wider than realistic
// flight-bus power; the clip is the load-bearing constraint.

using System;
using System.Collections.Generic;
using Voxelforge.ElectricPropulsion.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.ElectricPropulsion.Optimization;

/// <summary>
/// Convenience factory for an <see cref="EngineObjectiveAdapter{TDesign,TConditions,TResult}"/>
/// over <see cref="ElectricPropulsionEngine"/> for the Hall-Effect
/// variant.
/// </summary>
public static class HetObjective
{
    /// <summary>
    /// Names of the six HET design vector slots. Order is load-bearing.
    /// </summary>
    public static readonly string[] DefaultVariableNames =
    {
        "DischargeVoltage_V",
        "DischargeCurrent_A",
        "MagneticField_T",
        "AnodeRadius_mm",
        "ChannelLength_mm",
        "XenonMassFlow_kgs",
    };

    /// <summary>
    /// Default 6-dim bounds spanning the BPT-4000 / SPT-100 / PPS-1350
    /// cluster + headroom (Goebel &amp; Katz §3 Table 3-1).
    /// </summary>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("DischargeVoltage_V",     200.0,    400.0),
        new("DischargeCurrent_A",       5.0,     25.0),
        new("MagneticField_T",          0.01,     0.03),
        new("AnodeRadius_mm",          20.0,     60.0),
        new("ChannelLength_mm",        15.0,     40.0),
        new("XenonMassFlow_kgs",        5.0e-6,   3.0e-5),
    };

    /// <summary>
    /// Build an <see cref="IObjective"/> over the HET pipeline + given
    /// conditions + baseline design. Bounds default to <see cref="DefaultBounds"/>
    /// with the bind-time bus-power clip applied to dim 1 (DischargeCurrent_A).
    /// </summary>
    public static IObjective Build(
        ResistojetConditions conditions,
        ElectricPropulsionEngineDesign baseline,
        IReadOnlyList<DesignVariableInfo>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.Kind != ElectricPropulsionEngineKind.HallEffect)
            throw new ArgumentException(
                $"HetObjective.Build requires baseline.Kind=HallEffect; got {baseline.Kind}.",
                nameof(baseline));

        var vars = variables ?? ApplyBusPowerClip(DefaultBounds, conditions);
        if (vars.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"HET design vector requires exactly {DefaultVariableNames.Length} variables; got {vars.Count}.",
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
    /// Project an HET design into the 6-dim SA vector. Inverse of <see cref="Unpack"/>.
    /// </summary>
    public static double[] Pack(ElectricPropulsionEngineDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return new[]
        {
            design.DischargeVoltage_V,
            design.DischargeCurrent_A,
            design.MagneticField_T,
            design.AnodeRadius_mm,
            design.ChannelLength_mm,
            design.XenonMassFlow_kgs,
        };
    }

    /// <summary>
    /// Inflate an SA vector + baseline design into a concrete HET design.
    /// Categorical state (Kind, AnodeMaterial, CathodeType) is preserved
    /// from <paramref name="baseline"/>.
    /// </summary>
    public static ElectricPropulsionEngineDesign Unpack(
        double[] vector, ElectricPropulsionEngineDesign baseline)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(baseline);
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"HET vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));
        return baseline with
        {
            DischargeVoltage_V = vector[0],
            DischargeCurrent_A = vector[1],
            MagneticField_T    = vector[2],
            AnodeRadius_mm     = vector[3],
            ChannelLength_mm   = vector[4],
            XenonMassFlow_kgs  = vector[5],
        };
    }

    /// <summary>
    /// Apply the bind-time bus-power clip. ADR-029 D5: V_d × I_d ≤ BusPower.
    /// Clip dim 1 (I_d) given the dim-0 midpoint V_d so the upper-current
    /// edge respects bus power. SA's inner loop still explores inside the
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
            // Approximate the worst case: max V_d gives the lowest allowable I_d.
            double maxVd = defaults[0].Max;
            double maxId_busLimit = conditions.BusPower_W_avail / maxVd;
            double newMax = Math.Min(defaults[1].Max, maxId_busLimit);
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
