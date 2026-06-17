// AntennaLinkResult.cs — Sprint ANT.W1 solver output.
// Sprint ANT.W3: added ReceiverSensitivity_dBm + RequiredEbN0_dB +
// AchievedEbN0_dB so the link-budget snapshot reports the modulation/
// FEC-driven sensitivity floor + how much margin the link clears it by.
// Sprint ANT.W2: added RainAttenuation_dB + AtmosphericAbsorption_dB +
// SystemLoss_dB + LinkClosureMargin_dB. ReceivedPower_dBm remains the
// Friis-only value (backwards compatible); LinkClosureMargin_dB is the
// full link-budget gate: P_rx_dBm − SystemLoss_dB − ReceiverSensitivity_dBm.

namespace Voxelforge.Antenna;

/// <summary>
/// Solve-time outputs for an RF-link snapshot (Sprint ANT.W1, extended
/// in ANT.W3).
/// </summary>
/// <param name="Wavelength_m">λ = c / f [m].</param>
/// <param name="TransmitAntennaGain_dBi">G_tx [dBi].</param>
/// <param name="ReceiveAntennaGain_dBi">G_rx [dBi].</param>
/// <param name="EffectiveIsotropicRadiatedPower_dBW">EIRP_dBW = 10·log10(P_tx)
/// + G_tx_dBi [dBW].</param>
/// <param name="FreeSpacePathLoss_dB">FSPL_dB = 20·log10(4πR/λ) [dB].</param>
/// <param name="ReceivedPower_dBm">P_rx_dBm = EIRP_dBW + G_rx_dBi −
/// FSPL_dB + 30 [dBm].</param>
/// <param name="ReceivedPower_W">P_rx [W] from the dBm result.</param>
/// <param name="ReceiverSensitivity_dBm">ANT.W3 — receiver sensitivity
/// floor [dBm] = N_floor + RequiredEbN0_dB, the lowest P_rx at which
/// the selected modulation closes at its BER target.</param>
/// <param name="RequiredEbN0_dB">ANT.W3 — required Eb/N₀ [dB] from
/// <see cref="ModulationSchemeTable.RequiredEbN0_dB"/> for the design's
/// <see cref="AntennaLinkDesign.Modulation"/>.</param>
/// <param name="AchievedEbN0_dB">ANT.W3 — achieved Eb/N₀ [dB] computed
/// from the received-power-vs-thermal-noise-floor ratio at the design's
/// bandwidth occupancy. The link closes when this exceeds
/// <paramref name="RequiredEbN0_dB"/>.</param>
/// <param name="RainAttenuation_dB">ANT.W2 — rain slant-path attenuation
/// [dB] from ITU-R P.838-3 + P.618-13. 0 when RainRate = 0 (clear sky).</param>
/// <param name="AtmosphericAbsorption_dB">ANT.W2 — atmospheric gas
/// absorption (O₂ + H₂O) on the slant path [dB] from ITU-R P.676-12,
/// ICAO standard sea-level atmosphere / sin(elevation).</param>
/// <param name="SystemLoss_dB">ANT.W2 — total system propagation and
/// hardware loss [dB] = RainAttenuation + AtmosphericAbsorption +
/// PointingLoss + PolarisationMismatch + CableConnectorLoss.</param>
/// <param name="LinkClosureMargin_dB">ANT.W2 — link closure margin [dB]
/// = ReceivedPower_dBm − SystemLoss_dB − ReceiverSensitivity_dBm.
/// Positive = link closed; negative = link fails by that margin.</param>
internal sealed record AntennaLinkResult(
    double Wavelength_m,
    double TransmitAntennaGain_dBi,
    double ReceiveAntennaGain_dBi,
    double EffectiveIsotropicRadiatedPower_dBW,
    double FreeSpacePathLoss_dB,
    double ReceivedPower_dBm,
    double ReceivedPower_W,
    double ReceiverSensitivity_dBm,
    double RequiredEbN0_dB,
    double AchievedEbN0_dB,
    double RainAttenuation_dB,
    double AtmosphericAbsorption_dB,
    double SystemLoss_dB,
    double LinkClosureMargin_dB);
