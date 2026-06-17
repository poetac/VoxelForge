// ChildLangmuirBeamModel.cs — Sprint EP.W2.GIT Child-Langmuir beam-extraction
// physics helper.
//
// Stateless, allocation-free, deterministic implementation of the
// Child-Langmuir beam-extraction model for gridded-ion thrusters per
// Goebel & Katz "Fundamentals of Electric Propulsion: Ion and Hall Thrusters"
// (2008) §5. NSTAR (NASA-JPL, Deep Space 1 / Dawn) calibration anchor.
//
// Physics summary (singly-ionised Xenon, two-grid optics):
//   • Ion-beam current is space-charge limited by the Child-Langmuir law:
//
//       J_CL = (4/9) · ε₀ · √(2 q / m_ion) · V_net^1.5 / d_gap²
//
//     where d_gap is the screen-to-accelerator effective gap.
//
//   • The full extracted current is J_CL · A_open where A_open is the open
//     screen-grid area = N_holes · π · r_aperture². Geometric perveance:
//
//       P = J / V_net^1.5  ≈  N_holes · π · (r_aperture / d_gap)² · K_CL
//
//     with K_CL = (4/9) · ε₀ · √(2 q / m_ion).
//
//   • Beam exit velocity (singly-charged Xe ion, no double-charge correction):
//
//       v_ion = √(2 q V_net / m_ion)
//
//   • Thrust = J_beam · v_ion / (q · Z) — for Z=1 collapses to ṁ · v_ion.
//
//   • Mass flow ṁ = J_beam · m_ion / q (only the ionised fraction reaches the
//     beam). Neutral-leak losses are absorbed into the mass-utilisation
//     efficiency η_m (~0.85–0.92 for NSTAR-class thrusters); modelled as a
//     scalar override knob since perveance physics doesn't fix it directly.
//
//   • Isp_vacuum = v_ion / g₀ — for Xe at V_net=1100 V this gives ≈ 4060 m/s
//     real-ion / 3060 m/s effective with mass-utilisation correction.
//
// NSTAR anchor (Goebel & Katz §5 cluster):
//   V_net ≈ 1100 V, J_beam ≈ 1.76 A, screen-grid radius ≈ 14 cm,
//   grid-gap ~0.6 mm, perveance ≈ 4.9e-8 A/V^1.5 (well below CL saturation).
//   Target: Isp ≈ 3300 s, Thrust ≈ 92 mN, mass flow ≈ 2.6 mg/s.
//
// The five calibration constants (q, m_Xe, ε₀, g₀, K_grid) plus the default
// mass-utilisation efficiency are exposed as public consts so reviewers can
// audit the derivation. Validation tolerance per ADR-029 D4 generalised:
// ±20 % thrust / ±15 % Isp. Tighter than PPT because Child-Langmuir is a
// closed-form physics limit, not a loose empirical fit.

using System;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Output of the Child-Langmuir gridded-ion beam-extraction model. Pure
/// data; no reference to PicoGK or any I/O surface.
/// </summary>
/// <param name="BeamCurrent_A">Extracted ion-beam current [A], post mass-utilisation.</param>
/// <param name="ChildLangmuirLimit_A">Space-charge-limited beam current at V_net [A].</param>
/// <param name="Perveance_AOverV1p5">Geometric perveance P [A / V^1.5].</param>
/// <param name="IonExitVelocity_ms">Singly-charged Xe ion exit velocity [m/s].</param>
/// <param name="Thrust_N">J_beam · v_ion / q [N].</param>
/// <param name="IspVacuum_s">v_eff / g₀ where v_eff = Thrust / ṁ.</param>
/// <param name="MassFlow_kgs">Beam-line mass flow ṁ = J_beam · m_Xe / (η_m · q) [kg/s].</param>
/// <param name="BeamPower_W">V_net · J_beam [W] — useful beam power (excludes neutraliser, screen drain, ionisation).</param>
/// <param name="PlumeDivergenceHalfAngle_rad">Plume half-angle θ [rad] (cluster anchor, geometry-coupled).</param>
/// <param name="Converged">True for the closed-form solve (always converges).</param>
public sealed record ChildLangmuirBeamResult(
    double BeamCurrent_A,
    double ChildLangmuirLimit_A,
    double Perveance_AOverV1p5,
    double IonExitVelocity_ms,
    double Thrust_N,
    double IspVacuum_s,
    double MassFlow_kgs,
    double BeamPower_W,
    double PlumeDivergenceHalfAngle_rad,
    bool   Converged);

/// <summary>
/// Child-Langmuir beam-extraction model for gridded-ion thrusters. Mirror of
/// <see cref="BuschDischargeModel"/> / <see cref="MaeckerKovityaArcModel"/> /
/// <see cref="AblationDischargeModel"/> for the GIT variant.
/// </summary>
public static class ChildLangmuirBeamModel
{
    // ── Physical constants ──────────────────────────────────────────────

    /// <summary>Standard gravity for Isp [m/s²].</summary>
    public const double g0 = 9.80665;

    /// <summary>Elementary charge [C].</summary>
    public const double ElementaryCharge_C = 1.602_176_634e-19;

    /// <summary>Vacuum permittivity ε₀ [F/m].</summary>
    public const double VacuumPermittivity_FperM = 8.854_187_8128e-12;

    /// <summary>
    /// Singly-charged Xenon ion mass [kg]. Atomic mass 131.293 u × 1.66054e-27 kg/u.
    /// </summary>
    public const double XenonIonMass_kg = 131.293 * 1.660_539_066_60e-27;

    // ── Calibration constants (NSTAR cluster anchor) ────────────────────

    /// <summary>
    /// Default beam-extraction mass-utilisation efficiency η_m. NSTAR cluster
    /// 0.85 – 0.92 (Goebel &amp; Katz §5.2); choose mid-band 0.90 as the
    /// default cluster anchor. Set
    /// <see cref="ElectricPropulsionEngineDesign.GitMassUtilizationOverride"/>
    /// to a finite value to override per design.
    /// </summary>
    public const double DefaultMassUtilization = 0.90;

    /// <summary>
    /// Default beam-plume half-angle θ_plume [rad] = 0.349 rad ≈ 20°. NSTAR
    /// cluster value (Goebel &amp; Katz §5.3); plume scales weakly with grid
    /// design at this fidelity and is held as a cluster anchor.
    /// </summary>
    public const double DefaultPlumeHalfAngle_rad = 0.349;

    /// <summary>
    /// Child-Langmuir prefactor K_CL = (4/9) · ε₀ · √(2 q / m_Xe)
    /// [A / (V^1.5 · m²)] — derived constant. Precomputed for clarity.
    /// </summary>
    public static readonly double ChildLangmuirPrefactor
        = (4.0 / 9.0) * VacuumPermittivity_FperM
        * Math.Sqrt(2.0 * ElementaryCharge_C / XenonIonMass_kg);

    /// <summary>
    /// Solve the Child-Langmuir gridded-ion beam-extraction model end-to-end.
    /// </summary>
    /// <param name="beamVoltage_V">V_b [V] — screen-grid bias / net accelerating voltage.</param>
    /// <param name="beamCurrentRequested_A">Design-requested beam current J_b [A].</param>
    /// <param name="screenGridRadius_mm">Outer radius of the active beam area [mm].</param>
    /// <param name="accelGridGap_mm">Screen-to-accelerator effective gap d [mm].</param>
    /// <param name="neutralizerCurrent_A">Neutraliser-cathode emission current [A] (carried through to plasma state for gate).</param>
    /// <param name="massUtilizationOverride">Optional override for η_m; <see cref="double.NaN"/> uses <see cref="DefaultMassUtilization"/>.</param>
    /// <returns>Solved performance state.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any required input is NaN or non-positive, or when the
    /// optional <paramref name="massUtilizationOverride"/> is finite but
    /// outside (0, 1].
    /// </exception>
    public static ChildLangmuirBeamResult Solve(
        double beamVoltage_V,
        double beamCurrentRequested_A,
        double screenGridRadius_mm,
        double accelGridGap_mm,
        double neutralizerCurrent_A,
        double massUtilizationOverride)
    {
        if (double.IsNaN(beamVoltage_V) || beamVoltage_V <= 0)
            throw new ArgumentOutOfRangeException(nameof(beamVoltage_V),
                $"BeamVoltage_V must be positive; got V_b={beamVoltage_V:F1} V.");
        if (double.IsNaN(beamCurrentRequested_A) || beamCurrentRequested_A <= 0)
            throw new ArgumentOutOfRangeException(nameof(beamCurrentRequested_A),
                $"BeamCurrent_A must be positive; got J_b={beamCurrentRequested_A:F3} A.");
        if (double.IsNaN(screenGridRadius_mm) || screenGridRadius_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(screenGridRadius_mm),
                $"ScreenGridRadius_mm must be positive; got R={screenGridRadius_mm:F3} mm.");
        if (double.IsNaN(accelGridGap_mm) || accelGridGap_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(accelGridGap_mm),
                $"AccelGridGap_mm must be positive; got d={accelGridGap_mm:F3} mm.");
        if (double.IsNaN(neutralizerCurrent_A) || neutralizerCurrent_A <= 0)
            throw new ArgumentOutOfRangeException(nameof(neutralizerCurrent_A),
                $"NeutralizerCathodeCurrent_A must be positive; got I_n={neutralizerCurrent_A:F3} A.");
        if (!double.IsNaN(massUtilizationOverride)
            && (massUtilizationOverride <= 0 || massUtilizationOverride > 1.0))
            throw new ArgumentOutOfRangeException(nameof(massUtilizationOverride),
                $"GitMassUtilizationOverride must be NaN or in (0, 1]; got η_m={massUtilizationOverride:F3}.");

        // Resolve mass-utilisation efficiency.
        double eta_m = double.IsNaN(massUtilizationOverride)
            ? DefaultMassUtilization
            : massUtilizationOverride;

        // 1. Geometric perveance + Child-Langmuir saturation current.
        double area_m2 = Math.PI * (screenGridRadius_mm * 1e-3) * (screenGridRadius_mm * 1e-3);
        double gap_m   = accelGridGap_mm * 1e-3;
        double V_3_2 = beamVoltage_V * Math.Sqrt(beamVoltage_V);
        double J_CL_density = ChildLangmuirPrefactor * V_3_2 / (gap_m * gap_m);
        double J_CL = J_CL_density * area_m2;
        // P = J_CL / V_b^1.5 — geometric perveance.
        double perveance = J_CL / V_3_2;

        // 2. Effective beam current. The Child-Langmuir law is a hard upper
        //    bound; a design that requests more than the saturation limit
        //    simply doesn't get more — the surplus is rejected, with the
        //    saturation gate flagging the condition. For the physics solve
        //    proceed at the saturation limit so downstream quantities don't
        //    take a stale design value.
        double J_beam = Math.Min(beamCurrentRequested_A, J_CL);

        // 3. Ion exit velocity from energy conservation (singly-charged Xe).
        double v_ion = Math.Sqrt(2.0 * ElementaryCharge_C * beamVoltage_V / XenonIonMass_kg);

        // 4. Mass flow ṁ = J_beam · m_Xe / (η_m · q). Beam-line current is
        //    only the ionised fraction; total propellant flow is higher by
        //    1/η_m to account for un-ionised neutrals.
        double mDot = J_beam * XenonIonMass_kg / (eta_m * ElementaryCharge_C);

        // 5. Thrust = J_beam · v_ion / q (Newton's second law for the ion
        //    stream). Equivalent to ṁ_beam · v_ion with ṁ_beam = J_beam·m/q.
        double thrust = J_beam * v_ion * XenonIonMass_kg / ElementaryCharge_C;

        // 6. Vacuum Isp = v_eff / g₀ where v_eff = Thrust / ṁ_total.
        //    v_eff = η_m · v_ion (neutral leak dilutes the effective velocity).
        double v_eff = mDot > 0 ? thrust / mDot : 0.0;
        double isp = v_eff / g0;

        // 7. Useful beam power (excludes screen drain, neutraliser, ionisation).
        double P_beam = beamVoltage_V * J_beam;

        return new ChildLangmuirBeamResult(
            BeamCurrent_A:                J_beam,
            ChildLangmuirLimit_A:         J_CL,
            Perveance_AOverV1p5:          perveance,
            IonExitVelocity_ms:           v_ion,
            Thrust_N:                     thrust,
            IspVacuum_s:                  isp,
            MassFlow_kgs:                 mDot,
            BeamPower_W:                  P_beam,
            PlumeDivergenceHalfAngle_rad: DefaultPlumeHalfAngle_rad,
            Converged:                    true);
    }
}
