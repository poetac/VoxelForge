// WingSparDesign.cs — Sprint AS.W1 wing-spar beam design record.
//
// Sized to bracket Cessna-172-class GA wing spars (half-span ~ 5.5 m,
// MTOW ~ 1100 kg, 3.8 g maneuver-load envelope).

using System;

namespace Voxelforge.Aerostructures;

/// <summary>
/// Design parameters for a cantilevered wing spar idealised as an
/// Euler-Bernoulli beam under tip load + uniformly-distributed lift
/// (Sprint AS.W1 scaffold). Standalone — does not integrate with the
/// <c>IEngine&lt;,,&gt;</c> stack yet.
/// </summary>
/// <param name="SectionType">Spar cross-section topology.</param>
/// <param name="Material">Spar material.</param>
/// <param name="HalfSpan_m">L [m] — root-to-tip cantilever length.</param>
/// <param name="OuterHeight_m">h [m] — section height (chord-normal).</param>
/// <param name="OuterWidth_m">b [m] — section width (chord-direction). For
/// SolidCircular sections, b is ignored and h is reinterpreted as 2·R.</param>
/// <param name="WallThickness_m">t [m] — wall thickness for hollow
/// sections. Ignored for solid sections.</param>
/// <param name="DistributedLift_Nm">w [N/m] — uniformly-distributed
/// upward lift per unit span (root-to-tip approximation; real elliptical
/// loading is left as a Wave-2 generalisation).</param>
/// <param name="LoadFactor">n [-] — maneuver load factor (1 g = level
/// flight; 3.8 g = FAR Part 23 normal-category limit).</param>
internal sealed record WingSparDesign(
    SparSectionType SectionType,
    SparMaterial Material,
    double HalfSpan_m,
    double OuterHeight_m,
    double OuterWidth_m,
    double WallThickness_m,
    double DistributedLift_Nm,
    double LoadFactor)
{
    /// <summary>
    /// Sprint AS.W2. When true, the lift distribution is modelled as
    /// elliptical (Prandtl's optimal lift distribution): w(y) = w₀ ·
    /// √(1 − (y/L)²) instead of uniform. Elliptical loading is the
    /// induced-drag-optimal shape for finite-aspect-ratio wings.
    /// Defaults to false → AS.W1 uniformly-distributed-load
    /// bit-identical behaviour.
    /// </summary>
    public bool UseEllipticalLift { get; init; } = false;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (SectionType == SparSectionType.None)
            throw new ArgumentException(
                "SectionType must be set (None sentinel is reserved).", nameof(SectionType));
        if (Material == SparMaterial.None)
            throw new ArgumentException(
                "Material must be set (None sentinel is reserved).", nameof(Material));
        if (HalfSpan_m <= 0)
            throw new ArgumentException("HalfSpan_m must be > 0.", nameof(HalfSpan_m));
        if (OuterHeight_m <= 0)
            throw new ArgumentException("OuterHeight_m must be > 0.", nameof(OuterHeight_m));
        if (SectionType != SparSectionType.SolidCircular && OuterWidth_m <= 0)
            throw new ArgumentException(
                "OuterWidth_m must be > 0 for non-circular sections.", nameof(OuterWidth_m));
        if (SectionType == SparSectionType.HollowRectangularBox)
        {
            if (WallThickness_m <= 0)
                throw new ArgumentException(
                    "WallThickness_m must be > 0 for hollow sections.",
                    nameof(WallThickness_m));
            if (2.0 * WallThickness_m >= OuterHeight_m
             || 2.0 * WallThickness_m >= OuterWidth_m)
                throw new ArgumentException(
                    "WallThickness_m must be < half of the smaller outer dimension.",
                    nameof(WallThickness_m));
        }
        if (DistributedLift_Nm <= 0)
            throw new ArgumentException("DistributedLift_Nm must be > 0.",
                nameof(DistributedLift_Nm));
        if (LoadFactor <= 0)
            throw new ArgumentException("LoadFactor must be > 0.", nameof(LoadFactor));
    }
}
