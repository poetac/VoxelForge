// PatchGeometryResult.cs — Sprint ANT.W6 result record for the
// microstrip patch antenna voxel builder. All dimensional fields in mm.

namespace Voxelforge.Antenna;

/// <summary>
/// Geometry summary for a rectangular microstrip patch antenna built by
/// <see cref="AntennaVoxelBuilder.BuildPatch"/>. The assembly is a
/// three-layer sandwich: ground plane (bottom) → dielectric substrate
/// → patch conductor (top). Assembly centred at the origin; z-axis is
/// the stack normal (ground at negative z, patch at positive z).
/// </summary>
/// <param name="PatchWidth_mm">W [mm] — patch conductor width
/// (along x). Either auto-computed via the Bahl-Trivedi formula or
/// taken from <see cref="AntennaLinkDesign.PatchWidth_mm"/>.</param>
/// <param name="PatchLength_mm">L [mm] — patch conductor length
/// (along y, the resonant dimension). Either auto-computed or from
/// <see cref="AntennaLinkDesign.PatchLength_mm"/>.</param>
/// <param name="SubstrateThickness_mm">h [mm] — dielectric substrate
/// thickness.</param>
/// <param name="ResonantFrequency_Hz">f_r [Hz] — resonant frequency
/// computed from the Bahl-Trivedi effective permittivity + fringing
/// correction (Bahl I.J., Trivedi D.K., 1977). Equals the design
/// frequency when patch dimensions were auto-computed.</param>
/// <param name="Material">Print material; determines ε_r used in the
/// resonant-frequency formula and the substrate-thinness gate.</param>
/// <param name="SubstrateTooThin">True when the substrate thickness is
/// below the print-material minimum feature size
/// (<see cref="AntennaConstraintIds.SubstrateTooThin"/>).</param>
/// <param name="GeometryRfMismatch">True when the resonant frequency
/// deviates from the design frequency by more than 5 %
/// (<see cref="AntennaConstraintIds.GeometryRfMismatch"/>). Only fires
/// when at least one patch dimension was explicitly supplied.</param>
/// <param name="Voxels">PicoGK voxel handle for the three-layer patch
/// assembly (ground + substrate + patch conductor).</param>
internal sealed record PatchGeometryResult(
    double        PatchWidth_mm,
    double        PatchLength_mm,
    double        SubstrateThickness_mm,
    double        ResonantFrequency_Hz,
    PrintMaterial Material,
    bool          SubstrateTooThin,
    bool          GeometryRfMismatch,
    IVoxelHandle  Voxels) : IAntennaGeometryResult;
