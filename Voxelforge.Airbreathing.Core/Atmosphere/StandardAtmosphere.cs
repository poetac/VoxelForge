// StandardAtmosphere.cs — US Standard Atmosphere 1976.
//
// Pure data + closed-form per-layer formulas. No external deps; bit-
// identical across processes. Covers 0-86 km geometric altitude (the
// 1976 model's defined range; above 86 km the model transitions to
// statistical density tables which are out of scope for the air-
// breathing pillar — even scramjets cap below 60 km).
//
// References
// ----------
//   - U.S. Government Printing Office, "U.S. Standard Atmosphere, 1976"
//     (NOAA-S/T 76-1562), Tables 4 and 5.
//   - NASA TM-X-74335 reproduces the same model with worked examples.
//
// Layer table per the 1976 standard (geopotential altitude basis):
//
//   Index | h_b (km) | T_b (K) | L_b (K/km) | P_b (Pa)
//   ------|----------|---------|------------|-----------
//     0   |   0      |  288.15 |  −6.5      | 101 325.00
//     1   |  11      |  216.65 |   0.0      |  22 632.06
//     2   |  20      |  216.65 |  +1.0      |   5 474.89
//     3   |  32      |  228.65 |  +2.8      |     868.02
//     4   |  47      |  270.65 |   0.0      |     110.91
//     5   |  51      |  270.65 |  −2.8      |      66.939
//     6   |  71      |  214.65 |  −2.0      |       3.9564
//
// Geometric ↔ geopotential conversion:
//   h_geopot = R_E · h_geom / (R_E + h_geom),  R_E = 6 356 766 m
//
// Per-layer step formulas (ISA piecewise):
//   non-isothermal layer (L_b ≠ 0):
//     T(h)  = T_b + L_b · (h − h_b)
//     P(h)  = P_b · (T_b / T(h))^(g₀·M / (R*·L_b))
//   isothermal layer (L_b = 0):
//     T(h)  = T_b
//     P(h)  = P_b · exp(−g₀·M·(h − h_b) / (R*·T_b))

using System;

namespace Voxelforge.Airbreathing.Atmosphere;

/// <summary>
/// One atmospheric state at a given altitude. All static properties
/// (not stagnation) — applies to a parcel of air at rest in the
/// freestream.
/// </summary>
/// <param name="StaticT_K">Static temperature [K].</param>
/// <param name="StaticP_Pa">Static pressure [Pa].</param>
/// <param name="Density_kg_m3">Density [kg/m³].</param>
/// <param name="SpeedOfSound_m_s">Speed of sound a = √(γRT) [m/s] with γ = 1.40, R = 287.05 J/(kg·K).</param>
public readonly record struct AtmosphereState(
    double StaticT_K,
    double StaticP_Pa,
    double Density_kg_m3,
    double SpeedOfSound_m_s);

/// <summary>
/// US Standard Atmosphere 1976. Static (deterministic, allocation-
/// free) entry point.
/// </summary>
public static class StandardAtmosphere
{
    /// <summary>Sea-level reference temperature [K].</summary>
    public const double SeaLevelT_K = 288.15;

    /// <summary>Sea-level reference pressure [Pa].</summary>
    public const double SeaLevelP_Pa = 101_325.0;

    /// <summary>Sea-level reference density [kg/m³].</summary>
    public const double SeaLevelDensity_kg_m3 = 1.225;

    /// <summary>Standard gravity g₀ [m/s²].</summary>
    public const double G0_m_s2 = 9.80665;

    /// <summary>Specific gas constant for dry air [J/(kg·K)].</summary>
    public const double AirSpecificGasConstant_J_kg_K = 287.05287;

    /// <summary>Ratio of specific heats for air (used in <see cref="AtmosphereState.SpeedOfSound_m_s"/>).</summary>
    public const double AirGamma = 1.40;

    /// <summary>Maximum geometric altitude the model supports [m].</summary>
    public const double MaxAltitude_m = 86_000.0;

    /// <summary>Earth radius used for geopotential conversion [m].</summary>
    private const double EarthRadius_m = 6_356_766.0;

    /// <summary>Universal gas constant [J/(mol·K)] (1976 value).</summary>
    private const double Rstar_J_mol_K = 8.31432;

    /// <summary>Mean molar mass of dry air [kg/mol] (1976 value).</summary>
    private const double Mair_kg_mol = 0.0289644;

    /// <summary>g₀ · M / R*  — per-layer barometric exponent factor [K/m].</summary>
    private const double GMR = G0_m_s2 * Mair_kg_mol / Rstar_J_mol_K;

    /// <summary>
    /// Layer base data. Index = layer; entries are (h_b geopotential m,
    /// T_b K, L_b K/m, P_b Pa). Lapse rate is in K/m (NOT K/km) so the
    /// per-layer formula T(h) = T_b + L_b · (h − h_b) consumes
    /// geopotential altitude directly in metres.
    /// </summary>
    private static readonly (double H_b_m, double T_b_K, double L_b_K_per_m, double P_b_Pa)[] _layers =
    {
        (    0.0, 288.15, -0.0065,    101_325.000),
        (11_000.0, 216.65,  0.0,        22_632.060),
        (20_000.0, 216.65,  0.001,       5_474.889),
        (32_000.0, 228.65,  0.0028,        868.0187),
        (47_000.0, 270.65,  0.0,           110.9063),
        (51_000.0, 270.65, -0.0028,         66.93887),
        (71_000.0, 214.65, -0.002,           3.956420),
    };

    /// <summary>
    /// Atmospheric state at the given geometric altitude.
    /// </summary>
    /// <param name="geometricAltitude_m">
    /// Geometric altitude above mean sea level [m]. Must be in
    /// [0, <see cref="MaxAltitude_m"/>] inclusive.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Out-of-range altitude.</exception>
    public static AtmosphereState At(double geometricAltitude_m)
    {
        if (double.IsNaN(geometricAltitude_m) || geometricAltitude_m < 0.0 || geometricAltitude_m > MaxAltitude_m)
            throw new ArgumentOutOfRangeException(nameof(geometricAltitude_m),
                $"Altitude {geometricAltitude_m} m outside the US 1976 model range [0, {MaxAltitude_m}] m.");

        double hGeopot = GeopotentialAltitude_m(geometricAltitude_m);
        int layer = LayerIndex(hGeopot);
        var (hB, tB, lB, pB) = _layers[layer];

        double t, p;
        if (lB == 0.0)
        {
            t = tB;
            p = pB * Math.Exp(-GMR * (hGeopot - hB) / tB);
        }
        else
        {
            t = tB + lB * (hGeopot - hB);
            p = pB * Math.Pow(tB / t, GMR / lB);
        }

        double rho = p / (AirSpecificGasConstant_J_kg_K * t);
        double a   = Math.Sqrt(AirGamma * AirSpecificGasConstant_J_kg_K * t);

        return new AtmosphereState(
            StaticT_K:        t,
            StaticP_Pa:       p,
            Density_kg_m3:    rho,
            SpeedOfSound_m_s: a);
    }

    /// <summary>
    /// Convert geometric to geopotential altitude. Geopotential
    /// altitude scales the geometric altitude to account for gravity's
    /// inverse-square decrease with radius from Earth's centre.
    /// </summary>
    public static double GeopotentialAltitude_m(double geometricAltitude_m)
        => EarthRadius_m * geometricAltitude_m / (EarthRadius_m + geometricAltitude_m);

    private static int LayerIndex(double hGeopot)
    {
        // _layers is small + monotone; linear scan beats binary search
        // for cache reasons + has no off-by-one risk.
        for (int i = _layers.Length - 1; i >= 0; --i)
        {
            if (hGeopot >= _layers[i].H_b_m) return i;
        }
        return 0;
    }
}
