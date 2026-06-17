using Voxelforge.Chamber;

namespace Voxelforge.Geometry;

/// <summary>
/// Output of AerospikeBuilder.Build — the voxel body plus the parametric
/// contour + a summary of the resulting engine so the caller (CLI / UI /
/// report writer) can surface thrust-class outputs without re-deriving them.
/// <para>
/// A1 (Core extraction) introduced this record carrying an <c>object?</c>
/// for the voxel body so Core could stay PicoGK-free. The IVoxelHandle
/// follow-up (2026-04-25) replaced the cast wart with a proper opaque
/// marker — App-side consumers unwrap via <c>handle.AsPicoGK()</c>.
/// </para>
/// <para>
/// Sprint 2a (2026-04-22): <see cref="Voxels"/> is nullable to support
/// the xUnit-safe physics-only entry point that skips PicoGK voxelization.
/// Callers that need the voxel body (STL export, viewer) must null-check;
/// callers that only consume contour + thermal + summary scalars (SA
/// scoring, feasibility-gate evaluation, report writing) are untouched.
/// </para>
/// </summary>
public sealed record AerospikeBuildResult(
    IVoxelHandle?         Voxels,                 // null on the geometry-only path
    AerospikeContour      Contour,
    double                ThroatOuterRadius_mm,
    double                ThroatInnerRadius_mm,
    double                PlugTruncatedLength_mm,
    double                ChamberRadius_mm,
    double                ChamberLength_mm,
    double                TotalLength_mm,         // injector face → plug base
    double                TotalDiameter_mm,
    double                SolidVolume_mm3,
    double                EstimatedMass_g,
    string                Description,
    // Null on the geometry-only path.
    AerospikeThermalResult? Thermal = null,
    // Sized injector pattern + face placement. Null when
    // AerospikeSpec.InjectorPattern is null.
    AerospikeInjectorSizing? InjectorSizing = null,
    // Injector-face equilibrium T estimate (analogue of
    // HeatTransfer.InjectorFaceThermal for the aerospike pre-throat
    // chamber). Populated when InjectorSizing is present; null
    // otherwise.
    HeatTransfer.AerospikeInjectorFaceResult? InjectorFace = null);
