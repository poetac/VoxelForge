namespace Voxelforge;

/// <summary>
/// Opaque marker for a voxel-grid handle. Core records (e.g.
/// <c>AerospikeBuildResult</c>, <c>ChamberGeometryResult</c>) carry voxel
/// bodies as <see cref="IVoxelHandle"/> so the headless Core library doesn't
/// need to reference PicoGK directly.
/// <para>
/// The concrete implementation (<c>Voxelforge.PicoGKVoxelHandle</c>
/// in the Voxels project) wraps a <c>PicoGK.Voxels</c>. App-side consumers
/// unwrap via the <c>AsPicoGK()</c> extension method that lives alongside
/// the wrapper.
/// </para>
/// <para>
/// A2 (2026-04-25) introduced this interface to retire the <c>object?</c>
/// cast wart from the original A1 Core extraction (see ADR-015).
/// </para>
/// </summary>
public interface IVoxelHandle
{
}
