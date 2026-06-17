// ItuAtmosphericModels.cs — Sprint ANT.W2 ITU-R propagation-loss models.
//
// Two ITU-R Recommendations:
//
//   ITU-R P.838-3 (2005) — specific rain attenuation.
//     Table 1 k_H / α_H values for horizontal polarization;
//     log-log frequency interpolation.
//     γ_R = k_H · R^α_H  [dB/km].
//
//   ITU-R P.618-13 (2017) §2.2.1 — slant-path effective length.
//     Horizontal reduction factor r₀ = 1/(1 + L_G/d₀),
//     d₀ = 35·exp(−0.015·R) [km].
//
//   ITU-R P.676-12 (2019) — atmospheric absorption.
//     Total zenith path attenuation (O₂ + H₂O) at ICAO standard
//     sea-level atmosphere (1013.25 hPa, 15 °C, 7.5 g/m³ vapour).
//     Table derived from P.676-12 Fig. 1 and Annex 2 formulas.
//     Slant path: A = A_zenith / sin(elevation), valid for el ≥ 5°.
//
// References:
//   ITU-R P.838-3 (2005), Table 1.
//   ITU-R P.618-13 (2017), Annex 1 §1.3.
//   ITU-R P.676-12 (2019), Annex 2 + Fig. 1.

using System;

namespace Voxelforge.Antenna;

/// <summary>
/// Sprint ANT.W2 — tabulated ITU-R P.838-3 and P.676-12 propagation-loss
/// models for the link-budget solver.
/// </summary>
internal static class ItuAtmosphericModels
{
    // ── ITU-R P.838-3 Table 1 — horizontal polarization ─────────────────
    // γ_R = k_H · R^α_H  [dB/km].  Valid 1–1000 GHz; table covers
    // 1–100 GHz (the satellite / deep-space band of interest here).
    private static readonly double[] s_fRain_GHz =
    {
          1,   2,   4,   6,   7,   8,  10,  12,  15,  20,
         25,  30,  35,  40,  45,  50,  60,  70,  80,  90, 100
    };

    private static readonly double[] s_kH =
    {
        3.87e-5, 1.54e-4, 6.50e-4, 1.75e-3, 3.01e-3, 4.54e-3,
        1.01e-2, 1.88e-2, 3.67e-2, 7.51e-2, 1.24e-1, 1.87e-1,
        2.63e-1, 3.50e-1, 4.42e-1, 5.36e-1, 7.07e-1, 8.51e-1,
        9.75e-1, 1.06e0,  1.12e0
    };

    private static readonly double[] s_alphaH =
    {
        0.912, 0.963, 1.121, 1.308, 1.332, 1.327,
        1.276, 1.217, 1.154, 1.099, 1.061, 1.021,
        0.979, 0.939, 0.903, 0.873, 0.826, 0.793,
        0.769, 0.753, 0.743
    };

    // Rain height above MSL [km] — midlatitude mean 0 °C isotherm.
    // ITU-R P.839-4 gives a latitude-dependent value; 3.0 km is the
    // standard for latitudes 20–60° (Europe, USA, Japan).
    private const double RainHeight_km = 3.0;

    // ── ITU-R P.676-12 zenith path attenuation ───────────────────────────
    // Total zenith attenuation (O₂ + H₂O) at sea level, ICAO standard
    // atmosphere (1013.25 hPa, 15 °C, 7.5 g/m³ water vapour).
    // Derived from P.676-12 Fig. 1 and Annex 2 simplified formulas.
    // Uncertainty near the 22.235 GHz H₂O and 60 GHz O₂ bands is ±20%
    // (chart read-off); adequate for link-budget design optimisation.
    private static readonly double[] s_fAtm_GHz =
    {
         1,     2,     4,     6,     8,    10,    12,    15,
        18,    20,   22.235, 25,    30,    35,    40,
        45,    50,    54,    57,    60,    64,    66,
        70,    80,    90,   100
    };

    private static readonly double[] s_zenithAtm_dB =
    {
        0.020, 0.021, 0.021, 0.022, 0.024, 0.030, 0.034, 0.041,
        0.090, 0.200, 0.460, 0.105, 0.100, 0.120, 0.155,
        0.700, 1.400, 5.500, 15.0,  17.0,  4.5,   1.5,
        0.470, 0.360, 0.390, 0.450
    };

    /// <summary>
    /// Sprint ANT.W2. Specific rain attenuation [dB/km] at the given
    /// frequency and rain rate (ITU-R P.838-3 Table 1 horizontal
    /// polarization, log-log frequency interpolation).
    /// </summary>
    /// <param name="frequency_Hz">Carrier frequency [Hz].</param>
    /// <param name="rainRate_mmPerHr">Rain rate R₀ [mm/hr]. 0 returns 0
    /// (clear sky). Representative values: 5 (light), 25 (heavy),
    /// 50 (intense — ITU-R rain-zone P).</param>
    /// <returns>γ_R = k_H · R^α_H [dB/km].</returns>
    internal static double SpecificRainAttenuation_dB_per_km(
        double frequency_Hz,
        double rainRate_mmPerHr)
    {
        if (rainRate_mmPerHr <= 0.0) return 0.0;
        double f_GHz = frequency_Hz / 1e9;
        (double k, double alpha) = InterpolateKAlpha(f_GHz);
        return k * Math.Pow(rainRate_mmPerHr, alpha);
    }

    /// <summary>
    /// Sprint ANT.W2. Total rain slant-path attenuation [dB], combining
    /// ITU-R P.838-3 specific attenuation with the ITU-R P.618-13 §1.3
    /// horizontal path-reduction factor.
    /// </summary>
    /// <param name="frequency_Hz">Carrier frequency [Hz].</param>
    /// <param name="elevation_deg">Slant-path elevation above horizon [°].
    /// Clamped to ≥ 5° (flat-earth model breaks down below 5°).</param>
    /// <param name="rainRate_mmPerHr">Rain rate [mm/hr]. 0 returns 0.</param>
    internal static double RainSlantPathAttenuation_dB(
        double frequency_Hz,
        double elevation_deg,
        double rainRate_mmPerHr)
    {
        if (rainRate_mmPerHr <= 0.0) return 0.0;

        double el_rad  = DegreesToRadians(Math.Max(5.0, elevation_deg));
        double gamma_R = SpecificRainAttenuation_dB_per_km(
            frequency_Hz, rainRate_mmPerHr);

        // Slant-path length through the rain layer [km].
        double L_S = RainHeight_km / Math.Sin(el_rad);
        // Horizontal projection [km].
        double L_G = L_S * Math.Cos(el_rad);

        // P.618-13 Annex 1 §1.3 reduction factor:
        //   r₀ = 1 / (1 + L_G / d₀),   d₀ = 35·exp(−0.015·R)  [km].
        double d0 = 35.0 * Math.Exp(-0.015 * rainRate_mmPerHr);
        double r0 = 1.0 / (1.0 + L_G / d0);

        return gamma_R * L_S * r0;
    }

    /// <summary>
    /// Sprint ANT.W2. Total atmospheric absorption on a slant path [dB]
    /// (O₂ + H₂O) using a tabulated zenith attenuation from ITU-R
    /// P.676-12 for the ICAO standard sea-level atmosphere, divided by
    /// sin(elevation). Valid for elevation ≥ 5°.
    /// </summary>
    /// <param name="frequency_Hz">Carrier frequency [Hz].</param>
    /// <param name="elevation_deg">Slant-path elevation [°]. Clamped ≥ 5°.</param>
    internal static double AtmosphericAbsorption_dB(
        double frequency_Hz,
        double elevation_deg)
    {
        double el_rad = DegreesToRadians(Math.Max(5.0, elevation_deg));
        double zenith = InterpolateAtmZenith(frequency_Hz / 1e9);
        return zenith / Math.Sin(el_rad);
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private static (double k, double alpha) InterpolateKAlpha(double f_GHz)
    {
        int n = s_fRain_GHz.Length;
        if (f_GHz <= s_fRain_GHz[0])     return (s_kH[0],     s_alphaH[0]);
        if (f_GHz >= s_fRain_GHz[n - 1]) return (s_kH[n - 1], s_alphaH[n - 1]);

        int i = BinarySearchFloor(s_fRain_GHz, f_GHz);
        double t = (Math.Log10(f_GHz)          - Math.Log10(s_fRain_GHz[i]))
                 / (Math.Log10(s_fRain_GHz[i + 1]) - Math.Log10(s_fRain_GHz[i]));
        // Log-log interpolation for k; linear for α (varies near-linearly).
        double log10k = (1.0 - t) * Math.Log10(s_kH[i]) + t * Math.Log10(s_kH[i + 1]);
        double alpha  = (1.0 - t) * s_alphaH[i]         + t * s_alphaH[i + 1];
        return (Math.Pow(10.0, log10k), alpha);
    }

    private static double InterpolateAtmZenith(double f_GHz)
    {
        int n = s_fAtm_GHz.Length;
        if (f_GHz <= s_fAtm_GHz[0])     return s_zenithAtm_dB[0];
        if (f_GHz >= s_fAtm_GHz[n - 1]) return s_zenithAtm_dB[n - 1];

        int i = BinarySearchFloor(s_fAtm_GHz, f_GHz);
        double t = (f_GHz - s_fAtm_GHz[i])
                 / (s_fAtm_GHz[i + 1] - s_fAtm_GHz[i]);
        // Log-linear interpolation (zenith attenuation spans orders of
        // magnitude near the O₂ and H₂O resonance bands).
        double logZ = (1.0 - t) * Math.Log10(s_zenithAtm_dB[i])
                    + t         * Math.Log10(s_zenithAtm_dB[i + 1]);
        return Math.Pow(10.0, logZ);
    }

    // Largest index i such that arr[i] <= val. Pre-condition: arr[0] <= val < arr[^1].
    private static int BinarySearchFloor(double[] arr, double val)
    {
        int lo = 0, hi = arr.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (arr[mid] <= val) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    private static double DegreesToRadians(double deg) => Math.PI * deg / 180.0;
}
