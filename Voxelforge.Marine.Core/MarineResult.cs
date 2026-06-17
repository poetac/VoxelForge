// MarineResult.cs — full evaluation result for one marine hull candidate.
//
// Implements IEngineResult. Analogous to AirbreathingResult on the
// air-breathing side. Carries hydrodynamic drag, buoyancy, structural
// margin, and the feasibility-gate result from MarineGates.Evaluate.

using System.Collections.Generic;
using Voxelforge.Engines;
using Voxelforge.Optimization;

namespace Voxelforge.Marine;

/// <summary>
/// Full evaluation result for one <see cref="MarineDesign"/> +
/// <see cref="MarineConditions"/> pair.
/// </summary>
/// <param name="Design">Input design (echo-back for correlation).</param>
/// <param name="Conditions">Input conditions (echo-back for correlation).</param>
/// <param name="DragForce_N">Total hull drag at cruise speed [N].</param>
/// <param name="DragCoefficient">C_D based on frontal area (π/4×D²) [-].</param>
/// <param name="BuoyancyForce_N">Archimedes uplift [N] = ρ_water × g × V_ext.</param>
/// <param name="DisplacedVolume_m3">External hull volume [m³].</param>
/// <param name="BuoyantWeight_N">
/// BuoyancyForce_N − HullMass_kg × g [N]. Positive = net positive buoyancy.
/// </param>
/// <param name="CriticalBucklingPressure_Pa">
/// ASME BPVC §VIII Div 1 UG-28 elastic thin-shell critical pressure [Pa].
/// Windenburg-Trilling (1934) formula for long cylinders.
/// </param>
/// <param name="BucklingSafetyFactor">
/// P_cr / P_hydrostatic_at_max_depth [—]. Must be ≥ 1.5.
/// </param>
/// <param name="HullMass_kg">Structural shell mass [kg].</param>
/// <param name="CgCbOffset_m">
/// |z_CG − z_CB| [m]. Zero for symmetric hull (M1 AuvMidBody); non-zero
/// when payload offset is applied in future variants.
/// </param>
/// <param name="Violations">Hard constraint violations. Empty when feasible.</param>
/// <param name="Advisories">Advisory warnings. Does not gate feasibility.</param>
/// <param name="IsFeasible">Convenience: <c>true</c> when Violations is empty.</param>
public sealed record MarineResult(
    MarineDesign Design,
    MarineConditions Conditions,
    double DragForce_N,
    double DragCoefficient,
    double BuoyancyForce_N,
    double DisplacedVolume_m3,
    double BuoyantWeight_N,
    double CriticalBucklingPressure_Pa,
    double BucklingSafetyFactor,
    double HullMass_kg,
    double CgCbOffset_m,
    IReadOnlyList<FeasibilityViolation> Violations,
    IReadOnlyList<FeasibilityViolation> Advisories,
    bool IsFeasible) : IEngineResult
{
    /// <summary>
    /// Sprint M.W3 — equilibrium trim angle τ [°] for SurfaceHull (Planing)
    /// designs. <see cref="double.NaN"/> for AUV kinds. Populated by
    /// <see cref="Hydrodynamics.SavitskyPlaningModel"/>.
    /// </summary>
    public double TrimAngle_deg { get; init; } = double.NaN;

    /// <summary>
    /// Sprint M.W3 — wetted-length-to-beam ratio λ [-] for SurfaceHull
    /// (Planing) designs. NaN for AUV kinds.
    /// </summary>
    public double WettedLengthToBeamRatio { get; init; } = double.NaN;

    /// <summary>
    /// Sprint M.W3 — beam-based Froude number C_v = V / √(gb) [-] for
    /// SurfaceHull (Planing) designs. NaN for AUV kinds.
    /// </summary>
    public double SpeedCoefficient { get; init; } = double.NaN;

    /// <summary>
    /// Sprint M.W3 — wetted surface area S_w [m²] for SurfaceHull (Planing)
    /// designs. NaN for AUV kinds. (Submerged AUVs use the fairing-derived
    /// area inside <see cref="DragForce_N"/>; not surfaced separately on
    /// the result for Wave-1/2 compat.)
    /// </summary>
    public double WettedSurfaceArea_m2 { get; init; } = double.NaN;
}
