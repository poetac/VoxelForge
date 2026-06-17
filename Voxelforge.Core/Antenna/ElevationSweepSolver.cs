// ElevationSweepSolver.cs — Sprint ANT.W5 orbital contact-window solver.
//
// Computes the contact-window duration for a single overhead pass of a
// satellite in a circular orbit at altitude h, given a ground-station
// minimum elevation mask θ_min (the ElevationAngle_deg field on the
// design). Uses two-body Earth orbital mechanics.
//
// Geometry derivation (overhead-pass model):
//
//   Three-body triangle: Earth centre (E), sub-satellite point (S),
//   ground station (G). Angles are:
//
//     ρ  = elevation angle at G (above local horizon). For contact,
//          ρ ≥ θ_min.
//     η  = nadir angle at S (angle S-E-G seen from the satellite).
//     λ  = Earth central angle (E-G arc, measured at E). λ = 90° − ρ − η.
//
//   From the law of sines in triangle E-S-G:
//     (R_E + h) / sin(90° + ρ) = R_E / sin η
//     → sin η = R_E · cos ρ / (R_E + h)
//
//   At minimum elevation ρ = θ_min:
//     η_max = arcsin(R_E · cos(θ_min) / (R_E + h))
//     λ_max = 90° − θ_min − η_max  [Earth central angle at edge of contact]
//
//   Contact half-arc = λ_max [rad]; total arc = 2 · λ_max.
//   Contact time for an overhead pass:
//     T_contact = 2 · λ_max / (2π) · T_orbital
//
// Statistical rain-margin exceedance is delegated to
// LinkClosureMarginDistribution.ComputeExceedanceProbability.
//
// Data-volume baseline uses BandwidthOccupancy_Hz as a 1 b/s/Hz proxy.
// For accurate throughput, multiply by the actual spectral efficiency
// (bits/symbol × FEC code rate).
//
// References:
//   Wertz J.R., Larson W.J. (1999). "Space Mission Engineering," §9.5.
//   Bate R., Mueller D., White J. (1971). "Fundamentals of Astrodynamics,"
//     §2.8 (two-body orbit).
//   ITU-R P.618-13 (2017), Annex 1 §1.3 (slant-path geometry basis).

using System;

namespace Voxelforge.Antenna;

/// <summary>
/// Sprint ANT.W5 — two-body orbital contact-window + statistical link
/// margin solver.
/// </summary>
internal static class ElevationSweepSolver
{
    /// <summary>Earth gravitational parameter GM [m³/s²] (IERS 2010).</summary>
    internal const double GravitationalParameter_m3s2 = 3.986004418e14;

    /// <summary>Earth mean radius R_E [m] (IUGG 2015).</summary>
    internal const double EarthRadius_m = 6_371_000.0;

    /// <summary>Seconds per sidereal day [s].</summary>
    private const double SecondsPerDay = 86_400.0;

    /// <summary>
    /// Solve the orbital contact window and statistical link margin for
    /// <paramref name="design"/>. The minimum elevation angle is taken
    /// from <see cref="AntennaLinkDesign.ElevationAngle_deg"/>.
    /// </summary>
    /// <param name="design">Validated antenna link design with
    ///   <see cref="AntennaLinkDesign.OrbitalAltitude_km"/> ≥ 160 km
    ///   (LEO minimum) and all other fields in their valid ranges.</param>
    /// <returns><see cref="AntennaSystemResult"/> with orbital period,
    ///   contact time per pass, data volumes, and rain-margin exceedance
    ///   probability.</returns>
    /// <exception cref="ArgumentNullException">design is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   OrbitalAltitude_km ≤ 0.</exception>
    internal static AntennaSystemResult Solve(AntennaLinkDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        if (design.OrbitalAltitude_km <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"OrbitalAltitude_km={design.OrbitalAltitude_km:F1} must be > 0.");

        double h = design.OrbitalAltitude_km * 1000.0;   // altitude [m]
        double a = EarthRadius_m + h;                     // semi-major axis [m]

        // Keplerian orbital period [s].
        double T_orb = 2.0 * Math.PI * Math.Sqrt(a * a * a / GravitationalParameter_m3s2);

        // Contact-window geometry for an overhead pass.
        double theta_min = design.ElevationAngle_deg * Math.PI / 180.0;  // [rad]
        double sinEta = Math.Min(1.0, EarthRadius_m * Math.Cos(theta_min) / a);
        double eta    = Math.Asin(sinEta);                       // nadir angle [rad]
        double lambda = Math.Max(0.0, Math.PI / 2.0 - theta_min - eta); // Earth central angle [rad]

        double contactTime_s = 2.0 * lambda / (2.0 * Math.PI) * T_orb;

        double passesPerDay = SecondsPerDay / T_orb;

        // Data-volume estimate: 1 b/s/Hz conservative baseline.
        double dataRate_bps    = design.BandwidthOccupancy_Hz;
        double dataPerPass_bits = dataRate_bps * contactTime_s;
        double dataPerDay_bits  = dataPerPass_bits * passesPerDay;

        double exceedance = LinkClosureMarginDistribution
            .ComputeExceedanceProbability(design);

        return new AntennaSystemResult(
            OrbitalAltitude_km:             design.OrbitalAltitude_km,
            OrbitalPeriod_s:                T_orb,
            PassesPerDay:                   passesPerDay,
            ContactTimePerPass_s:           contactTime_s,
            DataVolumePerPass_bits:         dataPerPass_bits,
            DataVolumePerDay_bits:          dataPerDay_bits,
            MarginExceedanceProbability:    exceedance);
    }
}
