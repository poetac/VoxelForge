// YagiUdaGeometryResult.cs — Sprint ANT.W5-voxel result record for the
// Yagi-Uda antenna voxel builder. All dimensional fields are in mm.

namespace Voxelforge.Antenna;

/// <summary>
/// Geometry summary for a Yagi-Uda end-fire array antenna built by
/// <see cref="AntennaVoxelBuilder.BuildYagiUda"/>. Boresight is +Z
/// (end-fire direction); the driven dipole is centred at z = 0;
/// the reflector is at z &lt; 0; directors are at z &gt; 0.
/// </summary>
/// <param name="DrivenElementLength_mm">L_d [mm] — total length of the
/// driven half-wave dipole (≈ 0.5 λ).</param>
/// <param name="ReflectorLength_mm">L_ref [mm] — reflector element
/// length (≈ 0.525 λ, slightly longer than the driven dipole).</param>
/// <param name="DirectorLength_mm">L_dir [mm] — director element length
/// (≈ 0.45 λ, shorter than driven). All directors share the same length
/// in this baseline implementation.</param>
/// <param name="DirectorCount">N_dir — number of director elements.</param>
/// <param name="ElementDiameter_mm">d_elem [mm] — cross-section diameter
/// of all rod elements and the boom (same diameter for simplicity).</param>
/// <param name="BoomLength_mm">L_boom [mm] — total boom length from
/// reflector z-position to last director z-position.</param>
/// <param name="BoomDiameter_mm">d_boom [mm] — diameter of the central
/// boom cylinder. Typically equal to ElementDiameter_mm.</param>
/// <param name="ElementOverhangViolated">True when element overhang
/// angle exceeds the print-material maximum
/// (<see cref="AntennaConstraintIds.ElementOverhangUnsupported"/>).
/// Elements are oriented perpendicular to the boom — for FDM / LPBF
/// this may require supports if pitch angle &gt; MaxOverhangAngle_deg.
/// </param>
/// <param name="Voxels">PicoGK voxel handle for the complete Yagi
/// boom + element array.</param>
internal sealed record YagiUdaGeometryResult(
    double DrivenElementLength_mm,
    double ReflectorLength_mm,
    double DirectorLength_mm,
    int    DirectorCount,
    double ElementDiameter_mm,
    double BoomLength_mm,
    double BoomDiameter_mm,
    bool   ElementOverhangViolated,
    IVoxelHandle Voxels) : IAntennaGeometryResult;
