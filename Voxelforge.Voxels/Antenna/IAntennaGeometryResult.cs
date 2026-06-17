// IAntennaGeometryResult.cs — Sprint ANT.W5-voxel common interface
// for all antenna topology geometry results. Lets AntennaVoxelBuilder
// .BuildAny() dispatch across all five supported kinds (ParabolicDish,
// Helical, Horn, YagiUda, Patch) and return a common handle.
//
// Callers that need topology-specific dimensional fields cast to the
// concrete result type (AntennaGeometryResult, HelicalGeometryResult,
// HornGeometryResult, YagiUdaGeometryResult, PatchGeometryResult).

namespace Voxelforge.Antenna;

/// <summary>
/// Sprint ANT.W5-voxel — common interface for antenna topology geometry
/// results returned by <see cref="AntennaVoxelBuilder.BuildAny"/>.
/// </summary>
internal interface IAntennaGeometryResult
{
    /// <summary>PicoGK voxel body for the built antenna.</summary>
    IVoxelHandle Voxels { get; }
}
