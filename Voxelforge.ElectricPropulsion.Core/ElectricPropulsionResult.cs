// ElectricPropulsionResult.cs — full evaluation result for one
// electric-propulsion design candidate.
//
// Sibling to RegenGenerationResult on the rocket side and
// AirbreathingResult on the air-breathing side. Carries the scalar
// performance outputs (thrust, Isp, efficiency), the thermal state
// (heater + chamber temps, radiation losses), and the feasibility
// verdict (hard violations + advisories).

using System.Collections.Generic;
using Voxelforge.ElectricPropulsion.Plasma;
using Voxelforge.Engines;
using Voxelforge.Optimization;
using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Full evaluation result for one
/// <see cref="ElectricPropulsionEngineDesign"/> +
/// <see cref="ResistojetConditions"/> pair.
/// </summary>
/// <param name="Design">The candidate that produced this result. Echoed back so reporting can correlate.</param>
/// <param name="Conditions">The operating conditions it was evaluated against.</param>
/// <param name="Thrust_N">Vacuum thrust [N] = ṁ · V_exit + (P_exit − P_∞) · A_exit.</param>
/// <param name="IspVacuum_s">Vacuum specific impulse [s] = Thrust / (ṁ · g₀) with P_∞ = 0.</param>
/// <param name="ExitVelocity_ms">Nozzle exit gas velocity [m/s] = M_exit · √(γ · R · T_exit).</param>
/// <param name="ThrustEfficiency">Useful kinetic / electrical input η_T = (½ ṁ V_exit²) / P_in.</param>
/// <param name="HeaterTemp_K">Heater coil surface temperature [K] from the lumped 0-D solve.</param>
/// <param name="ChamberTemp_K">Bulk chamber gas temperature [K] from the lumped 0-D solve.</param>
/// <param name="ExitMachNumber">Mach number at nozzle exit (M_exit, supersonic when feasible).</param>
/// <param name="ExitPressure_Pa">Static pressure at nozzle exit [Pa].</param>
/// <param name="RadiationLossFraction">q_rad / P_in — radiation loss fraction; bound by Hard gate <c>RESISTOJET_RADIATION_FRACTION_EXCESSIVE</c>.</param>
/// <param name="ChokedFlow">True iff P_chamber/P_∞ exceeds the choking criterion ((γ+1)/2)^(γ/(γ-1)).</param>
/// <param name="Violations">
/// Hard-constraint violations from <see cref="ElectricPropulsionFeasibility"/>.
/// Empty when feasible.
/// </param>
/// <param name="IsFeasible">Convenience: true when <see cref="Violations"/> is empty.</param>
public sealed record ElectricPropulsionResult(
    ElectricPropulsionEngineDesign      Design,
    ResistojetConditions                Conditions,
    double                              Thrust_N,
    double                              IspVacuum_s,
    double                              ExitVelocity_ms,
    double                              ThrustEfficiency,
    double                              HeaterTemp_K,
    double                              ChamberTemp_K,
    double                              ExitMachNumber,
    double                              ExitPressure_Pa,
    double                              RadiationLossFraction,
    bool                                ChokedFlow,
    IReadOnlyList<FeasibilityViolation> Violations,
    bool                                IsFeasible) : IEngineResult
{
    /// <summary>
    /// Advisory violations — soft warnings that surface to UI / report
    /// without gating optimization (e.g. <c>RESISTOJET_AREA_RATIO_OUT_OF_BAND</c>,
    /// <c>RESISTOJET_FROZEN_FLOW_LOSS_EXCESSIVE</c>). Reuses the
    /// <see cref="FeasibilityViolation"/> shape so consumers treat
    /// advisories and hard violations identically except for whether
    /// they fail <see cref="IsFeasible"/>.
    /// </summary>
    public IReadOnlyList<FeasibilityViolation> Advisories { get; init; }
        = System.Array.Empty<FeasibilityViolation>();

    /// <summary>
    /// Plasma state for Wave-2+ variants (HET / arcjet / ion / MPD).
    /// <see langword="null"/> for Resistojet runs. Populated by the
    /// per-kind pipeline inside
    /// <see cref="ElectricPropulsionOptimization.GenerateWith"/>;
    /// concrete shape per ADR-029 D1 (e.g. <see cref="HetPlasmaState"/>
    /// for <see cref="ElectricPropulsionEngineKind.HallEffect"/>).
    /// </summary>
    public IPlasmaState? PlasmaState { get; init; } = null;
}
