// TurbopumpGeneratorAdapter.cs — Voxels-side bridge implementing
// `Voxelforge.Turbopump.ITurbopumpGenerator` over the static
// `TurbopumpGeometryGenerator.Generate` method.
//
// Sprint A-3 Phase 2 / ADR-021 (2026-04-30): App callers attach an
// instance of this adapter to the orchestrator's
// `ITurbopumpGenerator?` parameter to keep the static-method dispatch
// at App granularity (Voxels-side static method is reached via the
// adapter; Core-resident orchestrator never references PicoGK
// transitively).

using Voxelforge.FeedSystem;

namespace Voxelforge.Turbopump;

/// <summary>
/// Default <see cref="ITurbopumpGenerator"/> implementation. One-line
/// wrapper around <see cref="TurbopumpGeometryGenerator.Generate"/>.
/// </summary>
public sealed class TurbopumpGeneratorAdapter : ITurbopumpGenerator
{
    /// <inheritdoc />
    public TurbopumpGeometry? Generate(PumpSizing pump)
        => TurbopumpGeometryGenerator.Generate(pump);
}

/// <summary>
/// Default <see cref="ITurbineGenerator"/> implementation. One-line
/// wrapper around <see cref="TurbineGeometryGenerator.Generate"/>.
/// </summary>
public sealed class TurbineGeneratorAdapter : ITurbineGenerator
{
    /// <inheritdoc />
    public TurbineGeometry? Generate(TurbineStage stage)
        => TurbineGeometryGenerator.Generate(stage);
}
