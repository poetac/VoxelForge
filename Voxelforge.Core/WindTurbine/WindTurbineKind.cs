// WindTurbineKind.cs — Sprint WT.W1 wind-turbine sub-classifier.
//
// Wave-1 ships the horizontal-axis wind turbine (HAWT) — the dominant
// commercial topology. Wave-2+ will add vertical-axis (VAWT, Darrieus
// + Savonius), ducted/diffuser-augmented, and tidal-axial geometries.
//
// HAWT cluster (rotor diameter D, rated power P):
//   Small (residential):   D ~ 3-10 m,   P ~ 1-10 kW
//   Mid (commercial):      D ~ 50-90 m,  P ~ 1-3 MW
//   Large (utility):       D ~ 120-180 m, P ~ 4-15 MW (NREL 5MW reference;
//                                                       IEA 15MW reference)
//   Offshore (largest):    D ~ 230 m,    P ~ 18-20 MW (GE Haliade-X class)

namespace Voxelforge.WindTurbine;

/// <summary>
/// Sub-classification of wind turbine within the wind-turbine pillar
/// (Sprint WT.W1 scaffold). Wave-1 ships HorizontalAxis only.
/// </summary>
internal enum WindTurbineKind
{
    /// <summary>Degenerate sentinel — not a valid design kind.</summary>
    None = 0,

    /// <summary>
    /// Horizontal-axis wind turbine (HAWT). Rotor blades sweep a disk
    /// normal to the wind direction. Dominant commercial topology
    /// (~ 99 % of installed utility-scale capacity). Wave-1 baseline
    /// sized against the NREL 5 MW reference (Jonkman et al. 2009).
    /// </summary>
    HorizontalAxis = 1,

    /// <summary>
    /// Vertical-axis wind turbine (Darrieus / H-rotor cluster, Sprint
    /// WT.W2). Lower peak C_p (≈ 0.40 vs ≈ 0.48 HAWT) at lower λ_peak
    /// (≈ 5.0 vs ≈ 7.5). Omnidirectional (no yaw system needed) +
    /// lower noise / visual impact. Urban + small-scale market cluster.
    /// </summary>
    VerticalAxis = 2,
}
