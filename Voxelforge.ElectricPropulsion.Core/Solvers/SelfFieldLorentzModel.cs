// SelfFieldLorentzModel.cs — Sprint EP.W2.MPD self-field Lorentz-acceleration
// physics helper.
//
// Stateless, allocation-free, deterministic implementation of the Maecker
// self-field MPD thrust formula per Maecker H. (1955) "Plasmaströmungen in
// Lichtbögen infolge eigenmagnetischer Kompression" (Z. Physik 141, 198) +
// Polk J.E. (1991) "Operation of a 100 kW class applied-field MPD thruster
// with lithium" (NASA-TM-104380) for the LiLFA validation cluster.
//
// Physics summary (axisymmetric self-field MPD, lithium/argon propellant):
//   Geometry coefficient (Maecker):
//
//       b = (μ₀ / 4π) · (ln(r_a / r_c) + 3/4)
//
//     where r_a is the anode inner radius and r_c the cathode outer radius.
//
//   Thrust (self-field only, no applied B):
//
//       T = b · J²                                                      [N]
//
//   Effective exit velocity (assuming all thrust comes from J×B):
//
//       v_exit = T / ṁ                                                  [m/s]
//       Isp     = v_exit / g₀                                           [s]
//
//   Discharge voltage (semi-empirical fit to LiLFA cluster — Polk 1991):
//
//       V_arc = V_anode_drop + V_arc_column · L / r_a
//
//     V_anode_drop ≈ 25 V (LiLFA), V_arc_column ≈ 8 V/(L/r_a). Held as a
//     coarse linear fit at this fidelity; the dispersion across the cluster
//     is absorbed by the ±25 % thrust tolerance on the fixture.
//
//   Magnetic pressure scale (peak at cathode tip, r=r_c):
//
//       B_peak     = μ₀ · J / (2 π r_c)
//       p_mag_peak = B_peak² / (2 μ₀)                                   [Pa]
//
//   Cathode wall temperature (lumped 0-D radiative balance at the cathode
//   tip; absorbs the cathode-fall power into the tip and emits as a Stefan
//   black-body to the chamber wall):
//
//       Q_in = V_cathode_drop · J  ≈  10 · J                            [W]
//       Q_rad = ε σ A_tip · T⁴
//       T_cathode = (Q_in / (ε σ A_tip))^0.25                           [K]
//
//     ε ≈ 0.40 for thoriated W; A_tip = π r_c² (face area). This tracks the
//     dominant failure mode without invoking a full thermal solver.
//
// LiLFA cluster anchor (Polk 1991, NASA-TM-104380):
//   J_arc ≈ 2000 A,  ṁ_Li ≈ 80 mg/s,  V_arc ≈ 60 V (P_arc ≈ 120 kW),
//   r_c ≈ 6 mm, r_a ≈ 50 mm, L ≈ 100 mm.
//   Target: T ≈ 2.0 N, Isp ≈ 2500 s, v_exit ≈ 25 km/s.
//
// Validation tolerance per ADR-029 D4 generalised: ±25 % thrust / ±15 % Isp.
// Looser than GIT (±20 % / ±15 %) because the discharge-voltage and cathode-
// erosion fits are coarse semi-empirical models, not closed-form physics.

using System;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Output of the self-field MPD Lorentz-acceleration model. Pure data; no
/// reference to PicoGK or any I/O surface.
/// </summary>
/// <param name="Thrust_N">
/// Total thrust [N] = T_self + T_af. Bit-identical to T_self when
/// B_applied = 0 (Wave-2 self-field-only path).
/// </param>
/// <param name="ThrustCoefficient_NperA2">Geometry-only coefficient b [N/A²].</param>
/// <param name="ExitVelocity_ms">Effective exit velocity v = T / ṁ [m/s].</param>
/// <param name="IspVacuum_s">v / g₀ [s].</param>
/// <param name="DischargeVoltage_V">Semi-empirical V_arc [V].</param>
/// <param name="DischargePower_W">V_arc · J_arc [W].</param>
/// <param name="MagneticPressure_Pa">Peak B² / 2μ₀ at the cathode tip [Pa].</param>
/// <param name="CathodeWallTemp_K">Lumped cathode-tip temperature [K].</param>
/// <param name="ThrustEfficiency_Maecker">η_T = (½ ṁ v²) / (V_arc · J_arc).</param>
/// <param name="PlumeDivergenceHalfAngle_rad">Plume half-angle [rad] (cluster anchor).</param>
/// <param name="Converged">True for the closed-form solve (always converges).</param>
public sealed record SelfFieldLorentzResult(
    double Thrust_N,
    double ThrustCoefficient_NperA2,
    double ExitVelocity_ms,
    double IspVacuum_s,
    double DischargeVoltage_V,
    double DischargePower_W,
    double MagneticPressure_Pa,
    double CathodeWallTemp_K,
    double ThrustEfficiency_Maecker,
    double PlumeDivergenceHalfAngle_rad,
    bool   Converged)
{
    /// <summary>
    /// Applied-field solenoid B_z [T] in force at solve time. 0 for
    /// self-field-only (Wave-2) operation. Carried so gates / fixtures /
    /// reporting can introspect the operating point without re-deriving
    /// from the design record.
    /// </summary>
    public double AppliedFieldStrength_T { get; init; }

    /// <summary>
    /// Applied-field thrust contribution T_af [N] from Sankaran 2004:
    /// T_af = k_af · J · B_applied · r_a. 0 when B_applied = 0; positive
    /// otherwise. Wave-3 (Sprint EP.W3.AF).
    /// </summary>
    public double AppliedFieldThrust_N { get; init; }

    /// <summary>
    /// Self-field Maecker thrust T_self = b · J² [N]. Bit-identical to
    /// <see cref="Thrust_N"/> when <see cref="AppliedFieldStrength_T"/> = 0.
    /// Carried separately so the advisory gate
    /// <c>MPD_APPLIED_FIELD_DOMINATES</c> can compute T_af / (T_self + T_af).
    /// </summary>
    public double SelfFieldThrust_N { get; init; }
}

/// <summary>
/// Self-field MPD Lorentz-acceleration model. Mirror of
/// <see cref="ChildLangmuirBeamModel"/> / <see cref="MaeckerKovityaArcModel"/>
/// for the MPD variant.
/// </summary>
public static class SelfFieldLorentzModel
{
    // ── Physical constants ──────────────────────────────────────────────

    /// <summary>Standard gravity for Isp [m/s²].</summary>
    public const double g0 = 9.80665;

    /// <summary>Vacuum permeability μ₀ [T·m/A].</summary>
    public const double VacuumPermeability_TmPerA = 4.0e-7 * Math.PI;

    /// <summary>Stefan-Boltzmann constant σ [W/(m²·K⁴)].</summary>
    public const double StefanBoltzmann_WperM2K4 = 5.670_374_419e-8;

    // ── Calibration constants (LiLFA cluster anchor) ────────────────────

    /// <summary>
    /// Cathode emissivity ε. Thoriated tungsten (the LiLFA cathode material
    /// of choice) sits at ε ≈ 0.40 over 2000–3500 K (Touloukian TPRC §7).
    /// </summary>
    public const double CathodeEmissivity = 0.40;

    /// <summary>
    /// Cathode-fall voltage [V] absorbed at the tip. LiLFA cluster ≈ 8–12 V
    /// (Polk 1991); 10 V mid-band anchor.
    /// </summary>
    public const double CathodeFallVoltage_V = 10.0;

    /// <summary>
    /// Anode-drop voltage [V] for the discharge-voltage fit. LiLFA cluster
    /// ≈ 20–30 V; 25 V mid-band anchor.
    /// </summary>
    public const double AnodeFallVoltage_V = 25.0;

    /// <summary>
    /// Arc-column voltage gradient coefficient [V] for the discharge-voltage
    /// fit V_arc = V_anode + V_col · (L / r_a). LiLFA cluster ≈ 8 V at L/r_a ≈ 2.
    /// </summary>
    public const double ArcColumnVoltageCoefficient_V = 8.0;

    /// <summary>
    /// Default plume-divergence half-angle θ_plume [rad] = 0.524 rad ≈ 30°.
    /// LiLFA cluster value; MPD plumes are inherently wider than gridded-ion
    /// because J×B acceleration is distributed across the plasma volume.
    /// </summary>
    public const double DefaultPlumeHalfAngle_rad = 0.524;

    /// <summary>
    /// Default applied-field coupling coefficient k_af (dimensionless) used
    /// in the Sankaran-2004 thrust-augmentation fit
    /// T_af = k_af · J · B_applied · r_a. The published envelope across
    /// LiLFA (Polk 1991, k_af ≈ 0.10) / Princeton X9 (Tikhonov 1997,
    /// k_af ≈ 0.15) / Stuttgart ZT-1 / Mai Riga (Krülle 1998, k_af ≈ 0.25)
    /// campaigns sits at 0.05 – 0.30. 0.20 is the cluster mid-band default;
    /// the fit implicitly absorbs the plume swirl-to-axial conversion
    /// efficiency and rotational-energy partition (not a first-principles
    /// constant). Override at the design level via
    /// <see cref="ElectricPropulsionEngineDesign.MpdAppliedFieldCouplingOverride"/>
    /// when fixture-derived calibration data exists.
    /// </summary>
    public const double DefaultAppliedFieldCoupling = 0.20;

    /// <summary>
    /// Applied-field strength [T] below which the augmentation is treated
    /// as numerically zero (avoids tiny T_af noise from round-trip NaN→0
    /// translations). Set to 1e-6 T (= 10 μT, well below any realistic
    /// solenoid B_z).
    /// </summary>
    public const double AppliedFieldNumericFloor_T = 1.0e-6;

    /// <summary>
    /// Solve the self-field Maecker MPD model end-to-end. Sprint EP.W3
    /// extends the surface with optional applied-field (B_z) augmentation
    /// — at <paramref name="appliedFieldStrength_T"/> = 0 (default) the
    /// solver returns bit-identical Wave-2 self-field-only output.
    /// </summary>
    /// <param name="arcCurrent_A">J_arc [A] — discharge current.</param>
    /// <param name="propellantMassFlow_kgs">ṁ [kg/s] — propellant mass flow.</param>
    /// <param name="cathodeRadius_mm">r_c [mm] — cathode outer radius.</param>
    /// <param name="anodeRadius_mm">r_a [mm] — anode inner radius.</param>
    /// <param name="chamberLength_mm">L [mm] — chamber axial length (cathode tip to anode lip).</param>
    /// <param name="appliedFieldStrength_T">
    /// Optional axial-solenoid B_z [T] for applied-field augmentation.
    /// Sprint EP.W3 (Sankaran-2004 fit). 0 (default) → self-field only.
    /// Values below <see cref="AppliedFieldNumericFloor_T"/> are treated
    /// as zero. Must be non-negative.
    /// </param>
    /// <param name="appliedFieldCoupling">
    /// Optional coupling coefficient k_af for the applied-field fit.
    /// <see cref="double.NaN"/> (default) uses
    /// <see cref="DefaultAppliedFieldCoupling"/> = 0.30. Must be positive
    /// when finite. Ignored when <paramref name="appliedFieldStrength_T"/>
    /// is zero.
    /// </param>
    /// <returns>Solved performance state.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any required input is NaN or non-positive, when
    /// <paramref name="anodeRadius_mm"/> does not exceed
    /// <paramref name="cathodeRadius_mm"/> (Maecker requires a finite annular
    /// gap), when <paramref name="appliedFieldStrength_T"/> is NaN or negative,
    /// or when the optional <paramref name="appliedFieldCoupling"/> is finite
    /// but non-positive.
    /// </exception>
    public static SelfFieldLorentzResult Solve(
        double arcCurrent_A,
        double propellantMassFlow_kgs,
        double cathodeRadius_mm,
        double anodeRadius_mm,
        double chamberLength_mm,
        double appliedFieldStrength_T = 0.0,
        double appliedFieldCoupling = double.NaN)
    {
        if (double.IsNaN(arcCurrent_A) || arcCurrent_A <= 0)
            throw new ArgumentOutOfRangeException(nameof(arcCurrent_A),
                $"ArcCurrent_A must be positive; got J={arcCurrent_A:F1} A.");
        if (double.IsNaN(propellantMassFlow_kgs) || propellantMassFlow_kgs <= 0)
            throw new ArgumentOutOfRangeException(nameof(propellantMassFlow_kgs),
                $"PropellantMassFlow_kgs must be positive; got ṁ={propellantMassFlow_kgs:E3} kg/s.");
        if (double.IsNaN(cathodeRadius_mm) || cathodeRadius_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(cathodeRadius_mm),
                $"MpdCathodeRadius_mm must be positive; got r_c={cathodeRadius_mm:F3} mm.");
        if (double.IsNaN(anodeRadius_mm) || anodeRadius_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(anodeRadius_mm),
                $"MpdAnodeRadius_mm must be positive; got r_a={anodeRadius_mm:F3} mm.");
        if (anodeRadius_mm <= cathodeRadius_mm)
            throw new ArgumentOutOfRangeException(nameof(anodeRadius_mm),
                $"MpdAnodeRadius_mm (r_a={anodeRadius_mm:F3} mm) must exceed MpdCathodeRadius_mm "
              + $"(r_c={cathodeRadius_mm:F3} mm); the Maecker formula requires a finite annular gap.");
        if (double.IsNaN(chamberLength_mm) || chamberLength_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(chamberLength_mm),
                $"MpdChamberLength_mm must be positive; got L={chamberLength_mm:F3} mm.");
        if (double.IsNaN(appliedFieldStrength_T) || appliedFieldStrength_T < 0)
            throw new ArgumentOutOfRangeException(nameof(appliedFieldStrength_T),
                $"AppliedFieldStrength_T must be non-negative; got B_z={appliedFieldStrength_T:F4} T.");
        if (!double.IsNaN(appliedFieldCoupling) && appliedFieldCoupling <= 0)
            throw new ArgumentOutOfRangeException(nameof(appliedFieldCoupling),
                $"AppliedFieldCoupling must be positive when finite; got k_af={appliedFieldCoupling:F3}.");

        // 1. Maecker geometry coefficient.
        double rRatio = anodeRadius_mm / cathodeRadius_mm;
        double b = (VacuumPermeability_TmPerA / (4.0 * Math.PI)) * (Math.Log(rRatio) + 0.75);

        // 2a. Self-field Maecker thrust T_self = b · J².
        double T_self = b * arcCurrent_A * arcCurrent_A;

        // 2b. Applied-field augmentation T_af = k_af · J · B_z · r_a
        //     (Sankaran 2004; LiLFA Polk 1991 cluster anchor). Numerically
        //     suppressed below AppliedFieldNumericFloor_T so a round-tripped
        //     v6 design with B_applied = NaN → 0 returns a bit-identical
        //     self-field result.
        double k_af = double.IsNaN(appliedFieldCoupling)
            ? DefaultAppliedFieldCoupling
            : appliedFieldCoupling;
        double r_a_m = anodeRadius_mm * 1e-3;
        double T_af = appliedFieldStrength_T > AppliedFieldNumericFloor_T
            ? k_af * arcCurrent_A * appliedFieldStrength_T * r_a_m
            : 0.0;

        // 2c. Total thrust = self-field + applied-field contributions.
        double thrust = T_self + T_af;

        // 3. Effective exit velocity + Isp.
        double v_exit = thrust / propellantMassFlow_kgs;
        double isp    = v_exit / g0;

        // 4. Discharge voltage (semi-empirical linear fit).
        double LoverRa = chamberLength_mm / anodeRadius_mm;
        double V_arc   = AnodeFallVoltage_V + ArcColumnVoltageCoefficient_V * LoverRa;
        double P_arc   = V_arc * arcCurrent_A;

        // 5. Magnetic pressure at the cathode tip.
        double r_c_m   = cathodeRadius_mm * 1e-3;
        double B_peak  = VacuumPermeability_TmPerA * arcCurrent_A / (2.0 * Math.PI * r_c_m);
        double p_mag   = B_peak * B_peak / (2.0 * VacuumPermeability_TmPerA);

        // 6. Cathode wall temperature (lumped radiative balance at the tip).
        //
        // Real MPD cathodes (50-200 mm ThW rods) lose ~90-99% of cathode-fall
        // heat through conduction down the rod to the water-cooled base and
        // re-radiation from the cylindrical body — only ~1% leaves as radiation
        // from the flat-disk tip face (Polk 1991 LiLFA survey, Kurtz 1996).
        // CathodeRadiationFraction = 0.01 is the Option-B empirical fix (#545).
        const double CathodeRadiationFraction = 0.01;
        double Q_cathode = CathodeRadiationFraction * CathodeFallVoltage_V * arcCurrent_A;
        double A_tip     = Math.PI * r_c_m * r_c_m;
        double T_cathode = Math.Pow(
            Q_cathode / (CathodeEmissivity * StefanBoltzmann_WperM2K4 * A_tip),
            0.25);

        // 7. Maecker thrust efficiency η_T = (½ ṁ v²) / P_arc.
        double eta = P_arc > 0
            ? Math.Max(0.0, Math.Min(1.0, 0.5 * propellantMassFlow_kgs * v_exit * v_exit / P_arc))
            : 0.0;

        return new SelfFieldLorentzResult(
            Thrust_N:                     thrust,
            ThrustCoefficient_NperA2:     b,
            ExitVelocity_ms:              v_exit,
            IspVacuum_s:                  isp,
            DischargeVoltage_V:           V_arc,
            DischargePower_W:             P_arc,
            MagneticPressure_Pa:          p_mag,
            CathodeWallTemp_K:            T_cathode,
            ThrustEfficiency_Maecker:     eta,
            PlumeDivergenceHalfAngle_rad: DefaultPlumeHalfAngle_rad,
            Converged:                    true)
        {
            AppliedFieldStrength_T = appliedFieldStrength_T > AppliedFieldNumericFloor_T
                ? appliedFieldStrength_T
                : 0.0,
            AppliedFieldThrust_N   = T_af,
            SelfFieldThrust_N      = T_self,
        };
    }
}
