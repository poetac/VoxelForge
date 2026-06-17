// StationMap.cs — station-by-station thermodynamic state for an
// air-breathing cycle.
//
// Standard turbomachinery station numbering per Mattingly *Elements
// of Propulsion: Gas Turbines and Rockets*, AIAA 2006, §1.6 and
// SAE AS755:
//
//   0  freestream
//   1  inlet face (engine intake plane)
//   2  compressor face / diffuser exit
//   3  combustor inlet
//   4  combustor exit / turbine inlet
//   5  turbine exit
//   6  afterburner inlet  (or, for a turbofan, mixer exit)
//   7  afterburner exit (if any)
//   8  nozzle throat
//   9  nozzle exit
//
// Turbofan-extended slots (Sprint A8 onward, single-spool low-bypass):
//
//   13  fan exit (cold path, post-fan / pre-splitter)
//   16  bypass duct exit (cold path, mixer entry)
//
// Ramjets skip stations 2 (no compressor) and 5 (no turbine) —
// freestream → inlet → diffuser → combustor → nozzle. Turbojets use
// the full chain through 0-9. Turbofans additionally populate
// stations 13 + 16 (cold path) and station 6 (mixer exit replacing
// the unused afterburner-inlet slot).
//
// Per Sprint A1, the StationState array is sized to 10 entries
// (indices 0-9) for ramjet / turbojet. Sprint A8 extends the array
// to 17 entries (indices 0-16) for turbofan so stations 13 + 16
// become valid indices. The <c>Station(int)</c> accessor uses
// <c>Stations.Count</c> for bounds-checking, so the variable length
// is API-compatible — existing ramjet / turbojet consumers continue
// to read indices 0-9 against the longer array unchanged.

using System;
using System.Collections.Generic;
using Voxelforge.Engines;

namespace Voxelforge.Airbreathing.Stations;

/// <summary>
/// One station's thermodynamic state. Stagnation properties are the
/// canonical cycle-analysis quantities; static properties are derived
/// where the cycle solver has the local Mach number.
/// </summary>
/// <param name="StagnationT_K">Stagnation (total) temperature [K].</param>
/// <param name="StagnationP_Pa">Stagnation (total) pressure [Pa].</param>
/// <param name="MassFlow_kg_s">Mass flow through the station [kg/s].</param>
/// <param name="MachNumber">
/// Local Mach number at the station's reference plane. <see cref="double.NaN"/>
/// when the station is degenerate (e.g. a station the cycle skips).
/// </param>
public readonly record struct StationState(
    double StagnationT_K,
    double StagnationP_Pa,
    double MassFlow_kg_s,
    double MachNumber) : IThermodynamicState
{
    // R_air = 287.05 J/(kg·K). Used for ideal-gas density.
    private const double R_air = 287.05;

    double IThermodynamicState.Temperature_K => StagnationT_K;
    double IThermodynamicState.Pressure_Pa   => StagnationP_Pa;

    double IThermodynamicState.Enthalpy_Jkg
        => double.IsNaN(StagnationT_K)
            ? double.NaN
            : Thermo.IdealGasAir.EnthalpyAir(StagnationT_K);

    double IThermodynamicState.Entropy_JkgK  => 0.0;  // deferred — no cp(T) entropy table

    double IThermodynamicState.Density_kgm3
        => double.IsNaN(StagnationT_K) || double.IsNaN(StagnationP_Pa) || StagnationT_K <= 0
            ? 0.0
            : StagnationP_Pa / (R_air * StagnationT_K);
}

/// <summary>
/// Station-by-station thermodynamic map for one cycle solve. Indexed
/// per the SAE AS755 convention. Unused stations carry
/// <see cref="StationState.MassFlow_kg_s"/> = 0 and other fields NaN.
/// </summary>
/// <param name="Stations">
/// Per-station array. Length 10 for ramjet / turbojet (indices 0-9);
/// length 17 for turbofan (indices 0-16, where 13 = fan exit and
/// 16 = bypass duct exit). Index 0 = freestream; index 9 = nozzle exit
/// in every case.
/// </param>
/// <param name="ThrustNet_N">
/// Net thrust [N]. <c>F_net = (ṁ + ṁ_f)·V_e − ṁ·V_∞ + (P_e − P_∞)·A_e</c>.
/// </param>
/// <param name="SpecificImpulse_s">
/// Fuel specific impulse [s]. <c>Isp = F_net / (ṁ_fuel · g₀)</c>.
/// Reported per kg of fuel burned (the conventional air-breathing
/// definition); contrast with rocket Isp which is per kg of total
/// propellant.
/// </param>
/// <param name="FuelMassFlow_kg_s">Fuel mass flow [kg/s].</param>
public sealed record StationMap(
    IReadOnlyList<StationState> Stations,
    double ThrustNet_N,
    double SpecificImpulse_s,
    double FuelMassFlow_kg_s)
{
    /// <summary>
    /// Convenience accessor for one station. Throws on out-of-range
    /// (the SAE convention is 0-9, so any index outside that is a bug
    /// at the call site, not a recoverable condition).
    /// </summary>
    public StationState Station(int index)
    {
        if (Stations is null) throw new InvalidOperationException("Stations array is null.");
        if (index < 0 || index >= Stations.Count)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Station index {index} out of range [0, {Stations.Count - 1}].");
        return Stations[index];
    }
}

public static class StationListExtensions
{
    public static StationState Station(this IReadOnlyList<StationState> stations, int index)
    {
        if (stations is null) throw new ArgumentNullException(nameof(stations));
        if (index < 0 || index >= stations.Count)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Station index {index} out of range [0, {stations.Count - 1}].");
        return stations[index];
    }
}
