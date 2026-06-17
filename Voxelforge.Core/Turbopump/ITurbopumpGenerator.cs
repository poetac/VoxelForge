// ITurbopumpGenerator.cs — Abstraction over the
// TurbopumpGeometryGenerator.Generate static method, used by the
// Core-bound RegenChamberOptimization orchestrator (Sprint A-3 Phase 2)
// so it can attach pump geometry to a TurbopumpResult without
// referencing the Voxels project (where the generator's own
// `using PicoGK;` import lives, even though Generate itself is
// pure-math).

using Voxelforge.FeedSystem;

namespace Voxelforge.Turbopump;

/// <summary>
/// Sprint A-3 Phase 2 / ADR-021 (2026-04-30): seam over
/// <c>Voxelforge.Turbopump.TurbopumpGeometryGenerator.Generate</c> so
/// the Core-resident orchestrator can produce
/// <see cref="TurbopumpGeometry"/> attachments without dragging the
/// Voxels project + PicoGK into Core.
/// <para>
/// App callers building voxel-rendered chambers pass
/// <c>Voxelforge.Turbopump.TurbopumpGeneratorAdapter</c> from the
/// Voxels project; headless / bench-SA / unit-test callers pass
/// <c>null</c> (the orchestrator skips pump-geometry attachment when
/// the generator is null, same effect as
/// <c>cond.IncludeTurbopumpGeometry = false</c>).
/// </para>
/// </summary>
public interface ITurbopumpGenerator
{
    /// <summary>
    /// Generate turbopump geometry parameters from a sized
    /// <see cref="PumpSizing"/> record. Returns null when the pump is
    /// degenerate (zero RPM or zero head) — same contract as the
    /// underlying static method.
    /// </summary>
    TurbopumpGeometry? Generate(PumpSizing pump);
}

/// <summary>
/// Sprint A-3 Phase 2: seam over
/// <c>Voxelforge.Turbopump.TurbineGeometryGenerator.Generate</c> for
/// turbine-wheel attachments. Same shape as
/// <see cref="ITurbopumpGenerator"/>; lives in the same file because
/// they're always passed together.
/// </summary>
public interface ITurbineGenerator
{
    /// <summary>
    /// Generate turbine-wheel geometry from a sized turbine stage.
    /// Returns null when the stage is degenerate.
    /// </summary>
    TurbineGeometry? Generate(TurbineStage stage);
}
