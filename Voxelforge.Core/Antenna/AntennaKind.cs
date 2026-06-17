// AntennaKind.cs — Sprint ANT.W1 antenna-topology discriminator.
//
// Wave-1 ships three cluster topologies spanning omni → high-gain:
//
//   IdealIsotropic — reference 0 dBi gain. Used as a calibration point.
//   HalfWaveDipole — 2.15 dBi gain. Cellular / WiFi mid-range.
//   ParabolicDish  — gain = η · (π·D/λ)². Deep-space + satcom.
//
// Wave-2 (Sprint ANT.W2) adds Yagi-Uda + horn (fixed cluster gains).
//
// Wave-4 (Sprint ANT.W4) adds:
//   Helical       — parametric Kraus end-fire formula.
//   Patch         — microstrip patch fixed cluster gain.
//   CrossedDipole — circular-polarisation crossed dipole.

namespace Voxelforge.Antenna;

/// <summary>
/// Antenna topology (Sprint ANT.W1, extended in ANT.W2 and ANT.W4).
/// </summary>
internal enum AntennaKind
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>Ideal isotropic radiator — 0 dBi reference (no physical realisation).</summary>
    IdealIsotropic = 1,

    /// <summary>Half-wave dipole — 2.15 dBi gain. Cellular + WiFi cluster.</summary>
    HalfWaveDipole = 2,

    /// <summary>Parabolic reflector dish — aperture-gain formula G = η · (πD/λ)².</summary>
    ParabolicDish = 3,

    /// <summary>
    /// Yagi-Uda end-fire array (Sprint ANT.W2). Multi-element with one
    /// driven dipole + N parasitic directors + 1 reflector. Cluster
    /// mid-band gain ≈ 7 dBi (3-element) to ≈ 15 dBi (10-element).
    /// </summary>
    YagiUda = 4,

    /// <summary>
    /// Conical / pyramidal horn antenna (Sprint ANT.W2). Aperture-class
    /// like the dish but with no reflector. Cluster gain ≈ 15-25 dBi.
    /// </summary>
    Horn = 5,

    /// <summary>
    /// End-fire helical antenna (Sprint ANT.W4). Parametric Kraus formula
    /// G = 15 · N · (C/λ)² · (S/λ) [Kraus 1988 §7-4]. Circular
    /// polarisation; widely used for LEO satellite UHF/VHF up-links,
    /// GPS patch-replacement, and cubesat S-band. Gain depends on
    /// <see cref="AntennaLinkDesign.HelicalTurns"/>,
    /// <see cref="AntennaLinkDesign.HelicalCircumference_rel"/>, and
    /// <see cref="AntennaLinkDesign.HelicalTurnSpacing_rel"/>.
    /// </summary>
    Helical = 6,

    /// <summary>
    /// Microstrip patch antenna (Sprint ANT.W4). Resonant half-wavelength
    /// patch on a ground plane; gain ≈ 7–8 dBi over the azimuth
    /// half-space. Dominant topology for GPS receivers, GNSS antennas,
    /// drone telemetry, and satellite phones. Fixed cluster gain
    /// <see cref="AntennaSolver.PatchGain_dBi"/> = 7.5 dBi.
    /// </summary>
    Patch = 7,

    /// <summary>
    /// Crossed-dipole antenna (Sprint ANT.W4). Two half-wave dipoles fed
    /// in phase quadrature at 90°, producing circular polarisation in the
    /// broadside direction. Gain ≈ 2.15 dBi (same beamwidth as a single
    /// half-wave dipole; the quadrature feed adds no gain but selects one
    /// circular-polarisation sense). Used in LEO weather-sat receive
    /// (NOAA APT, Meteor-M), and as a simple ground-station antenna for
    /// circularly-polarised uplinks.
    /// </summary>
    CrossedDipole = 8,
}

