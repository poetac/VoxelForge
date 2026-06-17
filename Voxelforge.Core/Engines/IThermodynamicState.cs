// IThermodynamicState.cs — common thermodynamic state interface.
//
// ADR-024 rec #3: design the unifying interface once ≥ 3 concrete
// implementations exist (rocket StationState, coolant CoolantState,
// combustion CombustionProductState). Sprint A8 ships the interface
// and wires the three concrete implementations.

namespace Voxelforge.Engines;

/// <summary>
/// Minimal thermodynamic state observable across engine families.
/// Implemented explicitly by concrete state records so callers that
/// know the concrete type can use its own public API without overhead;
/// callers that need family-agnostic code operate via this interface.
/// </summary>
public interface IThermodynamicState
{
    /// <summary>Static or stagnation temperature [K].</summary>
    double Temperature_K { get; }

    /// <summary>Static or stagnation pressure [Pa].</summary>
    double Pressure_Pa { get; }

    /// <summary>Specific enthalpy [J/kg].</summary>
    double Enthalpy_Jkg { get; }

    /// <summary>
    /// Specific entropy [J/(kg·K)]. Returns 0.0 when the model does
    /// not track entropy (deferred to a future cp(T) entropy table).
    /// </summary>
    double Entropy_JkgK { get; }

    /// <summary>Mass density [kg/m³].</summary>
    double Density_kgm3 { get; }
}
