// HybridFuelProperties.cs — Sprint R.W2 per-fuel property registry.
//
// Holds the cluster-anchored regression-rate constants + density per
// solid-grain fuel type. Mirrors the pattern used by NuclearFuelMaterial
// + UraniumEnrichmentTiers in the Nuclear pillar.
//
// Sources:
//   Karabeyoglu A., Cantwell B.J., Altman D. (2003). "Regression Rate
//     Modeling of Hybrid Rockets." AIAA-2003-4506.  → HTPB / LOX
//     fit values.
//   Karabeyoglu A. et al. (2005). "Combustion of Liquefying Hybrid
//     Propellants: Part 1, Theory." J. Prop. & Power, 22 (2). →
//     Paraffin high-regression mechanism.

using System;

namespace Voxelforge.Hybrid;

/// <summary>
/// Cluster-anchored regression + density properties for a hybrid fuel.
/// Pure data; no thermo or kinetics. Sprint R.W2 baseline.
/// </summary>
/// <param name="Density_kgm3">Solid-grain density [kg/m³].</param>
/// <param name="MarxmanA">
/// Marxman regression-rate coefficient a [m/s · (kg/(m²·s))^-n]. The
/// fit is <c>r_dot = a · G_ox^n</c>. For LOX/HTPB: a = 1.37e-4 (i.e.
/// r_dot in mm/s = 0.137·G_ox^0.681, the canonical Karabeyoglu fit).
/// </param>
/// <param name="MarxmanN">
/// Marxman regression-rate exponent n [-]. ~0.68 for both HTPB and
/// paraffin — the empirical fit anchors the same power law; paraffin
/// shows higher a (entrainment mechanism) but similar n.
/// </param>
internal sealed record HybridFuelProperties(
    double Density_kgm3,
    double MarxmanA,
    double MarxmanN);

/// <summary>
/// Static registry of hybrid-fuel properties.
/// </summary>
internal static class HybridFuelRegistry
{
    /// <summary>HTPB cluster-anchored — Karabeyoglu et al. 2003.</summary>
    internal static readonly HybridFuelProperties HTPB =
        new(Density_kgm3: 920.0,
            MarxmanA:     1.37e-4,
            MarxmanN:     0.681);

    /// <summary>
    /// Paraffin cluster-anchored. The entrainment mechanism makes the
    /// effective <c>a</c> roughly 3× HTPB at the same G_ox; n ≈ 0.62
    /// (slightly weaker G_ox dependence — Karabeyoglu et al. 2005 fit).
    /// </summary>
    internal static readonly HybridFuelProperties Paraffin =
        new(Density_kgm3: 920.0,
            MarxmanA:     4.10e-4,
            MarxmanN:     0.620);

    /// <summary>Resolve fuel-properties from the enum.</summary>
    internal static HybridFuelProperties For(HybridFuel fuel) => fuel switch
    {
        HybridFuel.HTPB     => HTPB,
        HybridFuel.Paraffin => Paraffin,
        _                   => throw new ArgumentOutOfRangeException(nameof(fuel),
                                   fuel, $"Unknown HybridFuel '{fuel}'."),
    };
}
