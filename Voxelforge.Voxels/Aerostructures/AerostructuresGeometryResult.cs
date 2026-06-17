// AerostructuresGeometryResult.cs — Sprint A.82 (C.2) result record returned
// by the wing-spar voxel builder. Mirrors the pattern of
// TankageGeometryResult / FlywheelGeometryResult: a small immutable summary
// of the built geometry plus the voxel handle for downstream STL / 3MF
// export.

namespace Voxelforge.Aerostructures;

/// <summary>
/// Geometry summary for a wing spar built by
/// <see cref="AerostructuresVoxelBuilder"/>. All dimensional fields are in
/// millimetres to match the PicoGK voxel-grid convention; the
/// <see cref="Voxels"/> handle wraps the underlying PicoGK voxel body so
/// callers can mesh / export without dragging PicoGK types into
/// <c>Voxelforge.Core</c>.
/// </summary>
/// <param name="SectionType">Spar cross-section topology (matches design.SectionType).</param>
/// <param name="HalfSpan_mm">L [mm] — root-to-tip span (matches design.HalfSpan_m × 1000).</param>
/// <param name="OuterHeight_mm">h [mm] — section height in the chord-normal (Z) axis.
///   For SolidCircular, this is the section diameter (2 · R).</param>
/// <param name="OuterWidth_mm">b [mm] — section width in the chord-direction (Y) axis.
///   For SolidCircular, this equals OuterHeight (the diameter; width is ignored by the design).</param>
/// <param name="WallThickness_mm">t [mm] — wall thickness for hollow sections. Zero for solid sections.</param>
/// <param name="SectionDescription">Plain-English summary of the cross-section topology
///   (e.g. "HollowRectangularBox 250 mm × 80 mm × 6 mm wall").</param>
/// <param name="IsHollowVoxelBody">True if the rendered voxel body is the HOLLOW shell
///   (only possible for HollowRectangularBox spars at the moment — axially-open ends
///   sidestep the PicoGK 2.0.0 closed-cavity flood-fill limitation). False if the body
///   is a SOLID envelope (rendered for all solid sections + when a future capped variant
///   lands).</param>
/// <param name="Voxels">PicoGK voxel handle wrapping the built wing-spar body.</param>
internal sealed record AerostructuresGeometryResult(
    SparSectionType SectionType,
    double          HalfSpan_mm,
    double          OuterHeight_mm,
    double          OuterWidth_mm,
    double          WallThickness_mm,
    string          SectionDescription,
    bool            IsHollowVoxelBody,
    IVoxelHandle    Voxels);
