// HeatPipeGeometryResult.cs — Sprint A.80 (C.2) result record returned by
// the heat-pipe voxel builder. Mirrors the pattern of
// FlywheelGeometryResult / TankageGeometryResult: a small immutable summary
// of the built geometry plus the voxel handle for downstream STL / 3MF
// export.

namespace Voxelforge.HeatPipe;

/// <summary>
/// Geometry summary for a heat-pipe device built by
/// <see cref="HeatPipeVoxelBuilder"/>. All dimensional fields are in
/// millimetres to match the PicoGK voxel-grid convention; the
/// <see cref="Voxels"/> handle wraps the underlying PicoGK voxel body so
/// callers can mesh / export without dragging PicoGK types into
/// <c>Voxelforge.Core</c>.
/// </summary>
/// <param name="EnvelopeOuterDiameter_mm">D_envelope_o [mm] — heat-pipe outer
/// diameter (envelope OD = wick-OD + 2·envelopeWallThickness).</param>
/// <param name="EnvelopeInnerDiameter_mm">D_envelope_i [mm] — envelope inner
/// diameter (= wick annulus outer diameter). Equals VapourCoreDiameter_mm +
/// 2·WickThickness_mm.</param>
/// <param name="EnvelopeWallThickness_mm">t_wall [mm] — envelope shell wall
/// thickness (cluster-anchor fraction of vapour-core diameter).</param>
/// <param name="WickOuterDiameter_mm">D_wick_o [mm] — wick annulus outer
/// diameter (= envelope inner diameter).</param>
/// <param name="WickInnerDiameter_mm">D_wick_i [mm] — wick annulus inner
/// diameter (= vapour-core diameter). VapourCore sits inside the wick.</param>
/// <param name="WickThickness_mm">t_wick [mm] — radial wick thickness
/// (cluster-anchor fraction of vapour-core diameter).</param>
/// <param name="VapourCoreDiameter_mm">D_vap [mm] — vapour-core (open
/// central cavity) diameter (matches design.InternalDiameter_m × 1000).</param>
/// <param name="Length_mm">L [mm] — heat-pipe overall length (matches
/// design.Length_m × 1000).</param>
/// <param name="Voxels">PicoGK voxel handle wrapping the built heat-pipe
/// body (envelope shell + wick annulus, both rendered as hollow open-ended
/// shells per the A.70 closed-cavity workaround documented in
/// <see cref="HeatPipeVoxelBuilder"/>).</param>
internal sealed record HeatPipeGeometryResult(
    double EnvelopeOuterDiameter_mm,
    double EnvelopeInnerDiameter_mm,
    double EnvelopeWallThickness_mm,
    double WickOuterDiameter_mm,
    double WickInnerDiameter_mm,
    double WickThickness_mm,
    double VapourCoreDiameter_mm,
    double Length_mm,
    IVoxelHandle Voxels);
