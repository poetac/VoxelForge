// AirbreathingFuel.cs — fuel choice for air-breathing engines.
//
// Sprint A1: minimal scaffold (H2 only). H2 was chosen for the ramjet
// MVP because (a) it's already tabulated on the rocket side, (b) it's
// the canonical scramjet fuel, and (c) most academic ramjet textbook
// problems use it (Mattingly Ch. 5 examples). Jet-A / JP-8 land later
// when a real-engine fixture (Lockheed D-21, AIM-9) demands them.

namespace Voxelforge.Airbreathing;

/// <summary>
/// Air-breathing fuel choice. Distinct enum from the rocket-side
/// <c>PropellantPair</c> because air-breathing has only a single fuel
/// stream (the oxidiser is atmospheric air, sourced from
/// <see cref="StandardAtmosphere"/>); a propellant *pair* shape would
/// be an awkward fit.
/// </summary>
/// <remarks>
/// Sprint A1 default: ramjet ships with
/// H2 only. Add Jet-A / JP-8 entries here when the first real-engine
/// fixture requiring them lands; their LHV / stoichiometric f/a /
/// reference enthalpy go on the corresponding fuel-properties record.
/// </remarks>
public enum AirbreathingFuel
{
    /// <summary>
    /// Hydrogen (gaseous H₂). LHV ≈ 119.96 MJ/kg; stoichiometric
    /// f/a ≈ 0.0291. Sprint A1 default.
    /// </summary>
    H2 = 0,

    /// <summary>
    /// Jet-A / Jet-A1 kerosene. LHV ≈ 43.15 MJ/kg; stoichiometric
    /// f/a ≈ 0.0680. Reserved — properties land alongside the first
    /// fixture that needs them.
    /// </summary>
    JetA = 1,

    /// <summary>
    /// JP-8 military kerosene. LHV ≈ 42.8 MJ/kg; stoichiometric
    /// f/a ≈ 0.0676. Reserved.
    /// </summary>
    Jp8 = 2,
}
