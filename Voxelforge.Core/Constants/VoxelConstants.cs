// VoxelConstants.cs — tech-debt T7 close-out (Phase 5, 2026-04-28).
//
// Tribal-knowledge voxel-size defaults previously inlined as `0.4` /
// `0.4f` literals across builder method signatures and `Program.cs`'s
// PicoGK Library init. Hoisted into named, cited constants so a future
// "what voxel size is the default?" question grep-resolves to a single
// answer-with-rationale instead of five string-matched literals.
//
// See `KarassikConstants` / `TurbineConstants` for the analogous pump and
// turbine extractions that landed via `TurbopumpGeometryGenerator`
// and `TurbineGeometryGenerator` partial-classes (constants live on
// the generator classes themselves rather than in this directory; same
// pattern, different home).

namespace Voxelforge.Constants;

/// <summary>
/// Voxel-size defaults shared across the geometry-builder pipeline.
/// All values are millimetres unless suffixed otherwise.
/// </summary>
public static class VoxelConstants
{
    /// <summary>
    /// Default voxel resolution for the geometry builders'
    /// <c>voxelSize_mm</c> parameter. Picked at <c>0.4 mm</c> as the
    /// balance point between print-feature fidelity (≥ 0.3 mm LPBF
    /// universal floor per ADR-007) and chamber-build memory budget
    /// on the 64 GB workstation constraint (CLAUDE.md). Higher
    /// fidelity (0.2 mm) is reserved for STL-export-only paths where
    /// the wall-clock cost is amortised over a single export.
    /// <para>
    /// Matches the <c>VoxelSizeMM = 0.4f</c> session default in the
    /// App's <c>Program.cs</c>; if you change this, update both. The
    /// two are intentionally separate types — this one is
    /// <see cref="double"/> for builder API ergonomics, the App's is
    /// <see cref="float"/> because PicoGK's <c>Library.Go</c> takes
    /// <see cref="float"/>.
    /// </para>
    /// </summary>
    public const double DefaultBuilderVoxelSize_mm = 0.4;
}
