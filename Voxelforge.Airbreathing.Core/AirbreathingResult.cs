// AirbreathingResult.cs — full evaluation result for one air-breathing
// design candidate.
//
// Sibling to RegenGenerationResult on the rocket side. Carries the
// station map (cycle output), the feasibility-gate result, and a
// (sprint-A4-onward) score breakdown the optimizer reads.

using System.Collections.Generic;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing;

/// <summary>
/// Full evaluation result for one <see cref="AirbreathingEngineDesign"/>
/// + <see cref="FlightConditions"/> pair.
/// </summary>
/// <param name="Design">The candidate that produced this result. Echo back so reporting can correlate.</param>
/// <param name="Conditions">The flight envelope it was evaluated against.</param>
/// <param name="Stations">Station-by-station thermodynamic state from the cycle solver.</param>
/// <param name="Violations">
/// Hard-constraint violations from <see cref="AirbreathingFeasibility"/>.
/// Empty when feasible.
/// </param>
/// <param name="IsFeasible">Convenience: true when <see cref="Violations"/> is empty.</param>
public sealed record AirbreathingResult(
    AirbreathingEngineDesign Design,
    FlightConditions Conditions,
    StationMap Stations,
    IReadOnlyList<FeasibilityViolation> Violations,
    bool IsFeasible) : IEngineResult
{
    /// <summary>
    /// Advisory violations — soft warnings that surface to UI / report
    /// without gating optimization (e.g. SURGE_MARGIN_INSUFFICIENT,
    /// fired when compressor surge margin falls below the 10 % industry
    /// preliminary-design floor). Reuses the
    /// <see cref="FeasibilityViolation"/> shape so consumers treat
    /// advisories and hard violations identically except for whether
    /// they fail <see cref="IsFeasible"/>.
    /// </summary>
    /// <remarks>
    /// Default empty so existing callers that construct
    /// <see cref="AirbreathingResult"/> positionally without setting
    /// <see cref="Advisories"/> remain backward-compatible.
    /// </remarks>
    public IReadOnlyList<FeasibilityViolation> Advisories { get; init; }
        = System.Array.Empty<FeasibilityViolation>();

    /// <summary>
    /// Net shaft power output [W]. Non-zero only for
    /// <see cref="AirbreathingEngineKind.GasTurbine"/>; propulsive
    /// cycle results leave this at 0.
    /// </summary>
    public double ShaftPower_W { get; init; } = 0.0;

    /// <summary>
    /// Cycle thermal efficiency η_th = W_net / Q_fuel [-].
    /// Non-zero only for <see cref="AirbreathingEngineKind.GasTurbine"/>.
    /// </summary>
    public double ThermalEfficiency { get; init; } = 0.0;

    /// <summary>
    /// Specific work W_net / ṁ_air [J/kg].
    /// Non-zero only for <see cref="AirbreathingEngineKind.GasTurbine"/>.
    /// </summary>
    public double SpecificWork_Jkg { get; init; } = 0.0;

    /// <summary>
    /// Estimated buzz (acoustic) frequency [Hz] from
    /// <c>HalfWavePipeAcousticCalculator.CombinedFrequency_Hz</c> with
    /// pulsejet-variant dispatch (Standard reed-valve closed-open vs Valveless
    /// Lockwood-Hiller open-open). Non-NaN only for
    /// <see cref="AirbreathingEngineKind.Pulsejet"/>; all other kinds leave
    /// this at NaN.
    /// </summary>
    public double EstimatedBuzzFrequency_Hz { get; init; } = double.NaN;
}
