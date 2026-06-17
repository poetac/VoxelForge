// RefrigerationGeometryResult.cs — Sprint A.81 (C.2) result record
// returned by the heat-pump / refrigeration voxel builder. Mirrors the
// pattern of FlywheelGeometryResult / TankageGeometryResult: a small
// immutable summary of the built geometry plus the voxel handle for
// downstream STL / 3MF export.

namespace Voxelforge.Refrigeration;

/// <summary>
/// Geometry summary for a heat-pump / refrigeration assembly built by
/// <see cref="RefrigerationVoxelBuilder"/>. All dimensional fields are
/// in millimetres to match the PicoGK voxel-grid convention; the
/// <see cref="Voxels"/> handle wraps the underlying PicoGK voxel body so
/// callers can mesh / export without dragging PicoGK types into
/// <c>Voxelforge.Core</c>.
/// <para>
/// The assembly is composed of three coaxial sub-envelopes along the +X
/// axis (the +X axis-of-symmetry convention shared with Flywheel /
/// Tankage):
/// </para>
/// <list type="bullet">
///   <item><description><b>Compressor</b>: solid cylinder centred on the
///   origin, axial extent
///   <c>x ∈ [-CompressorLength_mm / 2, +CompressorLength_mm / 2]</c>,
///   outer radius <see cref="CompressorOuterRadius_mm"/>.</description></item>
///   <item><description><b>Condenser coil envelope</b>: thick annular
///   shell on the <c>+X</c> side of the compressor (the "hot" side
///   delivers heat to the hot reservoir). Annulus inner / outer radii
///   <see cref="CondenserInnerRadius_mm"/> / <see cref="CondenserOuterRadius_mm"/>,
///   axial extent <see cref="CondenserLength_mm"/>. The shell
///   represents the volume swept by a helical or serpentine tube bundle
///   (true helical geometry deferred to a future refinement — see file
///   header on <see cref="RefrigerationVoxelBuilder"/>).</description></item>
///   <item><description><b>Evaporator coil envelope</b>: thick annular
///   shell on the <c>-X</c> side of the compressor (the "cold" side
///   extracts heat from the cold reservoir). Same envelope topology as
///   the condenser, with its own radial / axial dimensions.</description></item>
/// </list>
/// </summary>
/// <param name="CompressorOuterRadius_mm">R_compressor [mm] — compressor envelope radius.</param>
/// <param name="CompressorLength_mm">L_compressor [mm] — compressor axial extent.</param>
/// <param name="CondenserInnerRadius_mm">R_condenser_inner [mm] — condenser coil envelope inner radius.</param>
/// <param name="CondenserOuterRadius_mm">R_condenser_outer [mm] — condenser coil envelope outer radius.</param>
/// <param name="CondenserLength_mm">L_condenser [mm] — condenser coil envelope axial extent.</param>
/// <param name="EvaporatorInnerRadius_mm">R_evaporator_inner [mm] — evaporator coil envelope inner radius.</param>
/// <param name="EvaporatorOuterRadius_mm">R_evaporator_outer [mm] — evaporator coil envelope outer radius.</param>
/// <param name="EvaporatorLength_mm">L_evaporator [mm] — evaporator coil envelope axial extent.</param>
/// <param name="OverallLength_mm">Total axial extent of the assembly [mm] = L_compressor + L_condenser + L_evaporator (the three envelopes butt end-to-end with no axial gap, mirroring a tightly-packaged residential heat-pump outdoor unit).</param>
/// <param name="Voxels">PicoGK voxel handle wrapping the built heat-pump assembly body.</param>
internal sealed record RefrigerationGeometryResult(
    double CompressorOuterRadius_mm,
    double CompressorLength_mm,
    double CondenserInnerRadius_mm,
    double CondenserOuterRadius_mm,
    double CondenserLength_mm,
    double EvaporatorInnerRadius_mm,
    double EvaporatorOuterRadius_mm,
    double EvaporatorLength_mm,
    double OverallLength_mm,
    IVoxelHandle Voxels);
