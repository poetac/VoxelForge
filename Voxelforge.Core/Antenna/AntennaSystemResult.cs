// AntennaSystemResult.cs — Sprint ANT.W5 result record for the
// statistical margin + contact-window solver. Produced by
// ElevationSweepSolver.Solve() which wraps:
//
//   1. Orbital-mechanics contact window (for an overhead circular-orbit
//      pass at altitude OrbitalAltitude_km and minimum elevation
//      ElevationAngle_deg).
//   2. Rain-margin exceedance probability from
//      LinkClosureMarginDistribution, parameterised by the user-supplied
//      0.01 %-exceedance rain rate RainRate0p01pct_mmPerHr.
//
// The data-volume fields use BandwidthOccupancy_Hz as a conservative
// 1 bit/s/Hz spectral-efficiency baseline — adequate for a link-budget
// design optimiser; replace with the actual coded spectral efficiency
// (bits/symbol × code rate) for a final-design data-volume prediction.

namespace Voxelforge.Antenna;

/// <summary>
/// Sprint ANT.W5 — combined orbital + statistical RF-link system result.
/// </summary>
/// <param name="OrbitalAltitude_km">Orbital altitude above Earth surface
/// [km] from <see cref="AntennaLinkDesign.OrbitalAltitude_km"/>.</param>
/// <param name="OrbitalPeriod_s">Keplerian orbital period [s] for a
/// circular orbit at the given altitude.</param>
/// <param name="PassesPerDay">Average number of overhead passes per day
/// = 86400 / OrbitalPeriod_s. For LEO 550 km ≈ 15.1 passes/day.</param>
/// <param name="ContactTimePerPass_s">Duration of a single overhead pass
/// where the satellite is above the minimum elevation angle [s]. For LEO
/// 550 km at 10° min elevation ≈ 478 s per pass.</param>
/// <param name="DataVolumePerPass_bits">Total data volume during one
/// contact window [bits], computed as BandwidthOccupancy_Hz ×
/// ContactTimePerPass_s (1 b/s/Hz spectral-efficiency baseline).</param>
/// <param name="DataVolumePerDay_bits">Daily accumulated data volume
/// [bits] = DataVolumePerPass_bits × PassesPerDay.</param>
/// <param name="MarginExceedanceProbability">Fraction of time the
/// <see cref="AntennaLinkResult.LinkClosureMargin_dB"/> falls below zero
/// due to rain (ITU-R P.837 power-law rain-rate CDF model). 0 if
/// <see cref="AntennaLinkDesign.RainRate0p01pct_mmPerHr"/> is 0 (clear
/// sky / no statistics). A value of 1e-4 means the link is unavailable
/// 0.01 % of time (87.6 hours/year, the standard satellite availability
/// target for commercial services).</param>
internal sealed record AntennaSystemResult(
    double OrbitalAltitude_km,
    double OrbitalPeriod_s,
    double PassesPerDay,
    double ContactTimePerPass_s,
    double DataVolumePerPass_bits,
    double DataVolumePerDay_bits,
    double MarginExceedanceProbability);
