// AirbreathingFuelTables.cs — fuel-property lookup for the
// air-breathing pillar.
//
// Sprint A3 ships H2 only (the Sprint A1 default fuel choice). Jet-A
// + JP-8 throw at lookup time until they're populated (when the first
// real-engine fixture demanding them lands).
//
// Why not reuse rocket-side IPropellantTable / PropellantPair
// -----------------------------------------------------------
// Rocket-side fuel data is shaped around a propellant *pair* (oxidiser
// + fuel) because the rocket combustor sees both. Air-breathing
// engines see a single fuel stream against atmospheric air, so the
// "pair" concept is an awkward fit. Per the parallel-pillar design,
// the air-breathing side defines its own minimal
// fuel record.

using System;

namespace Voxelforge.Airbreathing.Thermo;

/// <summary>
/// Fuel-side thermodynamic properties at reference conditions (298 K,
/// 1 atm liquid for cryogens, gaseous for room-temp species).
/// </summary>
/// <param name="LowerHeatingValue_J_kg">
/// Lower heating value (LHV) — energy released per kg fuel burned to
/// gaseous water + CO₂ products. Used in the combustor energy balance:
/// <c>f = (h_t4 − h_t3) / (η_b · LHV − h_t4)</c>.
/// </param>
/// <param name="StoichiometricFuelAirRatio">
/// Stoichiometric fuel-air mass ratio f_st. Equivalence ratio φ
/// relates as <c>φ = (f / ṁ_a) / f_st</c>.
/// </param>
/// <param name="FormulaWeight_kg_kmol">
/// Molecular weight of the fuel [kg/kmol]. Carried for downstream
/// combustion-product mole-fraction calcs (post-A3 follow-on).
/// </param>
public sealed record AirbreathingFuelProperties(
    double LowerHeatingValue_J_kg,
    double StoichiometricFuelAirRatio,
    double FormulaWeight_kg_kmol);

/// <summary>
/// Static lookup from <see cref="AirbreathingFuel"/> to the fuel's
/// reference properties.
/// </summary>
public static class AirbreathingFuelTables
{
    /// <summary>
    /// Reference-condition properties for the requested fuel. Throws
    /// <see cref="NotSupportedException"/> when the fuel has no
    /// populated table yet (Sprint A3 ships H2 only).
    /// </summary>
    public static AirbreathingFuelProperties Lookup(AirbreathingFuel fuel)
    {
        return fuel switch
        {
            AirbreathingFuel.H2 => Hydrogen,
            AirbreathingFuel.JetA => JetA,
            AirbreathingFuel.Jp8 => Jp8,
            _ => throw new ArgumentOutOfRangeException(nameof(fuel),
                $"Unknown fuel '{fuel}'. Add a case here when extending the enum."),
        };
    }

    /// <summary>
    /// Hydrogen (H₂). LHV 119.96 MJ/kg per NIST WebBook standard
    /// reference state (298 K, 1 atm); stoichiometric f/a 0.0291
    /// from H₂ + ½O₂ → H₂O, with air at 23.2 % O₂ by mass.
    /// </summary>
    public static readonly AirbreathingFuelProperties Hydrogen = new(
        LowerHeatingValue_J_kg:        119_960_000.0,
        StoichiometricFuelAirRatio:    0.0291,
        FormulaWeight_kg_kmol:         2.01588);

    /// <summary>
    /// Jet-A / Jet-A1 kerosene. LHV 43.15 MJ/kg + stoichiometric
    /// f/a 0.0680 per ASTM D1655 specification + Mattingly Appendix B
    /// (representative formula C_12 H_24, MW ≈ 168 kg/kmol).
    /// </summary>
    public static readonly AirbreathingFuelProperties JetA = new(
        LowerHeatingValue_J_kg:        43_150_000.0,
        StoichiometricFuelAirRatio:    0.0680,
        FormulaWeight_kg_kmol:         168.0);

    /// <summary>
    /// JP-8 military kerosene. LHV 42.80 MJ/kg + stoichiometric f/a
    /// 0.0676 per MIL-DTL-83133 + USAF reference. Representative
    /// formula C_11 H_21, MW ≈ 153 kg/kmol.
    /// </summary>
    public static readonly AirbreathingFuelProperties Jp8 = new(
        LowerHeatingValue_J_kg:        42_800_000.0,
        StoichiometricFuelAirRatio:    0.0676,
        FormulaWeight_kg_kmol:         153.0);
}
