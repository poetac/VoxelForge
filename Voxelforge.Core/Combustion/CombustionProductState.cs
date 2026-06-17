// CombustionProductState.cs — thermodynamic state of combustion products.
//
// Completes the IThermodynamicState contract (ADR-024 rec #3) for
// combustion-product gas mixtures. Complements StationState (air-
// breathing) and CoolantState (coolant channel). Sprint A8.

using Voxelforge.Engines;

namespace Voxelforge.Combustion;

/// <summary>
/// Thermodynamic state of a combustion product mixture at a given
/// station. Immutable value type; implements <see cref="IThermodynamicState"/>
/// for family-agnostic state consumers.
/// </summary>
/// <param name="Temperature_K">Local temperature [K].</param>
/// <param name="Pressure_Pa">Local pressure [Pa].</param>
/// <param name="Enthalpy_Jkg">Specific enthalpy [J/kg].</param>
/// <param name="Density_kgm3">Mass density [kg/m³].</param>
/// <param name="MolarMass_kgkmol">Mean molar mass of the mixture [kg/kmol].</param>
/// <param name="GammaEffective">Effective ratio of specific heats γ = cp/cv.</param>
public readonly record struct CombustionProductState(
    double Temperature_K,
    double Pressure_Pa,
    double Enthalpy_Jkg,
    double Density_kgm3,
    double MolarMass_kgkmol,
    double GammaEffective) : IThermodynamicState
{
    // Explicit interface implementations — non-breaking with callers
    // that use the record's own public properties directly.
    double IThermodynamicState.Temperature_K => Temperature_K;
    double IThermodynamicState.Pressure_Pa   => Pressure_Pa;
    double IThermodynamicState.Enthalpy_Jkg  => Enthalpy_Jkg;

    /// <summary>
    /// Entropy not tracked in the combustion product model; returns 0.
    /// Deferred to a future CEA entropy table.
    /// </summary>
    double IThermodynamicState.Entropy_JkgK  => 0.0;

    double IThermodynamicState.Density_kgm3  => Density_kgm3;
}
