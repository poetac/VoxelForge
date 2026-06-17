// IElectricPropulsionVoxelGenerator.cs — abstraction over electric-propulsion
// voxel-build methods. Mirrors IAirbreathingVoxelGenerator on the
// airbreathing side and IVoxelGenerator on the rocket side
// (ADR-021 / Phase 0): Core orchestrators reference this interface;
// Voxelforge.ElectricPropulsion.Voxels provides the PicoGK-bound
// concrete implementation.

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Abstraction over resistojet (and future electric-propulsion) voxel-build
/// methods. Keeps the headless <c>Voxelforge.ElectricPropulsion.Core</c>
/// library free of PicoGK references — the concrete builder lives in
/// <c>Voxelforge.ElectricPropulsion.Voxels</c>.
/// </summary>
/// <remarks>
/// The Build signature is intentionally minimal — Wave-1's
/// <c>ResistojetVoxelBuilder</c> takes the design record directly and
/// computes its own internal contour. Future variants may need
/// kind-discriminated overloads or a contour parameter; this interface
/// will gain methods as the family expands.
/// </remarks>
public interface IElectricPropulsionVoxelGenerator
{
    /// <summary>
    /// Build the resistojet shell voxel. Must run inside a
    /// <c>PicoGK.Library</c> scope on the task thread (CLAUDE.md PicoGK
    /// pitfall #4).
    /// </summary>
    /// <remarks>
    /// Wave-1 returns an opaque <see cref="object"/> placeholder (the
    /// concrete <c>ResistojetGeometryResult</c> lives in the Voxels
    /// project and Core cannot reference it without a circular dep). The
    /// caller in <c>Voxelforge.ElectricPropulsion.StlExporter</c> casts
    /// to the concrete type. A future ADR may extract a shared
    /// <c>IVoxelBuildResult</c> interface to the Core layer.
    /// </remarks>
    object Build(ElectricPropulsionEngineDesign design);
}
