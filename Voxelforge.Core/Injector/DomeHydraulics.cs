// DomeHydraulics.cs — Propellant inlet dome pressure-loss model.
//
// The injector dome is the axisymmetric plenum behind the injector
// face that distributes one propellant (ox or fuel) onto the back side
// of every element. Flow enters through an inlet port on the apex
// (or the side, depending on preset), turns 90°, expands into the
// dome cavity, and exits through the injector orifices.
//
// Two loss mechanisms matter:
//   1. Sudden expansion from inlet port area A_in to dome cross-
//      section A_dome. Borda–Carnot limit: ΔP ≈ ½·ρ·(v_in − v_dome)².
//   2. Distribution loss — the last (outermost) element is farther
//      from the inlet than the first, so there's a small-Δ tangential
//      flow at the dome circumference. Empirical coefficient
//      K_dist ≈ 0.5 · ½·ρ·v_dome² (Huzel §5).
//
// Optional anti-vortex baffle: a radial plate near the apex that
// kills swirl. Adds ~0.3·½·ρ·v_in² but improves distribution
// uniformity (not modelled here; caller benefits from it via the
// Rupe uniformity soft-penalty in the scoring profile).
//
// This module replaces the 1.5-velocity-head hard-coded placeholder
// that the legacy PressureStackup used for the dome term.
//
// References:
//   Huzel &amp; Huang AIAA Vol. 147 §5.2 ("Propellant Manifold Sizing");
//   Idelchik, "Handbook of Hydraulic Resistance", §4 (sudden expansion).

namespace Voxelforge.Injector;

/// <summary>
/// Minimum physical description of an injector dome for the
/// hydraulics / voxel-builder handshake. DomeDepth is the axial
/// extent of the plenum behind the injector face; DomeRadius is
/// typically ≈ chamber radius but can be larger or smaller per
/// design choice. InletDiameter is the cross-section of the feed
/// line where it enters the dome.
/// </summary>
public readonly record struct DomeSpec(
    double DomeDepth_mm,
    double DomeRadius_mm,
    double InletDiameter_mm,
    bool   IncludeAntiVortexBaffle);

/// <summary>
/// Output of <see cref="DomeHydraulics.Compute"/>.
/// </summary>
public readonly record struct DomeLossResult(
    double ExpansionDP_Pa,
    double DistributionDP_Pa,
    double BaffleDP_Pa,
    double TotalDP_Pa,
    double InletVelocity_ms,
    double DomeVelocity_ms);

public static class DomeHydraulics
{
    /// <summary>
    /// Distribution-loss coefficient applied to the dome-outlet velocity
    /// head. 0.5 matches Huzel for a smoothly-faired dome with no
    /// sharp corners; bump higher for rectangular boxes with sudden
    /// 90° turns.
    /// </summary>
    public const double DistributionK = 0.5;

    /// <summary>Anti-vortex baffle adds ~0.3 velocity heads (Huzel §5.2.3).</summary>
    public const double BaffleK = 0.3;

    public static DomeLossResult Compute(DomeSpec spec, double massFlow_kgs, double density_kgm3)
    {
        if (massFlow_kgs <= 0 || density_kgm3 <= 0)
            return new DomeLossResult(0, 0, 0, 0, 0, 0);

        double A_in = System.Math.PI * (spec.InletDiameter_mm * 1e-3) * (spec.InletDiameter_mm * 1e-3) / 4.0;
        // Dome cross-section approximated as the cylindrical plenum at
        // the nominal depth × 2πR — the flow expands radially.
        // Using an annular-like effective area gives a more realistic
        // ratio than just π R². A_dome = 2πR · min(depth, R) caps the
        // expansion at a square-ish plenum.
        double R_dome_m = spec.DomeRadius_mm * 1e-3;
        double depth_m  = System.Math.Max(spec.DomeDepth_mm * 1e-3, 1e-4);
        double A_dome = 2.0 * System.Math.PI * R_dome_m * System.Math.Min(depth_m, R_dome_m);
        A_in   = System.Math.Max(A_in,   1e-8);
        A_dome = System.Math.Max(A_dome, 1e-8);

        double v_in   = massFlow_kgs / (density_kgm3 * A_in);
        double v_dome = massFlow_kgs / (density_kgm3 * A_dome);

        // Borda–Carnot sudden expansion.
        double expansionDP = 0.5 * density_kgm3 * (v_in - v_dome) * (v_in - v_dome);

        // Distribution loss, referenced to dome-outlet velocity.
        double distDP = DistributionK * 0.5 * density_kgm3 * v_dome * v_dome;

        // Optional baffle, referenced to inlet velocity (that's where
        // the baffle intercepts the incoming jet).
        double baffleDP = spec.IncludeAntiVortexBaffle
            ? BaffleK * 0.5 * density_kgm3 * v_in * v_in
            : 0.0;

        double totalDP = expansionDP + distDP + baffleDP;

        return new DomeLossResult(
            ExpansionDP_Pa:   expansionDP,
            DistributionDP_Pa: distDP,
            BaffleDP_Pa:       baffleDP,
            TotalDP_Pa:        totalDP,
            InletVelocity_ms:  v_in,
            DomeVelocity_ms:   v_dome);
    }
}
