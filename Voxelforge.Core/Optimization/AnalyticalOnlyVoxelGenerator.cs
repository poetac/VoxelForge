// AnalyticalOnlyVoxelGenerator.cs — Core-side default for the
// `IVoxelGenerator` seam: routes `BuildAnalytical` through
// `Voxelforge.Geometry.ChamberAnalyticalBuilder` (pure C#) and throws
// on `Build` because Core cannot reach `ChamberVoxelBuilder.Build`
// (which lives in Voxels and uses PicoGK directly).
//
// Sprint A-3 / ADR-021 (2026-04-30): used as the default
// `IVoxelGenerator` for headless paths (`voxelforge-eval`, bench-SA,
// unit tests passing `skipVoxelGeometry: true`). App callers wanting
// the full voxel build pass a `ChamberVoxelBuilderAdapter` instead.

namespace Voxelforge.Optimization;

/// <summary>
/// Default <see cref="IVoxelGenerator"/> for headless callers. Both
/// <see cref="Build"/> and <see cref="BuildAnalytical"/> route through
/// <see cref="Geometry.ChamberAnalyticalBuilder.BuildAnalytical"/> —
/// the result has <c>Voxels = null!</c> for the
/// <see cref="Geometry.ChamberGeometryResult"/>'s mesh field, which
/// callers must not dereference. App callers wanting a real voxel body
/// pass <c>Voxelforge.Geometry.ChamberVoxelBuilderAdapter</c> from the
/// Voxels project; headless / unit-test callers passing this default
/// match the pre-A-3 behaviour where unit-test fixtures called
/// <c>RegenChamberOptimization.GenerateWith(cond, design)</c> at
/// <c>voxelSize_mm = 0</c> and ignored the (mesh-less) result.
/// </summary>
public sealed class AnalyticalOnlyVoxelGenerator : IVoxelGenerator
{
    /// <summary>Singleton instance — the generator is stateless.</summary>
    public static readonly AnalyticalOnlyVoxelGenerator Instance = new();

    private AnalyticalOnlyVoxelGenerator() { }

    /// <inheritdoc />
    public Geometry.ChamberGeometryResult Build(Geometry.ChamberBuildOptions opts, double voxelSize_mm)
        => Geometry.ChamberAnalyticalBuilder.BuildAnalytical(opts);

    /// <inheritdoc />
    public Geometry.ChamberGeometryResult BuildAnalytical(Geometry.ChamberBuildOptions opts)
        => Geometry.ChamberAnalyticalBuilder.BuildAnalytical(opts);
}
