// HydrogenStorageSolver.cs — Sprint H2T.W1 closed-form hydrogen-storage
// tank performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes stored mass,
// stored energy, gravimetric + volumetric efficiency, and (for cryo
// only) boil-off rate, for either a compressed-gas or cryogenic-liquid
// tank at a specified (P, T, V) operating point.
//
// CompressedGas mode — real-gas density via H₂ compressibility factor:
//   ρ_H₂ = P · M / (Z · R · T)
//   Z(P, T) for H₂ is non-trivial; we use a cluster-anchored linear
//   fit Z ≈ 1.0 + 6.0e-4 · P[bar] valid for 0 < P ≤ 1000 bar at near-
//   ambient T (NIST REFPROP cluster).
//
// LiquidCryogenic mode — fixed liquid density anchored to LH₂ at NBP:
//   ρ_LH₂ ≈ 70.85 kg/m³ at 20.3 K, 1 atm.
//   Boil-off: dm/dt = Q_leak / h_fg, h_fg(LH₂) = 446 kJ/kg.
//
// References:
//   NIST WebBook — hydrogen real-gas tables.
//   DOE Hydrogen Storage Technical Targets (2025): ≥ 6.5 % gravimetric
//     for vehicle on-board storage.
//   Toyota Mirai 2nd-gen — 700 bar Type-IV tanks, 5.6 kg H₂ across
//     three tanks (~ 142 L total).

using System;

namespace Voxelforge.HydrogenStorage;

/// <summary>
/// Closed-form hydrogen storage tank performance snapshot solver
/// (Sprint H2T.W1).
/// </summary>
internal static class HydrogenStorageSolver
{
    /// <summary>Universal gas constant [J/(mol·K)].</summary>
    internal const double R_J_molK = 8.31446;

    /// <summary>Molar mass of H₂ [kg/mol].</summary>
    internal const double MolarMassH2_kg_mol = 2.016e-3;

    /// <summary>
    /// Cluster-anchored compressibility-factor slope dZ/dP [1/bar] for
    /// H₂ at near-ambient T (15-40 °C). Valid 0 ≤ P ≤ 1000 bar.
    /// At P = 700 bar → Z ≈ 1.42 (matches NIST cluster mid-band).
    /// </summary>
    internal const double H2_CompressibilityFactorSlope_perBar = 6.0e-4;

    /// <summary>LH₂ density at normal boiling point (20.3 K, 1 atm) [kg/m³].</summary>
    internal const double Lh2Density_kgm3 = 70.85;

    /// <summary>LH₂ enthalpy of vaporisation [J/kg]. Used for boil-off mass-rate.</summary>
    internal const double Lh2EnthalpyOfVaporisation_J_kg = 446_000.0;

    /// <summary>
    /// Sprint H2T.W2. Metal-hydride effective ρ_H₂ [kg/m³]. Cluster
    /// mid-band across LaNi₅, MgH₂, and Mg₂Ni at saturation (DOE
    /// Hydrogen Storage Technical Targets 2025).
    /// </summary>
    internal const double MetalHydrideEffectiveDensity_kgm3 = 100.0;

    /// <summary>H₂ lower heating value [kWh/kg]. Standard energy-content reference.</summary>
    internal const double H2_Lhv_kWh_kg = 33.3;

    /// <summary>
    /// Solve the hydrogen storage tank performance snapshot at the
    /// design operating point.
    /// </summary>
    internal static HydrogenStorageResult Solve(HydrogenStorageDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        // 1. H₂ density per mode.
        double rho_H2;
        switch (design.Kind)
        {
            case HydrogenStorageKind.CompressedGas:
                double P_Pa = design.OperatingPressure_bar * 1e5;
                double Z = 1.0 + H2_CompressibilityFactorSlope_perBar
                              * design.OperatingPressure_bar;
                rho_H2 = P_Pa * MolarMassH2_kg_mol
                       / (Z * R_J_molK * design.OperatingTemperature_K);
                break;

            case HydrogenStorageKind.LiquidCryogenic:
                rho_H2 = Lh2Density_kgm3;
                break;

            case HydrogenStorageKind.MetalHydride:
                // Sprint H2T.W2. Effective ρ_H₂ ≈ 100 kg/m³ across the
                // LaNi₅ / MgH₂ / Mg₂Ni cluster (NIST WebBook + DOE H₂
                // storage technical targets). The metal-lattice
                // chemisorption locks H₂ at higher density than LH₂.
                rho_H2 = MetalHydrideEffectiveDensity_kgm3;
                break;

            default:
                throw new InvalidOperationException(
                    $"Unhandled HydrogenStorageKind '{design.Kind}'.");
        }

        // 2. Stored mass + energy.
        double m_H2 = rho_H2 * design.InternalVolume_m3;
        double E_stored_kWh = m_H2 * H2_Lhv_kWh_kg;

        // 3. Gravimetric + volumetric efficiency.
        double m_total = m_H2 + design.DryMass_kg;
        double gravimetricEff = m_total > 0 ? m_H2 / m_total : 0.0;
        // 1 m³ = 1000 L → divide by 1000 to convert m³ → L.
        double volumetricEnergyDensity_kWh_L = design.InternalVolume_m3 > 0
            ? E_stored_kWh / (design.InternalVolume_m3 * 1000.0)
            : 0.0;

        // 4. Boil-off — cryogenic only.
        double boilOffRate_kgs = design.Kind == HydrogenStorageKind.LiquidCryogenic
            ? design.HeatLeakRate_W / Lh2EnthalpyOfVaporisation_J_kg
            : 0.0;

        return new HydrogenStorageResult(
            HydrogenDensity_kgm3:           rho_H2,
            StoredHydrogenMass_kg:          m_H2,
            StoredHydrogenEnergy_kWh:       E_stored_kWh,
            GravimetricEfficiency:          gravimetricEff,
            VolumetricEnergyDensity_kWh_L:  volumetricEnergyDensity_kWh_L,
            BoilOffRate_kgs:                boilOffRate_kgs);
    }
}
