// NtrObjective.cs — IObjective adapter wiring the NTR physics into the
// engine-family-agnostic SA / CMA-ES / NSGA-II optimizer surface.
//
// Uses EngineObjectiveAdapter from Voxelforge.Core.Optimization (same adapter
// used by ResistojetObjective). Per-pillar boilerplate is therefore minimal —
// just bounds + unpack + score projection.
//
// Vector layout (Wave-1, 6 dims, matches pillar spec §2):
//   0  ReactorThermalPower_MW   [50, 2000]
//   1  PropellantMassFlow_kgs   [1, 50]
//   2  ChamberPressure_bar      [25, 80]
//   3  ThroatRadius_mm          [5, 200]
//   4  ExpansionRatio           [20, 200]
//   5  RegenChannelDepth_mm     [0.5, 5.0]
//
// Score: −Isp_vacuum on feasible solves; +∞ on infeasible (canonical
// IObjective infeasibility sentinel; SA's MultiChainOptimizer treats
// +∞ as "never accepted as new best").

using System;
using System.Collections.Generic;
using Voxelforge.Nuclear.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.Nuclear.Optimization;

/// <summary>
/// Convenience factory for an
/// <see cref="EngineObjectiveAdapter{TDesign,TConditions,TResult}"/>
/// over <see cref="NuclearEngine"/>. Wave-1 covers the 6-dim NTR vector
/// layout per pillar spec §2.
/// </summary>
public static class NtrObjective
{
    /// <summary>
    /// Names of the six NTR design vector slots. Order is load-bearing —
    /// <see cref="Pack"/> + <see cref="Unpack"/> depend on it.
    /// </summary>
    public static readonly string[] DefaultVariableNames =
    {
        "ReactorThermalPower_MW",
        "PropellantMassFlow_kgs",
        "ChamberPressure_bar",
        "ThroatRadius_mm",
        "ExpansionRatio",
        "RegenChannelDepth_mm",
    };

    /// <summary>
    /// Default 6-dim bounds spanning the NERVA/NRX-A6 operating cluster
    /// + headroom per pillar spec §2.
    /// </summary>
    public static readonly DesignVariableInfo[] DefaultBounds =
    {
        new("ReactorThermalPower_MW",    50.0,   2000.0),
        new("PropellantMassFlow_kgs",     1.0,     50.0),
        new("ChamberPressure_bar",       25.0,     80.0),
        new("ThroatRadius_mm",            5.0,    200.0),
        new("ExpansionRatio",            20.0,    200.0),
        new("RegenChannelDepth_mm",       0.5,      5.0),
    };

    /// <summary>
    /// Bounds calibrated to the NRX-A6 operating regime (1000 MW,
    /// 30 kg/s, 30–60 bar, 50–150 mm throat, ε 50–150, 1–4 mm regen).
    /// </summary>
    public static readonly DesignVariableInfo[] NervaBounds =
    {
        new("ReactorThermalPower_MW",   500.0,  1500.0),
        new("PropellantMassFlow_kgs",    10.0,    50.0),
        new("ChamberPressure_bar",       30.0,    60.0),
        new("ThroatRadius_mm",           50.0,   150.0),
        new("ExpansionRatio",            50.0,   150.0),
        new("RegenChannelDepth_mm",       1.0,     4.0),
    };

    /// <summary>
    /// Build an <see cref="IObjective"/> over the NTR engine + given conditions
    /// + baseline design. Bounds default to <see cref="NervaBounds"/> when
    /// <paramref name="variables"/> is not supplied.
    /// </summary>
    /// <param name="conditions">Operating conditions (inlet temp, ΔV mission).</param>
    /// <param name="baseline">
    /// Baseline design — categorical state (Kind, FuelLoadingFraction,
    /// ReactorCoreLength_mm, ReactorCoreDiameter_mm, NozzleLength_mm,
    /// NozzleWallThickness_mm, NozzleChannelWidth_mm, NozzleManifoldDepth_mm,
    /// RegenChannelCount) preserved across optimizer iterations.
    /// </param>
    /// <param name="variables">Optional bound override; defaults to <see cref="NervaBounds"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="conditions"/> or <paramref name="baseline"/>
    /// is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="variables"/> is supplied but its count is
    /// not equal to <see cref="DefaultVariableNames"/>.Length (6).
    /// </exception>
    public static IObjective Build(
        NuclearThermalConditions conditions,
        NuclearThermalDesign     baseline,
        IReadOnlyList<DesignVariableInfo>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(baseline);

        var vars = variables ?? NervaBounds;
        if (vars.Count != DefaultVariableNames.Length)
            throw new ArgumentOutOfRangeException(nameof(variables),
                $"NTR design vector requires exactly {DefaultVariableNames.Length} variables; got {vars.Count}.");

        return new EngineObjectiveAdapter<NuclearThermalDesign, NuclearThermalConditions, NtrGenerationResult>(
            engine:     NuclearEngine.Instance,
            conditions: conditions,
            baseline:   baseline,
            variables:  vars,
            unpack:     (v, b) => Unpack(v, b),
            evaluate:   ScoreNegativeIsp);
    }

    /// <summary>
    /// Project a <see cref="NuclearThermalDesign"/> into the SA vector layout.
    /// Inverse of <see cref="Unpack"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> is <see langword="null"/>.
    /// </exception>
    public static double[] Pack(NuclearThermalDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return new[]
        {
            design.ReactorThermalPower_MW,
            design.PropellantMassFlow_kgs,
            design.ChamberPressure_bar,
            design.ThroatRadius_mm,
            design.ExpansionRatio,
            design.RegenChannelDepth_mm,
        };
    }

    /// <summary>
    /// Inflate an SA vector + baseline design into a concrete design record.
    /// Categorical state (Kind, FuelLoadingFraction, ReactorCore*,
    /// NozzleLength_mm, NozzleWallThickness_mm, NozzleChannelWidth_mm,
    /// NozzleManifoldDepth_mm, RegenChannelCount) is preserved from
    /// <paramref name="baseline"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="vector"/> or <paramref name="baseline"/>
    /// is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="vector"/>.Length is not equal to
    /// <see cref="DefaultVariableNames"/>.Length (6).
    /// </exception>
    public static NuclearThermalDesign Unpack(
        double[]             vector,
        NuclearThermalDesign baseline)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(baseline);
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentOutOfRangeException(nameof(vector),
                $"NTR vector requires {DefaultVariableNames.Length} elements; got {vector.Length}.");
        return baseline with
        {
            ReactorThermalPower_MW = vector[0],
            PropellantMassFlow_kgs = vector[1],
            ChamberPressure_bar    = vector[2],
            ThroatRadius_mm        = vector[3],
            ExpansionRatio         = vector[4],
            RegenChannelDepth_mm   = vector[5],
        };
    }

    // Score: minimise −Isp_vacuum on feasible solves; +∞ on infeasible.
    private static EvaluationResult ScoreNegativeIsp(NtrGenerationResult result)
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
