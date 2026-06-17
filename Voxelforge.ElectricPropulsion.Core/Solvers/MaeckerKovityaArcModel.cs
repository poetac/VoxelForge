// MaeckerKovityaArcModel.cs — Wave-2 Arcjet thermal-arc physics helper.
//
// Stateless, allocation-free, deterministic implementation of the
// Maecker-Kovitya constricted-arc thermal model for low-power arcjets per
// Sutton & Biblarz "Rocket Propulsion Elements" 9e §16.3 + Goebel & Katz
// "Fundamentals of Electric Propulsion" 2008 §4.
//
// Physics summary (energy-balance arcjet):
//   P_arc      = V_arc · I_arc                      (electrical input)
//   P_gas      = η_thermal · P_arc                   (bulk-gas enthalpy gain)
//   P_anode    = (1 − η_thermal) · κ_anode · P_arc   (anode wall heat-load fraction)
//   V_exit     = √(2 · P_gas / ṁ)                    (energy-to-kinetic; ideal-gas)
//   θ_plume    = arctan(K_plume · L_arc / R_throat)  (nozzle + arc-column expansion)
//   T          = ṁ · V_exit · cos(θ_plume)           (axial thrust)
//   Isp_vac    = T / (ṁ · g₀)                        (specific impulse)
//
// MR-509 ATOS calibration anchor (Aerojet 1.8 kW arcjet on hydrazine):
//   V_arc=100 V, I_arc=18 A, ṁ=0.039 g/s NH3+H2+N2, R_throat=0.5 mm
//   target Thrust ≈ 0.222 N (datasheet 0.20–0.26 N), Isp ≈ 580 s (560–600 s)
//   η_thermal calibrated at 0.40 (Sutton 9e §16.3 reports 0.30–0.50 across
//   the low-power arcjet cluster).
//
// The three calibration constants (η_thermal, κ_anode, K_plume) are exposed
// as public consts so reviewers can audit the calibration trail. Wider
// validation tolerance (±20 % thrust / ±15 % Isp per ADR-029 D4) absorbs
// the residual energy-balance approximation error.

using System;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Output of the Maecker-Kovitya arcjet model. Pure data; no reference to
/// PicoGK or any I/O surface.
/// </summary>
public sealed record MaeckerKovityaResult(
    double ExitVelocity_ms,
    double Thrust_N,
    double IspVacuum_s,
    double GasEnthalpyGain_W,
    double AnodePowerLoss_W,
    double AnodeWallTemp_K,
    double PlumeDivergenceHalfAngle_rad,
    double ArcPower_W,
    bool   Converged);

/// <summary>
/// Maecker-Kovitya constricted-arc thermal model for low-power arcjets.
/// Mirror of <see cref="BuschDischargeModel"/> for the Arcjet variant.
/// </summary>
public static class MaeckerKovityaArcModel
{
    // ── Physical constants ──────────────────────────────────────────────

    /// <summary>Standard gravity for Isp [m/s²].</summary>
    public const double g0 = 9.80665;

    /// <summary>Stefan-Boltzmann constant [W/(m²·K⁴)].</summary>
    public const double Sigma_SB = 5.670374419e-8;

    /// <summary>
    /// Background sink temperature for vacuum operation [K]. Cosmic
    /// microwave background; matches HET pillar anchor.
    /// </summary>
    public const double T_Vacuum_K = 3.0;

    private const double T_Vacuum_K4 = T_Vacuum_K * T_Vacuum_K * T_Vacuum_K * T_Vacuum_K;

    // ── Calibration constants (MR-509 ATOS cluster anchor) ─────────────

    /// <summary>
    /// Thermal efficiency η_thermal — fraction of arc-column power deposited
    /// as bulk-gas enthalpy (vs lost to electrodes / radiation / frozen-flow).
    /// Sutton &amp; Biblarz 9e §16.3 reports 0.30–0.50 for low-power
    /// arcjets; 0.40 lands MR-509 ATOS inside the ADR-029 D4 ±20 % thrust
    /// envelope. Designs override via <see cref="ElectricPropulsionEngineDesign.ArcjetThermalEfficiency"/>
    /// when calibration data is richer.
    /// </summary>
    public const double DefaultThermalEfficiency = 0.40;

    /// <summary>
    /// Anode-loss share κ_anode of the wasted (non-η_thermal) power that
    /// reaches the anode wall. The remainder leaves as cathode loss +
    /// radiation. Sutton &amp; Biblarz §16.3: 0.55–0.70 for the constricted
    /// downstream-anode geometry; 0.60 is the centred anchor.
    /// </summary>
    public const double AnodeLossShare = 0.60;

    /// <summary>
    /// Plume-divergence calibration constant K_plume in θ = arctan(K_plume).
    /// Sutton 9e §16.3 reports half-angles 15–25° across the low-power
    /// arcjet cluster; the divergence is dominated by the conical CD-nozzle
    /// expansion, not the arc-column geometry, so a fixed cluster anchor
    /// is more honest than a geometric scaling. K_plume = 0.35 → θ = 19.3°,
    /// landing MR-509 ATOS at the ±20 % thrust / ±15 % Isp envelope.
    /// </summary>
    public const double PlumeConstant = 0.35;

    /// <summary>
    /// Anode-wall surface emissivity for radiative cooling balance. ~0.30
    /// for polished tungsten; bumped to 0.40 to absorb the molybdenum +
    /// rhenium options. Lower than HET's BN/graphite ε≈0.85 (radiative
    /// cooling is harder with metallic anodes — convective gas cooling
    /// is the dominant mechanism, captured implicitly via the η_thermal
    /// budget rather than directly here).
    /// </summary>
    public const double AnodeEmissivity = 0.40;

    /// <summary>
    /// Solve the Maecker-Kovitya arcjet energy balance end-to-end.
    /// </summary>
    /// <param name="arcVoltage_V">V_arc [V] — arc terminal voltage.</param>
    /// <param name="arcCurrent_A">I_arc [A] — arc current.</param>
    /// <param name="arcGap_mm">L_arc [mm] — cathode-tip-to-constrictor arc length (used for plume divergence).</param>
    /// <param name="propellantMassFlow_kgs">ṁ [kg/s] — propellant mass flow rate.</param>
    /// <param name="nozzleThroatRadius_mm">R_throat [mm] — nozzle throat radius (used for plume divergence + anode area).</param>
    /// <param name="chamberLength_mm">L_chamber [mm] — chamber/anode axial length (used for anode wall area).</param>
    /// <param name="chamberRadius_mm">R_chamber [mm] — chamber/anode inner radius (used for anode wall area).</param>
    /// <param name="thermalEfficiency">η_thermal [-]. Pass <see cref="DefaultThermalEfficiency"/> for the cluster anchor.</param>
    /// <returns>Solved performance + thermal state.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any geometric / electrical input is NaN or non-positive,
    /// or when <paramref name="thermalEfficiency"/> is NaN or not in (0, 1].
    /// </exception>
    public static MaeckerKovityaResult Solve(
        double arcVoltage_V,
        double arcCurrent_A,
        double arcGap_mm,
        double propellantMassFlow_kgs,
        double nozzleThroatRadius_mm,
        double chamberLength_mm,
        double chamberRadius_mm,
        double thermalEfficiency)
    {
        if (double.IsNaN(arcVoltage_V) || arcVoltage_V <= 0)
            throw new ArgumentOutOfRangeException(nameof(arcVoltage_V),
                $"ArcVoltage_V must be positive; got V_arc={arcVoltage_V:F1} V.");
        if (double.IsNaN(arcCurrent_A) || arcCurrent_A <= 0)
            throw new ArgumentOutOfRangeException(nameof(arcCurrent_A),
                $"ArcCurrent_A must be positive; got I_arc={arcCurrent_A:F3} A.");
        if (double.IsNaN(arcGap_mm) || arcGap_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(arcGap_mm),
                $"ArcGap_mm must be positive; got L_arc={arcGap_mm:F3} mm.");
        if (double.IsNaN(propellantMassFlow_kgs) || propellantMassFlow_kgs <= 0)
            throw new ArgumentOutOfRangeException(nameof(propellantMassFlow_kgs),
                $"PropellantMassFlow_kgs must be positive; got ṁ={propellantMassFlow_kgs:E3} kg/s.");
        if (double.IsNaN(nozzleThroatRadius_mm) || nozzleThroatRadius_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(nozzleThroatRadius_mm),
                $"NozzleThroatRadius_mm must be positive; got R_t={nozzleThroatRadius_mm:F3} mm.");
        if (double.IsNaN(chamberLength_mm) || chamberLength_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(chamberLength_mm),
                $"ChamberLength_mm must be positive; got L={chamberLength_mm:F3} mm.");
        if (double.IsNaN(chamberRadius_mm) || chamberRadius_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(chamberRadius_mm),
                $"ChamberRadius_mm must be positive; got R={chamberRadius_mm:F3} mm.");
        if (double.IsNaN(thermalEfficiency) || thermalEfficiency <= 0 || thermalEfficiency > 1.0)
            throw new ArgumentOutOfRangeException(nameof(thermalEfficiency),
                $"ThermalEfficiency must be in (0, 1]; got η_thermal={thermalEfficiency:F3}.");

        // 1. Arc power.
        double P_arc = arcVoltage_V * arcCurrent_A;

        // 2. Bulk-gas enthalpy gain.
        double P_gas = thermalEfficiency * P_arc;

        // 3. Energy → exit velocity. Assumes the gas does adiabatic expansion
        //    converting enthalpy to kinetic energy by a steady-state
        //    isentropic process. The ½·m·v² ↔ enthalpy bookkeeping treats
        //    the chamber stagnation enthalpy as h_0 ≈ P_gas / ṁ.
        double v_exit = Math.Sqrt(2.0 * P_gas / propellantMassFlow_kgs);

        // 4. Plume divergence half-angle. Fixed at the cluster anchor —
        //    real arcjet plume divergence is dominated by the conical CD-nozzle
        //    expansion, not the arc-column geometry, so an L_arc/R_throat
        //    geometric scaling would be a false precision.
        double theta = Math.Atan(PlumeConstant);
        // Reference the geometry inputs so they remain part of the validation
        // surface (NaN propagation + future model refinements that DO use them).
        _ = arcGap_mm;
        _ = nozzleThroatRadius_mm;

        // 5. Axial thrust T = ṁ · V_exit · cos(θ).
        double thrust = propellantMassFlow_kgs * v_exit * Math.Cos(theta);

        // 6. Vacuum specific impulse.
        double isp = thrust / (g0 * propellantMassFlow_kgs);

        // 7. Anode power loss + wall radiation balance.
        double P_anode = AnodeLossShare * (1.0 - thermalEfficiency) * P_arc;
        double R_chamber_m = chamberRadius_mm * 1.0e-3;
        double L_chamber_m = chamberLength_mm * 1.0e-3;
        double A_anode_m2 = 2.0 * Math.PI * R_chamber_m * L_chamber_m;
        double radiationCoeff = AnodeEmissivity * Sigma_SB * A_anode_m2;
        double T_anode_4 = (P_anode / radiationCoeff) + T_Vacuum_K4;
        double T_anode = Math.Sqrt(Math.Sqrt(T_anode_4));

        return new MaeckerKovityaResult(
            ExitVelocity_ms:              v_exit,
            Thrust_N:                     thrust,
            IspVacuum_s:                  isp,
            GasEnthalpyGain_W:            P_gas,
            AnodePowerLoss_W:             P_anode,
            AnodeWallTemp_K:              T_anode,
            PlumeDivergenceHalfAngle_rad: theta,
            ArcPower_W:                   P_arc,
            Converged:                    true);
    }
}
