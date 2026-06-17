// ReceiverSensitivityCalculator.cs — Sprint ANT.W3 receiver sensitivity
// floor + thermal-noise floor helpers.
//
// Closed-form, allocation-free, deterministic. Implements the canonical
// thermal-noise-floor formula:
//
//   N_floor [W] = k_B · T_sys · BW                      (single-sided PSD)
//   N_floor [dBm] = 10·log10(k_B · T_sys · BW) + NF_dB + 30
//
//   Sensitivity_dBm = N_floor_dBm + RequiredEbN0_dB
//
// The +30 converts dBW → dBm (10·log10(1000) = 30). The NF_dB term
// (receiver noise figure) folds in the loss of the LNA + downconverter
// stages above the antenna-temperature floor. RequiredEbN0_dB comes
// from ModulationSchemeTable; together they pin the minimum P_rx the
// receiver can demodulate at the chosen scheme's BER target.
//
// References:
//   Sklar B. (2001). Digital Communications, 2nd ed., §4.2 (thermal noise).
//   Friis H. T. (1944). "Noise figures of radio receivers." Proc. IRE 32.
//   Proakis J. (2007). Digital Communications, 5th ed., Eq. 2.2-18.

using System;

namespace Voxelforge.Antenna;

/// <summary>
/// Receiver sensitivity + thermal-noise-floor calculator (Sprint ANT.W3).
/// </summary>
internal static class ReceiverSensitivityCalculator
{
    /// <summary>
    /// Thermal-noise floor at the receiver input [dBm].
    /// <para>
    /// <c>N_floor_dBm = 10·log10(k_B · T_sys · BW) + NF_dB + 30</c>
    /// </para>
    /// </summary>
    /// <param name="systemNoiseTemperature_K">T_sys [K] — combined
    /// antenna + LNA + cable contributions.</param>
    /// <param name="bandwidth_Hz">BW [Hz] — receiver noise bandwidth.</param>
    /// <param name="noiseFigure_dB">NF [dB] — receiver noise figure
    /// (additional loss above the kT·BW thermal floor).</param>
    internal static double ThermalNoiseFloor_dBm(
        double systemNoiseTemperature_K,
        double bandwidth_Hz,
        double noiseFigure_dB)
    {
        if (systemNoiseTemperature_K <= 0)
            throw new ArgumentOutOfRangeException(nameof(systemNoiseTemperature_K),
                "T_sys must be > 0.");
        if (bandwidth_Hz <= 0)
            throw new ArgumentOutOfRangeException(nameof(bandwidth_Hz),
                "Bandwidth must be > 0.");
        if (double.IsNaN(noiseFigure_dB))
            throw new ArgumentOutOfRangeException(nameof(noiseFigure_dB),
                "Noise figure must be a real number (was NaN).");

        // N [W] = k_B · T_sys · BW; convert W → dBW → dBm (+30).
        double n_W   = AntennaSolver.BoltzmannConstant_J_K
                     * systemNoiseTemperature_K
                     * bandwidth_Hz;
        double n_dBm = 10.0 * Math.Log10(n_W) + 30.0 + noiseFigure_dB;
        return n_dBm;
    }

    /// <summary>
    /// Receiver sensitivity floor [dBm] — the lowest received signal
    /// power at which the chosen modulation can be demodulated at its
    /// target BER.
    /// <para>
    /// <c>Sensitivity_dBm = N_floor_dBm + RequiredEbN0_dB</c>
    /// </para>
    /// where <c>N_floor</c> includes the receiver noise figure.
    /// </summary>
    /// <param name="systemNoiseTemperature_K">T_sys [K].</param>
    /// <param name="bandwidth_Hz">BW [Hz].</param>
    /// <param name="noiseFigure_dB">NF [dB].</param>
    /// <param name="requiredEbN0_dB">Required Eb/N₀ [dB] for the
    /// selected modulation + FEC (see <see cref="ModulationSchemeTable.RequiredEbN0_dB"/>).</param>
    internal static double Sensitivity_dBm(
        double systemNoiseTemperature_K,
        double bandwidth_Hz,
        double noiseFigure_dB,
        double requiredEbN0_dB)
    {
        if (double.IsNaN(requiredEbN0_dB))
            throw new ArgumentOutOfRangeException(nameof(requiredEbN0_dB),
                "Required Eb/N0 must be a real number (was NaN).");
        return ThermalNoiseFloor_dBm(
                   systemNoiseTemperature_K, bandwidth_Hz, noiseFigure_dB)
             + requiredEbN0_dB;
    }
}
