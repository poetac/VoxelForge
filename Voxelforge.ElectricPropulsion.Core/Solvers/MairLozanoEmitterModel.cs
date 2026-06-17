// MairLozanoEmitterModel.cs — Sprint EP.W5 phase 2 closed-form
// Field-Emission Electric Propulsion (FEEP) emitter physics.
//
// Stateless, allocation-free, deterministic. Single-component Mair-
// Lozano model: a liquid-metal beam emitted from a sharp tip is
// treated as a single effective ion species with cluster-calibrated
// mass m_eff = γ · m_ion_pure. The kinematic relations follow directly
// from energy conservation against m_eff and charge conservation
// against e.
//
// Physics:
//   1. Tip field: E_tip = α · V_acc / r_tip, α ≈ 0.5 for sharp
//      tungsten cones (Forbes 1999 geometry-factor cluster).
//   2. Fowler-Nordheim threshold: real emitters turn on near
//      E_tip ≈ 10⁹ V/m for typical work functions. Below the
//      threshold the model still computes a beam (the gate flags it
//      as below-threshold advisory).
//   3. Effective exit velocity (energy conservation):
//        v_eff = √(2 · e · V_acc / m_eff)
//      with m_eff = γ · m_ion_pure (γ = cluster factor per propellant).
//   4. Mass flow (charge conservation):
//        ṁ = I_beam · m_eff / e
//   5. Thrust:
//        T = ṁ · v_eff = I_beam · √(2 · m_eff · V_acc / e)
//   6. Specific impulse: Isp = v_eff / g₀.
//
// Cluster-factor calibration γ per propellant:
//   • Indium γ = 47 — calibrated to IFM Nano cluster anchor
//     (V_acc = 9 kV, I_beam = 100 μA, r_tip = 5 μm → T ≈ 100 μN).
//     Real Indium-FEEP beams are polydisperse (In⁺ ions + multi-charge
//     clusters + droplets); γ = 47 lumps the population into a single
//     effective ion. Resulting kinematic Isp ≈ 1835 s — substantially
//     below the "effective Isp" of 4000-6000 s sometimes cited for
//     IFM Nano. The marketing Isp reflects propellant-utilisation-
//     corrected metrics from a two-population beam model (deferred
//     to a future Wave-4 refinement; tracked in [#503] follow-on).
//   • Cesium γ = 5 — calibrated to NanoFEEP-class anchors. Cs has
//     lower surface tension than In and produces less droplet content,
//     so the effective cluster size is smaller and the kinematic Isp
//     lands closer to the marketing figure.
//
// References:
//   • Mair G., Genovese A., Tajmar M. (1996–2010). Indium-FEEP
//     development series. TUI / Austrian Research Centers.
//   • Marcuccio S., Genovese A., Andrenucci M. (1997). "FEEP scaling
//     laws." J. Propulsion & Power 13(5), pp. 581–590.
//   • Fowler R.H., Nordheim L. (1928). "Electron emission in intense
//     electric fields." Proc. Roy. Soc. A 119, pp. 173–181.
//   • Forbes R.G. (1999). "Refining the application of Fowler-Nordheim
//     theory." J. Vac. Sci. Tech. B 17(2).

using System;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Mair-Lozano emitter-model output. Carried to the FEEP cycle solver
/// and unwrapped onto the pillar's common <c>ElectricPropulsionResult</c>
/// slots.
/// </summary>
/// <param name="Thrust_N">Effective thrust T [N] = ṁ · v_eff.</param>
/// <param name="IspVacuum_s">Specific impulse Isp [s] = v_eff / g₀.</param>
/// <param name="ExitVelocity_ms">Kinematic exit velocity v_eff [m/s].</param>
/// <param name="MassFlow_kgs">Effective ṁ [kg/s] = I_beam · m_eff / e.</param>
/// <param name="BeamCurrent_A">Beam current I_beam [A] (the input).</param>
/// <param name="EmitterTipField_VperM">Local tip field E_tip [V/m] = α · V_acc / r_tip.</param>
/// <param name="EffectiveIonMass_kg">Calibrated m_eff [kg] = γ · m_ion_pure.</param>
/// <param name="PlumeDivergence_rad">Plume half-angle θ [rad]. FEEP narrow ~15°.</param>
/// <param name="Converged">Always true for the closed-form model; reserved for parity with iterative solvers.</param>
public sealed record MairLozanoEmitterResult(
    double Thrust_N,
    double IspVacuum_s,
    double ExitVelocity_ms,
    double MassFlow_kgs,
    double BeamCurrent_A,
    double EmitterTipField_VperM,
    double EffectiveIonMass_kg,
    double PlumeDivergence_rad,
    bool   Converged);

/// <summary>
/// Closed-form Mair-Lozano single-component FEEP emitter model.
/// </summary>
public static class MairLozanoEmitterModel
{
    /// <summary>Elementary charge [C].</summary>
    internal const double ElementaryCharge_C = 1.602176634e-19;

    /// <summary>Standard gravity [m/s²] — for Isp conversion.</summary>
    internal const double StandardGravity_ms2 = 9.80665;

    /// <summary>
    /// Geometry factor α for a sharp tungsten emitter cone: E_tip =
    /// α · V_acc / r_tip. Cluster mid-band 0.4–0.6 (Forbes 1999); the
    /// model uses α = 0.5 as the canonical value.
    /// </summary>
    internal const double TipGeometryFactor = 0.5;

    /// <summary>
    /// Indium atomic mass [kg]. Singly-charged In⁺ ion mass is
    /// approximately the atomic mass (m_electron is negligible).
    /// </summary>
    internal const double IndiumAtomicMass_kg = 1.9063e-25;

    /// <summary>
    /// Cesium atomic mass [kg].
    /// </summary>
    internal const double CesiumAtomicMass_kg = 2.2069e-25;

    /// <summary>
    /// Indium beam-composition cluster factor γ_In. Calibrated to the
    /// IFM Nano cluster anchor (V_acc = 9 kV, I_beam = 100 μA, r_tip =
    /// 5 μm → T = 100 μN). Lumps the polydisperse beam (ions + multi-
    /// charge clusters + droplets) into a single effective ion mass
    /// m_eff = γ_In · m_In.
    /// </summary>
    internal const double IndiumClusterFactor = 47.0;

    /// <summary>
    /// Cesium beam-composition cluster factor γ_Cs. Smaller than
    /// Indium's because Cs has lower surface tension and produces less
    /// droplet content. Calibrated to NanoFEEP-class data; the model
    /// uses γ_Cs = 5 as the cluster mid-band.
    /// </summary>
    internal const double CesiumClusterFactor = 5.0;

    /// <summary>
    /// Plume half-angle [rad] for FEEP — narrow, ~ 15° = 0.262 rad.
    /// Extractor electrode acts as a focusing element; tighter than
    /// HET/MPD plumes.
    /// </summary>
    internal const double PlumeHalfAngle_rad = 0.262;

    /// <summary>
    /// Solve the Mair-Lozano single-component FEEP emitter model.
    /// </summary>
    /// <param name="acceleratingVoltage_V">V_acc [V]. Cluster envelope 5 000 – 12 000.</param>
    /// <param name="beamCurrent_A">I_beam [A]. Cluster envelope 10 μA – 1 mA.</param>
    /// <param name="emitterTipRadius_mm">r_tip [mm]. Cluster envelope 0.001 – 0.05 (1 – 50 μm).</param>
    /// <param name="propellant">Liquid-metal propellant — drives m_ion_pure + γ.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when any input is non-positive, NaN, or
    /// <paramref name="propellant"/> is <see cref="FeepPropellant.None"/>.
    /// </exception>
    public static MairLozanoEmitterResult Solve(
        double acceleratingVoltage_V,
        double beamCurrent_A,
        double emitterTipRadius_mm,
        FeepPropellant propellant)
    {
        if (double.IsNaN(acceleratingVoltage_V) || acceleratingVoltage_V <= 0.0)
            throw new ArgumentException(
                "acceleratingVoltage_V must be a positive finite value.",
                nameof(acceleratingVoltage_V));
        if (double.IsNaN(beamCurrent_A) || beamCurrent_A <= 0.0)
            throw new ArgumentException(
                "beamCurrent_A must be a positive finite value.",
                nameof(beamCurrent_A));
        if (double.IsNaN(emitterTipRadius_mm) || emitterTipRadius_mm <= 0.0)
            throw new ArgumentException(
                "emitterTipRadius_mm must be a positive finite value.",
                nameof(emitterTipRadius_mm));
        if (propellant == FeepPropellant.None)
            throw new ArgumentException(
                "propellant must be a real liquid-metal choice (Indium or Cesium), "
              + "not the FeepPropellant.None sentinel.",
                nameof(propellant));

        // 1. Effective ion mass by propellant.
        double m_ion_pure = propellant switch
        {
            FeepPropellant.Indium => IndiumAtomicMass_kg,
            FeepPropellant.Cesium => CesiumAtomicMass_kg,
            _ => throw new ArgumentException(
                $"Unrecognised FeepPropellant value: {propellant}.",
                nameof(propellant)),
        };
        double clusterFactor = propellant switch
        {
            FeepPropellant.Indium => IndiumClusterFactor,
            FeepPropellant.Cesium => CesiumClusterFactor,
            _ => 1.0,  // unreachable per the guard above
        };
        double m_eff = clusterFactor * m_ion_pure;

        // 2. Tip field (geometry × voltage / radius).
        double r_tip_m = emitterTipRadius_mm * 1.0e-3;
        double E_tip = TipGeometryFactor * acceleratingVoltage_V / r_tip_m;

        // 3. Effective exit velocity (energy conservation against m_eff).
        double v_eff = Math.Sqrt(2.0 * ElementaryCharge_C * acceleratingVoltage_V / m_eff);

        // 4. Mass flow (charge conservation: each effective ion carries
        //    1 elementary charge; ṁ = N_ions/s · m_eff = (I_beam/e) · m_eff).
        double mDot = beamCurrent_A * m_eff / ElementaryCharge_C;

        // 5. Thrust = ṁ · v_eff. Equivalent closed form:
        //    T = I_beam · √(2 · m_eff · V_acc / e).
        double thrust = mDot * v_eff;

        // 6. Isp = v_eff / g₀.
        double isp = v_eff / StandardGravity_ms2;

        return new MairLozanoEmitterResult(
            Thrust_N:              thrust,
            IspVacuum_s:           isp,
            ExitVelocity_ms:       v_eff,
            MassFlow_kgs:          mDot,
            BeamCurrent_A:         beamCurrent_A,
            EmitterTipField_VperM: E_tip,
            EffectiveIonMass_kg:   m_eff,
            PlumeDivergence_rad:   PlumeHalfAngle_rad,
            Converged:             true);
    }
}
