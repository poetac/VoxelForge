// AntennaSolver.cs — Sprint ANT.W1 closed-form RF-link performance
// snapshot.
//
// Stateless, allocation-free, deterministic. Implements the canonical
// Friis transmission equation in log-dB form:
//
//   λ = c / f
//   G_tx_dBi  = per-topology gain (see ComputeAntennaGain_dBi)
//   G_rx_dBi  = per-topology gain
//   EIRP_dBW  = 10·log10(P_tx) + G_tx_dBi
//   FSPL_dB   = 20·log10(4π·R / λ)
//   P_rx_dBm  = EIRP_dBW + G_rx_dBi − FSPL_dB + 30   [dBW → dBm]
//   P_rx_W    = 10^((P_rx_dBm − 30) / 10)
//
// Per-topology gain formulas:
//   IdealIsotropic: G = 0 dBi (reference)
//   HalfWaveDipole: G = 2.15 dBi (canonical)
//   ParabolicDish:  G = η · (π·D/λ)²
//                   G_dBi = 10·log10(η · (π·D/λ)²)
//                         = 10·log10(η) + 20·log10(π·D/λ)
//   Helical (ANT.W4): G = 15 · N · (C/λ)² · (S/λ) [Kraus 1988 §7-4]
//   Patch   (ANT.W4): G = PatchGain_dBi = 7.5 dBi (fixed cluster)
//   CrossedDipole (ANT.W4): G = 2.15 dBi (same as HalfWaveDipole)
//
// References:
//   Friis H.T. (1946). "A note on a simple transmission formula."
//     Proc. IRE 34 (5).
//   Balanis C. (2016). "Antenna Theory," 4th ed.
//   ITU-R P.525 (1994). "Calculation of free-space attenuation."
//   Kraus J.D. (1988). "Antennas," 2nd ed., §7-4 (end-fire helix).

using System;

namespace Voxelforge.Antenna;

/// <summary>
/// Closed-form RF-link performance snapshot solver (Sprint ANT.W1).
/// </summary>
internal static class AntennaSolver
{
    /// <summary>Speed of light [m/s] (CODATA 2019 exact).</summary>
    internal const double SpeedOfLight_ms = 299_792_458.0;

    /// <summary>Half-wave dipole gain [dBi] (canonical textbook value).</summary>
    internal const double HalfWaveDipoleGain_dBi = 2.15;

    /// <summary>
    /// Yagi-Uda end-fire array typical gain [dBi]. Cluster mid-band for
    /// a 3-element Yagi commonly seen in cellular / WiFi rooftop /
    /// ham-radio applications. Sprint ANT.W2.
    /// </summary>
    internal const double YagiUdaGain_dBi = 7.0;

    /// <summary>
    /// Pyramidal horn typical gain [dBi]. Cluster mid-band for X-band
    /// + Ku-band ground stations + EM-test-range standard horns.
    /// Sprint ANT.W2.
    /// </summary>
    internal const double HornGain_dBi = 18.0;

    /// <summary>Boltzmann constant [J/K]. Sprint ANT.W2.</summary>
    internal const double BoltzmannConstant_J_K = 1.380649e-23;

    /// <summary>
    /// Microstrip patch antenna fixed cluster gain [dBi] (Sprint ANT.W4).
    /// Cluster mid-band for a resonant λ/2 × λ/2 patch on a ground plane
    /// (typical GPS receiver patch, drone telemetry, satellite-phone).
    /// Published range 6.5–8.5 dBi; 7.5 is the cluster centroid.
    /// Reference: Balanis C. (2016) "Antenna Theory," 4th ed., §14.2.
    /// </summary>
    internal const double PatchGain_dBi = 7.5;

    /// <summary>
    /// Crossed-dipole gain [dBi] (Sprint ANT.W4). Two half-wave dipoles
    /// fed in phase quadrature at 90° produce circular polarisation in
    /// the broadside direction. Total gain equals the single half-wave
    /// dipole (2.15 dBi) — the quadrature feed selects one CP sense
    /// rather than adding gain. Used for LEO weather-satellite receive
    /// (NOAA APT) and circularly-polarised uplinks.
    /// </summary>
    internal const double CrossedDipoleGain_dBi = HalfWaveDipoleGain_dBi;

    /// <summary>
    /// Solve the RF-link snapshot at the design operating point.
    /// </summary>
    internal static AntennaLinkResult Solve(AntennaLinkDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        double lambda = SpeedOfLight_ms / design.Frequency_Hz;

        double G_tx_dBi = ComputeAntennaGain_dBi(
            design.TransmitAntennaKind, design.TransmitDishDiameter_m,
            lambda, design.DishApertureEfficiency,
            design.HelicalTurns, design.HelicalCircumference_rel,
            design.HelicalTurnSpacing_rel);
        double G_rx_dBi = ComputeAntennaGain_dBi(
            design.ReceiveAntennaKind, design.ReceiveDishDiameter_m,
            lambda, design.DishApertureEfficiency,
            design.HelicalTurns, design.HelicalCircumference_rel,
            design.HelicalTurnSpacing_rel);

        // P_tx in dBW: 10·log10(P_tx_W).
        double P_tx_dBW = 10.0 * Math.Log10(design.TransmitPower_W);
        double EIRP_dBW = P_tx_dBW + G_tx_dBi;

        // FSPL in dB. Use the standard form 20·log10(4πR/λ).
        double FSPL_dB = 20.0 * Math.Log10(4.0 * Math.PI * design.LinkDistance_m / lambda);

        // Received power: dBW = EIRP + G_rx − FSPL; convert to dBm (+30).
        double P_rx_dBW = EIRP_dBW + G_rx_dBi - FSPL_dB;
        double P_rx_dBm = P_rx_dBW + 30.0;
        double P_rx_W   = Math.Pow(10.0, P_rx_dBW / 10.0);

        // Sprint ANT.W3 — modulation/FEC + sensitivity floor + achieved
        // Eb/N₀. The design's NF is folded into the sensitivity floor;
        // achieved Eb/N₀ is (P_rx / N_floor) in dB, where N_floor is
        // the same kT·BW·NF quantity (post-NF) the sensitivity uses.
        double required_dB = ModulationSchemeTable.RequiredEbN0_dB(design.Modulation);
        double sens_dBm    = ReceiverSensitivityCalculator.Sensitivity_dBm(
            systemNoiseTemperature_K: SystemNoiseTemperatureForFloor_K,
            bandwidth_Hz:             design.BandwidthOccupancy_Hz,
            noiseFigure_dB:           design.ReceiverNoiseFigure_dB,
            requiredEbN0_dB:          required_dB);
        double n_floor_dBm = ReceiverSensitivityCalculator.ThermalNoiseFloor_dBm(
            systemNoiseTemperature_K: SystemNoiseTemperatureForFloor_K,
            bandwidth_Hz:             design.BandwidthOccupancy_Hz,
            noiseFigure_dB:           design.ReceiverNoiseFigure_dB);
        double achieved_dB = P_rx_dBm - n_floor_dBm;

        // Sprint ANT.W2 — propagation + hardware system losses.
        double rain_dB = ItuAtmosphericModels.RainSlantPathAttenuation_dB(
            design.Frequency_Hz, design.ElevationAngle_deg, design.RainRate_mmPerHr);
        double atm_dB  = ItuAtmosphericModels.AtmosphericAbsorption_dB(
            design.Frequency_Hz, design.ElevationAngle_deg);
        double sysLoss_dB = rain_dB
                          + atm_dB
                          + design.PointingLoss_dB
                          + design.PolarisationMismatch_dB
                          + design.CableConnectorLoss_dB;
        // LinkClosureMargin > 0 means the link closes.
        // ReceivedPower_dBm is kept as the Friis-only value for backward
        // compatibility; the full gate is P_rx − system_loss − sensitivity.
        double closure_dB = P_rx_dBm - sysLoss_dB - sens_dBm;

        return new AntennaLinkResult(
            Wavelength_m:                          lambda,
            TransmitAntennaGain_dBi:               G_tx_dBi,
            ReceiveAntennaGain_dBi:                G_rx_dBi,
            EffectiveIsotropicRadiatedPower_dBW:   EIRP_dBW,
            FreeSpacePathLoss_dB:                  FSPL_dB,
            ReceivedPower_dBm:                     P_rx_dBm,
            ReceivedPower_W:                       P_rx_W,
            ReceiverSensitivity_dBm:               sens_dBm,
            RequiredEbN0_dB:                       required_dB,
            AchievedEbN0_dB:                       achieved_dB,
            RainAttenuation_dB:                    rain_dB,
            AtmosphericAbsorption_dB:              atm_dB,
            SystemLoss_dB:                         sysLoss_dB,
            LinkClosureMargin_dB:                  closure_dB);
    }

    /// <summary>
    /// Default system noise temperature [K] used by <see cref="Solve"/>
    /// when filling in the ANT.W3 sensitivity-floor + achieved-Eb/N₀
    /// fields. 290 K (the IEEE / ITU reference noise temperature, i.e.
    /// "room-temperature thermal" — the value the noise-figure formalism
    /// is normalised against; Sklar 2e §4.2.2). Callers that need a
    /// different T_sys (e.g. DSN 25 K cryo, GS 100 K, cellular 300 K)
    /// continue to call <see cref="ComputeLinkMargin_dB"/> directly with
    /// their own value — that path is unchanged.
    /// </summary>
    internal const double SystemNoiseTemperatureForFloor_K = 290.0;

    /// <summary>
    /// Compute per-topology antenna gain [dBi]. Public-static helper for
    /// per-antenna gain studies + link-budget margin analysis.
    /// Sprint ANT.W4 adds three new topologies via optional parameters:
    /// <c>helicalTurns</c>, <c>helicalCircumference_rel</c> (C/λ), and
    /// <c>helicalTurnSpacing_rel</c> (S/λ). Non-helical topologies ignore
    /// these parameters; defaults reproduce the pre-ANT.W4 behaviour.
    /// </summary>
    internal static double ComputeAntennaGain_dBi(
        AntennaKind kind,
        double dishDiameter_m,
        double wavelength_m,
        double dishApertureEfficiency,
        int    helicalTurns             = 10,
        double helicalCircumference_rel = 1.0,
        double helicalTurnSpacing_rel   = 0.25)
    {
        switch (kind)
        {
            case AntennaKind.IdealIsotropic:
                return 0.0;
            case AntennaKind.HalfWaveDipole:
                return HalfWaveDipoleGain_dBi;
            case AntennaKind.YagiUda:
                return YagiUdaGain_dBi;
            case AntennaKind.Horn:
                return HornGain_dBi;
            case AntennaKind.ParabolicDish:
                if (dishDiameter_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(dishDiameter_m),
                        "Dish diameter must be > 0 for a ParabolicDish.");
                if (wavelength_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(wavelength_m),
                        "Wavelength must be > 0.");
                // G = η · (πD/λ)². In dB: 10·log10(η) + 20·log10(πD/λ).
                double piDOverLambda = Math.PI * dishDiameter_m / wavelength_m;
                return 10.0 * Math.Log10(dishApertureEfficiency)
                     + 20.0 * Math.Log10(piDOverLambda);
            case AntennaKind.Helical:
                // Kraus end-fire formula (Kraus 1988 §7-4):
                //   G_linear = 15 · N · (C/λ)² · (S/λ)
                // Valid for 0.75 ≤ C/λ ≤ 1.33, 12° ≤ pitch angle ≤ 14°.
                // The Max(1.0, ...) floor prevents log10(0) on degenerate inputs;
                // ValidateSelf() already ensures N ≥ 1 and both ratios > 0.
                double G_helical = 15.0 * helicalTurns
                                 * helicalCircumference_rel * helicalCircumference_rel
                                 * helicalTurnSpacing_rel;
                return 10.0 * Math.Log10(Math.Max(1.0, G_helical));
            case AntennaKind.Patch:
                return PatchGain_dBi;
            case AntennaKind.CrossedDipole:
                return CrossedDipoleGain_dBi;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind,
                    $"Unknown AntennaKind '{kind}'.");
        }
    }

    // ── ANT.W7 — geometry → RF coupling advisories ──────────────────────

    /// <summary>
    /// Sprint ANT.W7. Check whether the physical helix coil diameter is
    /// consistent with the C/λ parametric design variable within 5 %.
    /// Returns <see langword="true"/> when a mismatch is detected
    /// (gate <see cref="AntennaConstraintIds.GeometryRfMismatch"/>).
    /// </summary>
    /// <param name="design">Antenna link design. Ignored (returns
    ///   <see langword="false"/>) when
    ///   <see cref="AntennaLinkDesign.HelicalCoilDiameter_mm"/> is 0.</param>
    internal static bool CheckHelicalGeometryRfMismatch(AntennaLinkDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (design.HelicalCoilDiameter_mm <= 0.0) return false;
        double lambda_mm = SpeedOfLight_ms / design.Frequency_Hz * 1000.0;
        double physicalCrel = Math.PI * design.HelicalCoilDiameter_mm / lambda_mm;
        double relDiff = Math.Abs(physicalCrel - design.HelicalCircumference_rel)
                       / design.HelicalCircumference_rel;
        return relDiff > 0.05;
    }

    /// <summary>
    /// Sprint ANT.W7. Check whether a user-supplied physical Yagi element
    /// spacing is within the optimal Yagi-Uda range (0.1–0.5 λ). Returns
    /// <see langword="true"/> when spacing is outside this range
    /// (gate <see cref="AntennaConstraintIds.GeometryRfMismatch"/>).
    /// </summary>
    /// <param name="design">Antenna link design. Ignored (returns
    ///   <see langword="false"/>) when
    ///   <see cref="AntennaLinkDesign.YagiElementSpacing_mm"/> is 0.</param>
    internal static bool CheckYagiElementSpacingValidity(AntennaLinkDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (design.YagiElementSpacing_mm <= 0.0) return false;
        double lambda_mm = SpeedOfLight_ms / design.Frequency_Hz * 1000.0;
        double spacingRel = design.YagiElementSpacing_mm / lambda_mm;
        return spacingRel < 0.1 || spacingRel > 0.5;
    }

    /// <summary>
    /// Sprint ANT.W7. Compute the resonant frequency [Hz] of a microstrip
    /// patch antenna using the Bahl-Trivedi effective-permittivity +
    /// fringing-correction formula (Bahl I.J., Trivedi D.K., 1977).
    /// </summary>
    /// <param name="design">Antenna link design. When
    ///   <see cref="AntennaLinkDesign.PatchLength_mm"/> is 0 the patch
    ///   length is auto-computed from the design frequency and material;
    ///   in that case the returned frequency equals the design frequency
    ///   (self-consistent by construction).</param>
    /// <returns>Resonant frequency f_r [Hz].</returns>
    internal static double ComputePatchResonantFrequency_Hz(AntennaLinkDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        double eps_r = PrintMaterialTable.RelativePermittivity(design.PrintMaterialKind);
        double h     = design.SubstrateThickness_mm;
        if (h <= 0.0 || eps_r <= 1.0) return design.Frequency_Hz;

        double W_mm = design.PatchWidth_mm > 0.0
            ? design.PatchWidth_mm
            : ComputePatchWidth_mm(design.Frequency_Hz, eps_r);

        double L_mm = design.PatchLength_mm > 0.0
            ? design.PatchLength_mm
            : ComputePatchLength_mm(design.Frequency_Hz, eps_r, W_mm, h);

        return PatchLengthToFrequency_Hz(L_mm, eps_r, W_mm, h);
    }

    /// <summary>
    /// Sprint ANT.W7. Returns <see langword="true"/> when the physical
    /// patch geometry (user-supplied
    /// <see cref="AntennaLinkDesign.PatchLength_mm"/> and/or
    /// <see cref="AntennaLinkDesign.PatchWidth_mm"/>) gives a resonant
    /// frequency that deviates from <see cref="AntennaLinkDesign.Frequency_Hz"/>
    /// by more than 5 % (gate
    /// <see cref="AntennaConstraintIds.GeometryRfMismatch"/>).
    /// Returns <see langword="false"/> if both patch dimensions are 0
    /// (auto-compute mode).
    /// </summary>
    internal static bool CheckPatchGeometryRfMismatch(AntennaLinkDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (design.PatchLength_mm <= 0.0 && design.PatchWidth_mm <= 0.0) return false;
        double f_r   = ComputePatchResonantFrequency_Hz(design);
        double relDiff = Math.Abs(f_r - design.Frequency_Hz) / design.Frequency_Hz;
        return relDiff > 0.05;
    }

    // ── Private patch geometry helpers (Bahl-Trivedi 1977) ───────────────

    // Auto-compute patch width W [mm] for dominant TM010 mode.
    internal static double ComputePatchWidth_mm(double frequency_Hz, double eps_r)
        => SpeedOfLight_ms / (2.0 * frequency_Hz)
           * Math.Sqrt(2.0 / (eps_r + 1.0)) * 1000.0;

    // Auto-compute patch length L [mm] from frequency, ε_r, W, and h.
    internal static double ComputePatchLength_mm(
        double frequency_Hz, double eps_r, double W_mm, double h_mm)
    {
        double L_eff = SpeedOfLight_ms
                     / (2.0 * frequency_Hz * 1e-3
                              * Math.Sqrt(EffectivePermittivity(eps_r, W_mm, h_mm)));
        double deltaL = FringingCorrection_mm(eps_r, W_mm, h_mm);
        return L_eff - 2.0 * deltaL;
    }

    // Compute resonant frequency [Hz] from physical length L [mm].
    private static double PatchLengthToFrequency_Hz(
        double L_mm, double eps_r, double W_mm, double h_mm)
    {
        double deltaL = FringingCorrection_mm(eps_r, W_mm, h_mm);
        double L_eff  = L_mm + 2.0 * deltaL;
        return SpeedOfLight_ms
             / (2.0 * L_eff * 1e-3 * Math.Sqrt(EffectivePermittivity(eps_r, W_mm, h_mm)));
    }

    // Bahl-Trivedi effective permittivity.
    private static double EffectivePermittivity(double eps_r, double W_mm, double h_mm)
        => (eps_r + 1.0) / 2.0
         + (eps_r - 1.0) / 2.0 * Math.Pow(1.0 + 12.0 * h_mm / W_mm, -0.5);

    // Hammerstad-Jensen fringing correction ΔL [mm].
    private static double FringingCorrection_mm(double eps_r, double W_mm, double h_mm)
    {
        double eps_eff = EffectivePermittivity(eps_r, W_mm, h_mm);
        double wh = W_mm / h_mm;
        return 0.412 * h_mm
             * (eps_eff + 0.3) * (wh + 0.264)
             / ((eps_eff - 0.258) * (wh + 0.8));
    }

    // ── (existing ComputeLinkMargin_dB below, unchanged) ─────────────────

    /// <summary>
    /// Sprint ANT.W2. Compute the Eb/N0 link margin [dB] for a digital
    /// communications link given received power, data rate, and system
    /// noise temperature. The classical link-budget formula:
    ///
    ///   N₀     = k_B · T_system               [W/Hz, single-sided PSD]
    ///   Eb     = P_rx / R_data                [J/bit]
    ///   Eb/N0  = Eb / N₀ = P_rx / (R_data · k_B · T_system)
    ///   margin = (Eb/N0) − (Eb/N0)_required   [dB]
    /// </summary>
    /// <param name="receivedPower_W">P_rx [W] from the Friis solve.</param>
    /// <param name="dataRate_bps">R_data [bits/s].</param>
    /// <param name="systemNoiseTemperature_K">T_sys [K] — combined antenna,
    /// LNA, and cable contributions. DSN ≈ 25 K (cryo LNA); typical
    /// commercial ground station ≈ 100 K; cellular ≈ 300 K.</param>
    /// <param name="requiredEbN0_dB">Required Eb/N0 [dB] for the
    /// modulation scheme + FEC. BPSK + uncoded ≈ 9.6 dB; QPSK turbo
    /// ≈ 3 dB; QAM-256 ≈ 18 dB; deep-space LDPC ≈ 0.5 dB.</param>
    /// <returns>Link margin [dB]. Positive = link closed; negative =
    /// link fails by that margin (requires more P_tx, larger antennas,
    /// or stronger FEC).</returns>
    internal static double ComputeLinkMargin_dB(
        double receivedPower_W,
        double dataRate_bps,
        double systemNoiseTemperature_K,
        double requiredEbN0_dB)
    {
        if (receivedPower_W <= 0)
            throw new ArgumentOutOfRangeException(nameof(receivedPower_W),
                "P_rx must be > 0.");
        if (dataRate_bps <= 0)
            throw new ArgumentOutOfRangeException(nameof(dataRate_bps),
                "R_data must be > 0.");
        if (systemNoiseTemperature_K <= 0)
            throw new ArgumentOutOfRangeException(nameof(systemNoiseTemperature_K),
                "T_sys must be > 0.");

        // Eb/N0 = P_rx / (R · k · T_sys).
        double ebOverN0_linear = receivedPower_W
                               / (dataRate_bps * BoltzmannConstant_J_K
                                                 * systemNoiseTemperature_K);
        double ebOverN0_dB = 10.0 * Math.Log10(ebOverN0_linear);
        return ebOverN0_dB - requiredEbN0_dB;
    }
}
