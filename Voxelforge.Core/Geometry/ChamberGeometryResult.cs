namespace Voxelforge.Geometry;

/// <summary>
/// Result of ChamberVoxelBuilder.Build — the voxel body + summary scalars.
/// <para>
/// A1 (Core extraction) introduced this record carrying an <c>object</c>
/// for the voxel body so Core could stay PicoGK-free. The IVoxelHandle
/// follow-up (2026-04-25) replaced the cast wart with a proper opaque
/// marker — App-side consumers unwrap via <c>handle.AsPicoGK()</c>.
/// </para>
/// </summary>
public sealed record ChamberGeometryResult(
    IVoxelHandle Voxels,            // wrap with new PicoGKVoxelHandle(voxels) at construction
    double SolidVolume_mm3,
    double InnerSurfaceArea_mm2,
    double OuterJacketThickness_mm,
    double TotalMass_g,
    double PrintedCost_USD,
    double BoundingLength_mm,
    double BoundingDiameter_mm,
    string Description,
    string InjectorSTLMessage = "",
    // Per-stage wall-clock profile of this Build() call. Null on the
    // BuildAnalytical (physics-only) path.
    BuildProfile? Profile = null);
