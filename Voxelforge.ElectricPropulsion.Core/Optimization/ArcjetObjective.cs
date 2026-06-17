// ArcjetObjective.cs — IObjective adapter for the Wave-2 Arcjet variant.
// Mirrors HetObjective shape so the SA / CMA-ES / NSGA-II portfolio reaches
// arcjet via the same EngineObjectiveAdapter<,,>.
//
// Vector layout (Wave-2, 6 dims, issue #476):
//   0  ArcCurrent_A              5 – 30 A
//   1  ArcVoltage_V              60 – 300 V
//   2  ArcGap_mm                 0.5 – 3.0 mm
//   3  PropellantMassFlow_kgs    5e-5 – 5e-4 kg/s   (higher than resistojet — NH3/H2/N2 mix)
//   4  NozzleThroatRadius_mm     0.5 – 3.0 mm
//   5  NozzleAreaRatio           50 – 200
//
// Score: −Isp_vacuum on feasible solves; +∞ on infeasible (uniform with
// ResistojetObjective / HetObjective).
//
// Bind-time clip: V_arc × I_arc ≤ conditions.BusPower_W_avail. Implemented
// by clipping dim 0 (ArcCurrent_A) given the dim-1 maximum V_arc — the SA
// inner loop will explore inside the resulting rectangle.

using System;
using System.Collections.Generic;
using Voxelforge.ElectricPropulsion.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.ElectricPropulsion.Optimization;

/// <summary>
/// Convenience factory for an <see cref="EngineObjectiveAdapter{TDesign,TConditions,TResult}"/>
/// over <see cref="ElectricPropulsionEngine"/> for the Arcjet variant.
/// </summary>
public static class ArcjetObjective
{
    /// <summary>
    /// Names of the six arcjet design vector slots. Order is load-bearing.
    /// </summary>
    public static readonly string[] DefaultVariableNames =
    {
        "ArcCurrent_A",
        "ArcVoltage_V",
        "ArcGap_mm",
        "PropellantMassFlow_kgs",
        "NozzleThroatRadius_mm",
        "NozzleAreaRatio",
    };

    /// <summary>
    /// Default 6-dim bounds spanning the MR-509 ATOS / Aerojet 1.8 kW /
    /// Velarc cluster + headroom (Sutton &amp; Biblarz 9e §16.3).
    /// </summary>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("ArcCurrent_A",             5.0,    30.0),
        new("ArcVoltage_V",            60.0,   300.0),
        new("ArcGap_mm",                0.5,     3.0),
        new("PropellantMassFlow_kgs",   5.0e-5,  5.0e-4),
        new("NozzleThroatRadius_mm",    0.5,     3.0),
        new("NozzleAreaRatio",         50.0,   200.0),
    };

    /// <summary>
    /// Build an <see cref="IObjective"/> over the Arcjet pipeline + given
    /// conditions + baseline design. Bounds default to <see cref="DefaultBounds"/>
    /// with the bind-time bus-power clip applied to dim 0 (ArcCurrent_A).
    /// </summary>
    public static IObjective Build(
        ResistojetConditions conditions,
        ElectricPropulsionEngineDesign baseline,
        IReadOnlyList<DesignVariableInfo>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.Kind != ElectricPropulsionEngineKind.Arcjet)
            throw new ArgumentException(
                $"ArcjetObjective.Build requires baseline.Kind=Arcjet; got {baseline.Kind}.",
                nameof(baseline));

        var vars = variables ?? ApplyBusPowerClip(DefaultBounds, conditions);
        if (vars.Count != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Arcjet design vector requires exactly {DefaultVariableNames.Length} variables; got {vars.Count}.",
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
    /// Project an Arcjet design into the 6-dim SA vector. Inverse of <see cref="Unpack"/>.
    /// </summary>
    public static double[] Pack(ElectricPropulsionEngineDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return new[]
        {
            design.ArcCurrent_A,
            design.ArcVoltage_V,
            design.ArcGap_mm,
            design.PropellantMassFlow_kgs,
            design.NozzleThroatRadius_mm,
            design.NozzleAreaRatio,
        };
    }

    /// <summary>
    /// Inflate an SA vector + baseline design into a concrete arcjet design.
    /// Categorical state (Kind, ArcjetElectrodeMaterial, etc.) is preserved
    /// from <paramref name="baseline"/>.
    /// </summary>
    public static ElectricPropulsionEngineDesign Unpack(
        double[] vector, ElectricPropulsionEngineDesign baseline)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(baseline);
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Arcjet vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.",
                nameof(vector));
        return baseline with
        {
            ArcCurrent_A           = vector[0],
            ArcVoltage_V           = vector[1],
            ArcGap_mm              = vector[2],
            PropellantMassFlow_kgs = vector[3],
            NozzleThroatRadius_mm  = vector[4],
            NozzleAreaRatio        = vector[5],
        };
    }

    /// <summary>
    /// Apply the bind-time bus-power clip. V_arc × I_arc ≤ BusPower. Clip
    /// dim 0 (I_arc) given the dim-1 max V_arc so the upper-current edge
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
            double maxVa = defaults[1].Max;                        // ArcVoltage_V max
            double maxIa_busLimit = conditions.BusPower_W_avail / maxVa;
            double newMax = Math.Min(defaults[0].Max, maxIa_busLimit);
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
