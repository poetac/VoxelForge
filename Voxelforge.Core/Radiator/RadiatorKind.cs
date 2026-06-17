// RadiatorKind.cs — Sprint RAD.W1 spacecraft radiator sub-classifier.
//
// Wave-1 ships flat-plate panel radiators — the dominant LEO + GEO
// commercial topology (ISS Active Thermal Control System, Iridium NEXT
// bus, GOES-R weather sat). Wave-2+ will add deployable / louvered /
// loop-heat-pipe / pumped-fluid-loop variants.

namespace Voxelforge.Radiator;

/// <summary>
/// Sub-classification of spacecraft radiator (Sprint RAD.W1).
/// </summary>
internal enum RadiatorKind
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>
    /// Flat-plate body-mounted or deployable panel. Stefan-Boltzmann
    /// radiation balance against deep-space + Earth-IR + albedo loads.
    /// </summary>
    FlatPanel = 1,

    /// <summary>
    /// Two-sided deployable radiator (Sprint RAD.W2). Radiates from
    /// both faces of the panel — typical for ISS-style deployable
    /// wings + EELV upper-stage radiators. Effective area is 2× the
    /// PanelArea_m2 field. Parasitic solar absorption is reported only
    /// for the sun-facing side (caller specifies via incident flux).
    /// </summary>
    TwoSidedDeployable = 2,
}
