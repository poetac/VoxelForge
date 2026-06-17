// NtrGenerationResult.cs — full evaluation result for one NTR candidate.
//
// Implements IEngineResult. Analogous to MarineResult / ElectricPropulsionResult.
// Carries cycle outputs, regen cooling summary, and gate violations.

using System.Collections.Generic;
using Voxelforge.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.Nuclear;

/// <summary>
/// Full evaluation result for one <see cref="NuclearThermalDesign"/> +
/// <see cref="NuclearThermalConditions"/> pair.
/// </summary>
/// <param name="Design">Input design (echo-back for correlation).</param>
/// <param name="Conditions">Input conditions (echo-back for correlation).</param>
/// <param name="CoreExitTemp_K">Core exit temperature [K].</param>
/// <param name="GammaEff">Effective γ of H₂ at core exit temperature [-].</param>
/// <param name="CStar_ms">Characteristic velocity c* [m/s].</param>
/// <param name="IspVacuum_s">Vacuum specific impulse [s].</param>
/// <param name="ThrustVacuum_N">Vacuum thrust [N].</param>
/// <param name="VolumetricHeatFlux_MWm3">Reactor volumetric heat flux [MW/m³].</param>
/// <param name="KEff">Heuristic neutron multiplication factor k_eff [-].</param>
/// <param name="RegenNozzleWallTempExceedsLimit">
/// True when the nozzle regen solver found peak wall T above Inconel 718 limit.
/// False when the regen pass was not run or wall T is within limits.
/// </param>
/// <param name="Violations">Hard constraint violations. Empty when feasible.</param>
/// <param name="Advisories">Advisory warnings. Does not gate feasibility.</param>
/// <param name="IsFeasible">Convenience: <c>true</c> when Violations is empty.</param>
public sealed record NtrGenerationResult(
    NuclearThermalDesign Design,
    NuclearThermalConditions Conditions,
    double CoreExitTemp_K,
    double GammaEff,
    double CStar_ms,
    double IspVacuum_s,
    double ThrustVacuum_N,
    double VolumetricHeatFlux_MWm3,
    double KEff,
    bool RegenNozzleWallTempExceedsLimit,
    IReadOnlyList<FeasibilityViolation> Violations,
    IReadOnlyList<FeasibilityViolation> Advisories,
    bool IsFeasible) : IEngineResult
{
    /// <summary>
    /// Sprint NU.W2 — peak fuel-pin centreline temperature T_peak [K] from
    /// <see cref="FuelPin.FuelPinHeatModel"/>. <see cref="double.NaN"/>
    /// when the per-pin model was not run (Wave-1 lumped-only path).
    /// </summary>
    public double PeakFuelCenterlineTemp_K { get; init; } = double.NaN;

    /// <summary>
    /// Sprint NU.W2 — pin outer surface temperature T_surf [K].
    /// NaN when the per-pin model was not run.
    /// </summary>
    public double PinSurfaceTemp_K { get; init; } = double.NaN;

    /// <summary>
    /// Sprint NU.W2 — applied hot-channel factor F_hc [-].
    /// NaN when the per-pin model was not run.
    /// </summary>
    public double FuelPinHotChannelFactor { get; init; } = double.NaN;

    /// <summary>
    /// Sprint NU.W2 — coolant exit temperature T_cool,exit [K] from the
    /// per-pin energy balance. NaN when the per-pin model was not run.
    /// </summary>
    public double FuelPinCoolantExitTemp_K { get; init; } = double.NaN;

    // ── Wave-3 bimodal NTR fields (Sprint NU.W3) ────────────────────────

    /// <summary>
    /// Sprint NU.W3 — net electric power output [kW_e] from the He Brayton
    /// gas loop. <see cref="double.NaN"/> when the bimodal pipeline didn't
    /// run (NervaSolidCore or BimodalMode.Thrust paths).
    /// </summary>
    public double ElectricPowerOutput_kWe { get; init; } = double.NaN;

    /// <summary>
    /// Sprint NU.W3 — closed-cycle Brayton thermal efficiency η = P_elec /
    /// Q_brayton. NaN when the bimodal pipeline didn't run.
    /// </summary>
    public double BraytonThermalEfficiency { get; init; } = double.NaN;

    /// <summary>
    /// Sprint NU.W3 — Carnot upper bound η_carnot = 1 − T_cold/T_hot.
    /// NaN when the bimodal pipeline didn't run.
    /// </summary>
    public double BraytonCarnotEfficiency { get; init; } = double.NaN;

    /// <summary>
    /// Sprint NU.W3 — reactor thermal power routed to the Brayton loop [MW].
    /// NaN when the bimodal pipeline didn't run.
    /// </summary>
    public double ReactorPowerToBrayton_MW { get; init; } = double.NaN;

    /// <summary>
    /// Sprint NU.W3 — He working-fluid mass flow ṁ_He [kg/s]. NaN when
    /// the bimodal pipeline didn't run.
    /// </summary>
    public double BraytonHeMassFlow_kgs { get; init; } = double.NaN;
}
