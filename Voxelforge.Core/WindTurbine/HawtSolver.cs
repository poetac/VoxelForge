// HawtSolver.cs — Sprint WT.W1 closed-form horizontal-axis wind-turbine
// performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes the per-snapshot
// available wind power, C_p (clamped at the Betz limit 16/27), rotor +
// electrical power, thrust, and tip speed for a HAWT at a specified
// free-stream wind speed.
//
// The Wave-1 model is a "BEM-lite" closed-form fit: instead of doing a
// per-element blade-element-momentum solve, we use a Gaussian cluster
// fit on C_p(λ) anchored against the NREL 5 MW reference rotor (peak
// C_p ≈ 0.48 at λ ≈ 7.5). Real C_p(λ, pitch) is a 2-D surface; the
// Gaussian-in-λ fit at design pitch is the canonical engineering
// approximation for steady-state power-curve sizing.
//
//   P_available     = 0.5 · ρ · A · V³                            [W]
//   C_p(λ)          = C_p_peak · exp(− ((λ − λ_peak) / σ_λ)² )   [-]
//   P_rotor         = C_p · P_available                            [W]
//   P_elec          = η_drivetrain · P_rotor                       [W]
//   a               = (1 − √(1 − C_p)) / 2   (from C_p = 4·a·(1−a)²)
//   C_T             = 4 · a · (1 − a)                              [-]
//   T               = C_T · 0.5 · ρ · A · V²                       [N]
//
// References:
//   Burton T., Sharpe D., Jenkins N., Bossanyi E. (2011). "Wind Energy
//     Handbook," 2nd ed., chap 3 (BEM theory) + chap 6 (C_p curves).
//   Jonkman J. et al. (2009). NREL/TP-500-38060 (NREL 5 MW reference).
//   Manwell J., McGowan J., Rogers A. (2010). "Wind Energy Explained,"
//     2nd ed., chap 3 (actuator-disk theory).

using System;

namespace Voxelforge.WindTurbine;

/// <summary>
/// Closed-form HAWT performance snapshot solver (Sprint WT.W1).
/// </summary>
internal static class HawtSolver
{
    /// <summary>Betz limit on C_p [-]. 16/27 ≈ 0.5926.</summary>
    internal const double BetzLimit = 16.0 / 27.0;

    /// <summary>Air density at sea-level standard atmosphere [kg/m³].</summary>
    internal const double StandardAirDensity_kgm3 = 1.225;

    // ── NREL 5 MW C_p(λ) cluster-fit anchors ─────────────────────────────

    /// <summary>
    /// Peak C_p achievable on a modern utility-scale HAWT. NREL 5 MW
    /// reference cluster mid-band 0.48 (Jonkman 2009 fig 4). Always ≤
    /// Betz limit 0.5926.
    /// </summary>
    internal const double PeakPowerCoefficient = 0.48;

    /// <summary>Tip-speed ratio at the C_p peak [-]. NREL 5 MW cluster.</summary>
    internal const double TipSpeedRatioAtPeakCp = 7.5;

    /// <summary>
    /// Standard deviation of the Gaussian-in-λ C_p fit [-]. Cluster-mid-
    /// band 3.0 — captures the C_p drop-off as λ moves away from the
    /// design point in either direction.
    /// </summary>
    internal const double TipSpeedRatioWidthSigma = 3.0;

    // ── VAWT (Darrieus / H-rotor) cluster-fit anchors — Sprint WT.W2 ────

    /// <summary>VAWT peak C_p cluster mid-band [-]. ~ 17 % lower than HAWT.</summary>
    internal const double VawtPeakPowerCoefficient = 0.40;

    /// <summary>VAWT tip-speed ratio at the C_p peak [-].</summary>
    internal const double VawtTipSpeedRatioAtPeakCp = 5.0;

    /// <summary>VAWT Gaussian-fit width σ_λ [-]. Narrower than HAWT
    /// because Darrieus rotors have a sharper sweet spot.</summary>
    internal const double VawtTipSpeedRatioWidthSigma = 2.0;

    /// <summary>
    /// Solve the HAWT performance snapshot at a specified free-stream
    /// wind speed.
    /// </summary>
    /// <param name="design">Validated HAWT design.</param>
    /// <param name="windSpeed_ms">Free-stream wind speed at hub height [m/s].</param>
    /// <param name="airDensity_kgm3">
    /// Air density [kg/m³]. Defaults to the sea-level standard atmosphere
    /// (1.225). Hub-height density correction is left to the caller.
    /// </param>
    /// <returns>Solved performance snapshot. When V is outside the
    /// [CutIn, CutOut] band, returns a parked snapshot (C_p = 0,
    /// P_rotor = 0, etc.) — the caller / gates flag the parked state.</returns>
    internal static HawtResult Solve(
        HawtDesign design,
        double     windSpeed_ms,
        double     airDensity_kgm3 = StandardAirDensity_kgm3)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        if (windSpeed_ms < 0.0)
            throw new ArgumentOutOfRangeException(nameof(windSpeed_ms),
                $"windSpeed_ms must be ≥ 0; got {windSpeed_ms}.");
        if (airDensity_kgm3 <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(airDensity_kgm3),
                $"airDensity_kgm3 must be > 0; got {airDensity_kgm3}.");

        // 1. Parked envelope. Below cut-in or above cut-out the turbine
        //    is feathered + idled.
        if (windSpeed_ms < design.CutInWindSpeed_ms
         || windSpeed_ms > design.CutOutWindSpeed_ms)
        {
            return new HawtResult(
                WindSpeed_ms:            windSpeed_ms,
                AvailablePower_W:        0.5 * airDensity_kgm3 * design.SweptArea_m2
                                         * windSpeed_ms * windSpeed_ms * windSpeed_ms,
                PowerCoefficient:        0.0,
                TipSpeedRatio:           0.0,
                RotorAngularSpeed_rads:  0.0,
                TipSpeed_ms:             0.0,
                RotorPower_W:            0.0,
                ElectricalPower_W:       0.0,
                RotorThrust_N:           0.0,
                ThrustCoefficient:       0.0,
                AxialInductionFactor:    0.0);
        }

        // 2. Available kinetic-energy flux through the swept disk.
        double P_avail = 0.5 * airDensity_kgm3 * design.SweptArea_m2
                       * windSpeed_ms * windSpeed_ms * windSpeed_ms;

        // 3. Tip-speed ratio + ω + tip speed. The design holds λ fixed
        //    via the pitch / yaw controller (variable-speed turbines).
        double lambda = design.DesignTipSpeedRatio;
        double omega  = lambda * windSpeed_ms / design.RotorRadius_m;
        double v_tip  = omega  * design.RotorRadius_m;

        // 4. C_p(λ) cluster fit + Betz clamp.
        double C_p = ComputePowerCoefficient(lambda, design.Kind);

        // 5. Axial induction a from C_p = 4·a·(1−a)². Inverting the
        //    cubic: a = (1 − √(1 − C_p·(27/16))) / 2 at the Betz optimum;
        //    for lower C_p we use a small-induction approximation. The
        //    actuator-disk inverse:  a satisfies 4a(1-a)² = C_p_eff
        //    where C_p_eff = C_p / 1.0 (assumes ideal disk, no tip-loss).
        //    For C_p ≤ Betz, take the lower root of the cubic 4·a³ −
        //    8·a² + 4·a − C_p = 0 via the standard induction-factor fit.
        double a = ComputeAxialInductionFactor(C_p);

        // 6. Thrust coefficient + axial thrust.
        double C_T = 4.0 * a * (1.0 - a);
        double T_N = C_T * 0.5 * airDensity_kgm3 * design.SweptArea_m2 * windSpeed_ms * windSpeed_ms;

        // 7. Power roll-up: rotor → drivetrain → grid bus.
        double P_rotor = C_p * P_avail;
        double P_elec  = design.GearboxAndGeneratorEfficiency * P_rotor;

        return new HawtResult(
            WindSpeed_ms:            windSpeed_ms,
            AvailablePower_W:        P_avail,
            PowerCoefficient:        C_p,
            TipSpeedRatio:           lambda,
            RotorAngularSpeed_rads:  omega,
            TipSpeed_ms:             v_tip,
            RotorPower_W:            P_rotor,
            ElectricalPower_W:       P_elec,
            RotorThrust_N:           T_N,
            ThrustCoefficient:       C_T,
            AxialInductionFactor:    a);
    }

    /// <summary>
    /// Compute C_p(λ) from the Gaussian-in-λ cluster fit anchored at the
    /// NREL 5 MW reference. Capped at the Betz limit. Public-static for
    /// tests + future C_p-curve sweeps.
    /// </summary>
    /// <param name="tipSpeedRatio">λ [-]. Must be ≥ 0.</param>
    /// <param name="kind">Sprint WT.W2. Turbine topology — drives per-
    /// kind C_p(λ) cluster anchors. Defaults to HorizontalAxis for
    /// backwards-compat with WT.W1.</param>
    /// <returns>C_p ∈ [0, BetzLimit]. Returns 0 at λ = 0.</returns>
    internal static double ComputePowerCoefficient(
        double tipSpeedRatio,
        WindTurbineKind kind = WindTurbineKind.HorizontalAxis)
    {
        if (tipSpeedRatio < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tipSpeedRatio),
                "λ must be ≥ 0.");
        if (tipSpeedRatio == 0.0) return 0.0;

        double cpPeak, lambdaPeak, sigma;
        switch (kind)
        {
            case WindTurbineKind.HorizontalAxis:
                cpPeak     = PeakPowerCoefficient;
                lambdaPeak = TipSpeedRatioAtPeakCp;
                sigma      = TipSpeedRatioWidthSigma;
                break;
            case WindTurbineKind.VerticalAxis:
                cpPeak     = VawtPeakPowerCoefficient;
                lambdaPeak = VawtTipSpeedRatioAtPeakCp;
                sigma      = VawtTipSpeedRatioWidthSigma;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind,
                    $"Unknown WindTurbineKind '{kind}'.");
        }
        double z = (tipSpeedRatio - lambdaPeak) / sigma;
        double C_p = cpPeak * Math.Exp(-z * z);
        // Defensive Betz clamp (the Gaussian fit can never exceed
        // PeakPowerCoefficient, but a calibration error could push it).
        return Math.Min(C_p, BetzLimit);
    }

    /// <summary>
    /// Compute the axial induction factor a from C_p via the actuator-
    /// disk momentum-theory cubic C_p = 4·a·(1−a)². Returns the smaller
    /// real root (the physically meaningful one for normal operation; the
    /// larger root is the "windmill brake" state outside the disk regime).
    /// </summary>
    /// <param name="powerCoefficient">C_p [-]. Must be in [0, BetzLimit].</param>
    /// <returns>a ∈ [0, 1/3] for C_p ∈ [0, BetzLimit].</returns>
    internal static double ComputeAxialInductionFactor(double powerCoefficient)
    {
        if (powerCoefficient < 0.0)
            throw new ArgumentOutOfRangeException(nameof(powerCoefficient),
                "C_p must be ≥ 0.");
        if (powerCoefficient > BetzLimit + 1e-9)
            throw new ArgumentOutOfRangeException(nameof(powerCoefficient),
                $"C_p ({powerCoefficient:F4}) cannot exceed Betz limit "
              + $"{BetzLimit:F4}.");
        if (powerCoefficient < 1e-12) return 0.0;

        // 4·a·(1-a)² = C_p. Bisection on a ∈ [0, 1/3] (the lower-induction
        // root is in this band).
        double lo = 0.0;
        double hi = 1.0 / 3.0;
        for (int iter = 0; iter < 64; iter++)
        {
            double mid = 0.5 * (lo + hi);
            double residual = 4.0 * mid * (1.0 - mid) * (1.0 - mid) - powerCoefficient;
            if (residual > 0.0)
                hi = mid;
            else
                lo = mid;
            if (hi - lo < 1e-12) break;
        }
        return 0.5 * (lo + hi);
    }
}
