// MarineEngine.cs — IEngine implementation for the marine pillar.
//
// Mirrors AirbreathingEngine on the air-breathing side. Stateless singleton
// that wraps MarineOptimization.GenerateWith behind the generic IEngine
// contract so callers don't need to know the pillar-specific static orchestrator.

using System;
using Voxelforge.Engines;

namespace Voxelforge.Marine.Engines;

/// <summary>
/// Marine pillar implementation of
/// <see cref="IEngine{TDesign,TConditions,TResult}"/>.
/// Stateless singleton; dispatches to
/// <see cref="MarineOptimization.GenerateWith"/>.
/// </summary>
public sealed class MarineEngine
    : IEngine<MarineDesign, MarineConditions, MarineResult>
{
    /// <summary>Singleton — the engine is stateless.</summary>
    public static readonly MarineEngine Instance = new();

    private MarineEngine() { }

    /// <inheritdoc />
    public string Family => EngineFamilies.Marine;

    /// <inheritdoc />
    public MarineResult Evaluate(MarineDesign design, MarineConditions conditions)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (conditions is null) throw new ArgumentNullException(nameof(conditions));
        if (design.Family != Family)
            throw new ArgumentException(
                $"Design family '{design.Family}' does not match engine family '{Family}'.",
                nameof(design));
        if (conditions.Family != Family)
            throw new ArgumentException(
                $"Conditions family '{conditions.Family}' does not match engine family '{Family}'.",
                nameof(conditions));

        return MarineOptimization.GenerateWith(design, conditions);
    }
}
