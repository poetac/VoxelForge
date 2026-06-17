// FlightConditions.cs — flight envelope for an air-breathing engine.
//
// The air-breathing analogue of the rocket-side OperatingConditions.
// Distinct record because the rocket OperatingConditions carries
// chamber pressure / mixture ratio / propellant pair / igniter type —
// none of which apply to an air-breathing engine. Air-breathing's
// independent variables are the flight envelope (altitude, Mach number,
// optional inlet recovery override).

using Voxelforge.Engines;

namespace Voxelforge.Airbreathing;

/// <summary>
/// Flight conditions an air-breathing engine is designed against.
/// Sized analogue of the rocket-side <c>OperatingConditions</c>.
/// </summary>
/// <param name="Altitude_m">
/// Geometric altitude above mean sea level [m]. Positive into
/// the atmosphere; sea-level is 0. Drives the freestream static T₀,
/// P₀, and ρ₀ via <see cref="StandardAtmosphere"/>.
/// </param>
/// <param name="MachNumber">
/// Freestream Mach number (dimensionless). Drives ram-compression and
/// inlet-recovery. Subsonic + transonic + supersonic ranges all valid;
/// hypersonic (M &gt; 5) is scramjet territory and only valid when
/// <see cref="AirbreathingEngineDesign.Kind"/> is <c>Scramjet</c> or
/// <c>Rbcc</c>.
/// </param>
/// <param name="Fuel">
/// Fuel choice. Today only <see cref="AirbreathingFuel.H2"/> ships
/// with a properties table; the others throw at lookup time until they
/// land in a follow-on sprint.
/// </param>
/// <remarks>
/// All three parameters are required and have no defaults — there is
/// no canonical flight condition the way there is a canonical chamber
/// pressure on the rocket side.
/// </remarks>
public sealed record FlightConditions(
    double Altitude_m,
    double MachNumber,
    AirbreathingFuel Fuel) : IEngineConditions
{
    /// <inheritdoc />
    public string Family => EngineFamilies.Airbreathing;
}
