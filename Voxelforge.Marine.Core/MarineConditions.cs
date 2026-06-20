// MarineConditions.cs — operating envelope for a marine vehicle.
//
// Implements IEngineConditions with Family = EngineFamilies.Marine.
// Analogous to FlightConditions on the air-breathing side.
// Distinct record because the rocket OperatingConditions / airbreathing
// FlightConditions carry physics (chamber pressure / Mach number) that
// do not apply to incompressible marine hydrodynamics.

using Voxelforge.Engines;

namespace Voxelforge.Marine;

/// <summary>
/// Operating conditions for a marine vehicle design.
/// Analogous to <c>FlightConditions</c> on the air-breathing side.
/// </summary>
/// <param name="CruiseSpeed_ms">Design cruise speed [m/s].</param>
/// <param name="MaxDepth_m">Maximum operating depth [m].</param>
/// <param name="WaterTemperature_K">
/// Seawater temperature [K]. Affects density and kinematic viscosity.
/// Default 277.15 K (4 °C — near-bottom deep-ocean typical).
/// </param>
/// <param name="Salinity_ppt">
/// Salinity [g/kg]. Default 35.0 (open-ocean standard).
/// </param>
public sealed record MarineConditions(
    double CruiseSpeed_ms,
    double MaxDepth_m,
    double WaterTemperature_K = 277.15,
    double Salinity_ppt = 35.0) : IEngineConditions
{
    /// <inheritdoc />
    public string Family => EngineFamilies.Marine;

    /// <summary>
    /// Seawater density [kg/m³] from Millero &amp; Poisson (1981) simplified:
    /// ρ ≈ 1025 + 0.8×S − 0.2×(T − 277). Valid for S ∈ [30, 40] g/kg,
    /// T ∈ [270, 290 K]. Floored at 900 kg/m³: the linear fit is an
    /// extrapolation outside that band and goes ≤ 0 for absurd temperatures
    /// (T ≳ 900 K), which would sign-flip buoyancy/drag and invert the
    /// HULL_BUOYANCY_NEGATIVE gate. The floor sits safely below any real
    /// water (fresh water at 100 °C ≈ 958 kg/m³) so the valid band is unchanged.
    /// </summary>
    public double WaterDensity_kgm3
        => System.Math.Max(
               900.0,
               1025.0 + 0.8 * (Salinity_ppt - 35.0) - 0.2 * (WaterTemperature_K - 277.0));

    /// <summary>
    /// Hydrostatic pressure at max depth [Pa] = ρ_water × g × h.
    /// g = 9.80665 m/s².
    /// </summary>
    public double HydrostaticPressure_Pa
        => WaterDensity_kgm3 * 9.80665 * MaxDepth_m;

    /// <summary>
    /// Kinematic viscosity of seawater [m²/s]. Approximated as
    /// 1.35e-6 at 4 °C (Van Mieghem 1954); temperature correction
    /// deferred (wave-1 AUV designs operate in a narrow temp range).
    /// </summary>
    public static double KinematicViscosity_m2s => 1.35e-6;
}
