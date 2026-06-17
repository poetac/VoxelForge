// NuclearEngine.cs — IEngine implementation for the nuclear thermal pillar.
//
// Mirrors MarineEngine on the marine side. Stateless singleton that wraps
// NuclearOptimization.GenerateWith behind the generic IEngine contract so
// callers don't need to know the pillar-specific static orchestrator.

using System;
using Voxelforge.Engines;

namespace Voxelforge.Nuclear.Engines;

/// <summary>
/// Nuclear thermal pillar implementation of
/// <see cref="IEngine{TDesign,TConditions,TResult}"/>.
/// Stateless singleton; dispatches to
/// <see cref="NuclearOptimization.GenerateWith"/>.
/// </summary>
public sealed class NuclearEngine
    : IEngine<NuclearThermalDesign, NuclearThermalConditions, NtrGenerationResult>
{
    /// <summary>Singleton — the engine is stateless.</summary>
    public static readonly NuclearEngine Instance = new();

    private NuclearEngine() { }

    /// <inheritdoc />
    public string Family => EngineFamilies.Nuclear;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/>
    /// is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the family of <paramref name="design"/> or
    /// <paramref name="conditions"/> does not match
    /// <see cref="EngineFamilies.Nuclear"/>.
    /// </exception>
    public NtrGenerationResult Evaluate(
        NuclearThermalDesign design,
        NuclearThermalConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        if (design.Family != Family)
            throw new ArgumentException(
                $"Design family '{design.Family}' does not match engine family '{Family}'.",
                nameof(design));
        if (conditions.Family != Family)
            throw new ArgumentException(
                $"Conditions family '{conditions.Family}' does not match engine family '{Family}'.",
                nameof(conditions));

        return NuclearOptimization.GenerateWith(design, conditions);
    }
}
