// HeliconIcrhMagneticNozzleModel.cs — Sprint EP.W4 phase 2
// parameterized 3-stage VASIMR physics.
//
// Stateless, allocation-free, deterministic. Three coupled stages
// model the Ad Astra Rocket VASIMR architecture:
//
//   1. Helicon ionisation. The RF source ionises argon; cluster
//      η_i = min(1, k_helicon · P_helicon · m_Ar / (ṁ · e ·
//      eV_ionization_Ar)). The denominator counts ionisation events
//      per second the helicon must support; the numerator is the
//      available ionisation power. Calibrated so VX-200i (P_h=30 kW,
//      ṁ=100 mg/s) gives η_i ≈ 0.95.
//
//   2. ICRH ion-cyclotron-resonance heating. At ω_RF = q B / m_Ar the
//      RF deposits energy preferentially in the ion population. The
//      energy per ion:
//        E_per_ion_eV = P_icrh · m_Ar / (η_i · ṁ · e²)
//      Bering 2010 reports ICRH heating efficiency near unity in the
//      Ad Astra VX-200 campaign once the cyclotron frequency is tuned;
//      the model adopts ideal ICRH coupling (no detuning factor) at
//      the cluster mid-band.
//
//   3. Magnetic nozzle. The hot plasma flows from the source chamber
//      (B_source) through a magnetic flux-tube expansion to the throat
//      (B_throat). Adiabatic invariance of μ = m v_⊥² / (2B) converts
//      T_⊥ into directed kinetic energy. The conversion efficiency
//      (Chen 2010):
//        η_nozzle = 1 − 1/M  where  M = B_source / B_throat
//      Mirror ratio M depends on B_z and the area expansion (flux
//      conservation: B·A = const → M = A_exit / A_source). For a
//      cylindrical chamber + diverging nozzle, M scales with B_z
//      and R_exit; the model uses
//        M = k_mirror · B_z · R_exit_mm
//      with k_mirror calibrated so VX-200i (B_z=2 T, R_exit=100 mm)
//      gives M = 3.0 → η_nozzle = 0.67.
//
//   Directed exit velocity:
//     v_directed = √(2 · η_nozzle · E_per_ion_eV · e / m_Ar)
//   Thrust:
//     T = η_i · ṁ_total · v_directed
//   Specific impulse:
//     Isp = v_directed / g₀
//
// Variable specific impulse: the VX-200i can shift P_helicon ↔ P_icrh
// at constant total power. Increasing P_icrh raises E_per_ion (and
// hence v_directed, Isp) while reducing η_i (because P_helicon is
// the bottleneck) — net result is lower thrust but much higher Isp.
// The model captures this trade-off through the simple coupling
// between the two stages.
//
// Calibration anchored to VX-200i (Chang Diaz 2009 + Bering 2010):
//   P_helicon = 30 kW, P_icrh = 170 kW, B_z = 2 T, R_exit = 100 mm,
//   ṁ_Ar = 100 mg/s → η_i ≈ 0.95, E_per_ion ≈ 743 eV, M = 3.0,
//   η_nozzle ≈ 0.67, v_directed ≈ 48 870 m/s, T ≈ 4.63 N, Isp ≈ 4982 s.
//   Target was T ≈ 5 N, Isp ≈ 5000 s — both within ±10 % of cluster
//   mid-band.
//
// References:
//   • Chang Diaz F.R., Squire J.P., Glover T.W., et al. (2009). "The
//     VASIMR engine: project status and recent accomplishments."
//     J. Propulsion & Power 25 / IEPC-2009-217.
//   • Bering E.A., Brukardt M., et al. (2010). "Recent improvements
//     in ionization costs and ion-cyclotron heating efficiency in the
//     VASIMR engine." AIAA-2010-6859.
//   • Chen F.F. (2010). "Plasma ionization by helicon waves; magnetic
//     mirror expansion." Plasma Phys. Controlled Fusion 33.
//   • Krieger K. et al. (2005). "Magnetic-nozzle scaling laws." AIAA
//     Joint Propulsion Conference.

using System;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// HeliconIcrhMagneticNozzleModel output. Carried to the VASIMR cycle
/// solver and unwrapped onto the pillar's common
/// <c>ElectricPropulsionResult</c> slots.
/// </summary>
/// <param name="Thrust_N">Effective thrust T [N] = η_i · ṁ · v_directed.</param>
/// <param name="IspVacuum_s">Specific impulse Isp [s] = v_directed / g₀.</param>
/// <param name="ExitVelocity_ms">Directed exit velocity v [m/s].</param>
/// <param name="MassFlow_kgs">Ionised mass flow ṁ_ion [kg/s].</param>
/// <param name="BeamCurrent_A">Equivalent beam current at the nozzle throat [A].</param>
/// <param name="IonTemperature_eV">T_⊥ [eV] set by ICRH heating.</param>
/// <param name="MagneticMirrorRatio">M = B_source / B_throat [-].</param>
/// <param name="IonisationFraction">η_i [-] from the helicon source.</param>
/// <param name="NozzleConversionEfficiency">η_nozzle = 1 − 1/M [-].</param>
/// <param name="PlumeDivergence_rad">Plume half-angle [rad].</param>
/// <param name="Converged">Always true for the closed-form parameterized fit.</param>
public sealed record HeliconIcrhMagneticNozzleResult(
    double Thrust_N,
    double IspVacuum_s,
    double ExitVelocity_ms,
    double MassFlow_kgs,
    double BeamCurrent_A,
    double IonTemperature_eV,
    double MagneticMirrorRatio,
    double IonisationFraction,
    double NozzleConversionEfficiency,
    double PlumeDivergence_rad,
    bool   Converged);

/// <summary>
/// Closed-form parameterized 3-stage VASIMR physics: helicon
/// ionisation → ICRH heating → magnetic-nozzle expansion. Mirrors
/// the HeliconDoubleLayerModel scope for HDLT.
/// </summary>
public static class HeliconIcrhMagneticNozzleModel
{
    /// <summary>Elementary charge [C].</summary>
    internal const double ElementaryCharge_C = 1.602176634e-19;

    /// <summary>Standard gravity [m/s²] — for Isp conversion.</summary>
    internal const double StandardGravity_ms2 = 9.80665;

    /// <summary>Argon atomic mass [kg].</summary>
    internal const double ArgonAtomicMass_kg = 6.6335e-26;

    /// <summary>Argon first-ionisation potential [eV] (NIST data).</summary>
    internal const double ArgonIonisationPotential_eV = 15.76;

    /// <summary>
    /// Helicon ionisation-coupling efficiency [-]. Cluster mid-band 0.10
    /// at VASIMR operating points; calibrated so VX-200i (P_h=30 kW,
    /// ṁ=100 mg/s) gives η_i ≈ 0.95. Above this threshold the helicon
    /// saturates (η_i capped at 1.0).
    /// </summary>
    internal const double HeliconCouplingEfficiency = 0.120;

    /// <summary>
    /// Magnetic-mirror ratio scaling k_mirror [1/(T·mm)]. M = k_mirror
    /// · B_z · R_exit_mm. Calibrated so VX-200i (B_z=2 T, R_exit=100 mm)
    /// gives M = 3.0 → η_nozzle = 0.67.
    /// </summary>
    internal const double MirrorRatioScale_perTmm = 0.015;

    /// <summary>
    /// Plume half-angle [rad] for VASIMR — tight, ~18° = 0.314 rad.
    /// The magnetic nozzle collimates the plume better than HDLT but
    /// not as tightly as gridded ion (FEEP / GIT).
    /// </summary>
    internal const double PlumeHalfAngle_rad = 0.314;

    /// <summary>
    /// Solve the VASIMR cycle for one (P_helicon, P_icrh, B_z, R_exit, ṁ) tuple.
    /// </summary>
    /// <param name="heliconRfPower_W">P_helicon [W]. Cluster envelope 5 000 – 100 000.</param>
    /// <param name="icrhRfPower_W">P_icrh [W]. Cluster envelope 10 000 – 500 000.</param>
    /// <param name="solenoidField_T">B_z [T]. Cluster envelope 0.5 – 5.</param>
    /// <param name="nozzleExitRadius_mm">R_exit [mm]. Cluster envelope 50 – 300.</param>
    /// <param name="argonMassFlow_kgs">ṁ_total [kg/s]. Cluster envelope 1e-5 to 5e-4 (10-500 mg/s).</param>
    /// <exception cref="ArgumentException">
    /// Thrown when any input is non-positive or NaN.
    /// </exception>
    public static HeliconIcrhMagneticNozzleResult Solve(
        double heliconRfPower_W,
        double icrhRfPower_W,
        double solenoidField_T,
        double nozzleExitRadius_mm,
        double argonMassFlow_kgs)
    {
        if (double.IsNaN(heliconRfPower_W) || heliconRfPower_W <= 0.0)
            throw new ArgumentException(
                "heliconRfPower_W must be a positive finite value.",
                nameof(heliconRfPower_W));
        if (double.IsNaN(icrhRfPower_W) || icrhRfPower_W <= 0.0)
            throw new ArgumentException(
                "icrhRfPower_W must be a positive finite value.",
                nameof(icrhRfPower_W));
        if (double.IsNaN(solenoidField_T) || solenoidField_T <= 0.0)
            throw new ArgumentException(
                "solenoidField_T must be a positive finite value.",
                nameof(solenoidField_T));
        if (double.IsNaN(nozzleExitRadius_mm) || nozzleExitRadius_mm <= 0.0)
            throw new ArgumentException(
                "nozzleExitRadius_mm must be a positive finite value.",
                nameof(nozzleExitRadius_mm));
        if (double.IsNaN(argonMassFlow_kgs) || argonMassFlow_kgs <= 0.0)
            throw new ArgumentException(
                "argonMassFlow_kgs must be a positive finite value.",
                nameof(argonMassFlow_kgs));

        // 1. Helicon ionisation fraction η_i. Available ionisation
        //    power / power needed to ionise all argon at the input
        //    flow rate. Cap at 1.0 (full ionisation).
        double ionizationCostPerSecond = argonMassFlow_kgs
                                       * ElementaryCharge_C
                                       * ArgonIonisationPotential_eV
                                       / ArgonAtomicMass_kg;
        double eta_i = Math.Min(1.0,
            HeliconCouplingEfficiency * heliconRfPower_W / ionizationCostPerSecond);

        // 2. ICRH energy per ion. P_icrh distributed over the ionised
        //    flow's particle count.
        //    N_ions_per_sec = η_i · ṁ / m_Ar
        //    E_per_ion_J = P_icrh / N_ions_per_sec
        //    E_per_ion_eV = E_per_ion_J / e
        double mDot_ion = eta_i * argonMassFlow_kgs;
        double N_ions_per_sec = mDot_ion / ArgonAtomicMass_kg;
        double E_per_ion_eV = N_ions_per_sec > 0
            ? icrhRfPower_W / (N_ions_per_sec * ElementaryCharge_C)
            : 0.0;
        // KNOWN LIMITATION (red-team round 2): E_per_ion has no physical ceiling.
        // Energy IS conserved — the jet kinetic power below works out to
        // η_nozzle·P_icrh ≤ P_icrh regardless — but at very low ionisation
        // fraction η_i the whole ICRH power is dumped into very few ions, so
        // E_per_ion (and hence v_directed / Isp) grows without bound while thrust
        // shrinks. A constructed low-η_i / high-P_icrh design therefore reports a
        // physically unrealistic Isp (e.g. >100 000 s) yet stays feasible (only
        // the advisory VASIMR_IONIZATION_FRACTION_LOW fires). A hard ceiling
        // can't be derived from a conservation law (energy already balances); it
        // needs an empirical ion-energy / Isp cap calibrated against VASIMR
        // (VX-200) test data — deferred rather than guessed. Not on an SA
        // objective path (no VASIMR optimiser), so reachable only via direct /
        // CLI / deserialised construction.

        // 3. Magnetic-mirror ratio + nozzle conversion efficiency.
        double M = MirrorRatioScale_perTmm * solenoidField_T * nozzleExitRadius_mm;
        // Cap η_nozzle at 0.95 to prevent unphysical extrapolation at
        // extreme mirror ratios (real nozzles saturate around 0.85-0.95
        // due to plume-detachment losses).
        double eta_nozzle = Math.Max(0.0, Math.Min(0.95, 1.0 - 1.0 / M));

        // 4. Directed exit velocity from thermal-to-kinetic conversion.
        double KE_per_ion_J = eta_nozzle * E_per_ion_eV * ElementaryCharge_C;
        double v_directed = Math.Sqrt(2.0 * KE_per_ion_J / ArgonAtomicMass_kg);

        // 5. Thrust + Isp.
        double thrust = mDot_ion * v_directed;
        double isp = v_directed / StandardGravity_ms2;

        // 6. Equivalent beam current at the nozzle throat.
        double beamCurrent = mDot_ion * ElementaryCharge_C / ArgonAtomicMass_kg;

        return new HeliconIcrhMagneticNozzleResult(
            Thrust_N:                    thrust,
            IspVacuum_s:                 isp,
            ExitVelocity_ms:             v_directed,
            MassFlow_kgs:                mDot_ion,
            BeamCurrent_A:               beamCurrent,
            IonTemperature_eV:           E_per_ion_eV,
            MagneticMirrorRatio:         M,
            IonisationFraction:          eta_i,
            NozzleConversionEfficiency:  eta_nozzle,
            PlumeDivergence_rad:         PlumeHalfAngle_rad,
            Converged:                   true);
    }
}
