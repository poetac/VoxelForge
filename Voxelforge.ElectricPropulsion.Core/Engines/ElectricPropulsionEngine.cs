// ElectricPropulsionEngine.cs — IEngine implementation for the
// electric-propulsion pillar.
//
// Wave-1 + Wave-2 shipped; Wave-3 partial. Dispatch on
// ElectricPropulsionEngineDesign.Kind covers six implemented variants:
// Resistojet (Sprint E.1/E.2), HallEffect (EP.W2.HET, Busch discharge),
// Arcjet (EP.W2.AJ, Maecker-Kovitya thermal-arc), PulsedPlasmaThruster
// (EP.W2.PPT, Solbes-Vondra ablation), GriddedIon (EP.W2.GIT, Child-
// Langmuir beam extraction), and MagnetoPlasmaDynamic (EP.W2.MPD,
// self-field Maecker Lorentz acceleration with optional applied-field
// per ADR-038). Wave-3 scaffolds (Vasimr / Feep / Hdlt) reserve enum
// slots and persistence round-trip; the physics dispatch throws
// NotImplementedException on those three kinds until their phase-2
// solvers ship (Sprints EP.W4/W5/W6).
//
// Mirrors RocketEngine.cs and AirbreathingEngine.cs structurally —
// stateless singleton, per-call validation, dispatch into the static
// orchestrator (ElectricPropulsionOptimization.GenerateWith).

namespace Voxelforge.ElectricPropulsion.Engines;

using Voxelforge.Engines;

/// <summary>
/// Electric-propulsion engine implementation of the
/// <see cref="IEngine{TDesign,TConditions,TResult}"/> contract.
/// Wave-1 + Wave-2 implemented: dispatches the full physics + feasibility
/// pipeline for <see cref="ElectricPropulsionEngineKind.Resistojet"/>,
/// <see cref="ElectricPropulsionEngineKind.Arcjet"/>,
/// <see cref="ElectricPropulsionEngineKind.HallEffect"/>,
/// <see cref="ElectricPropulsionEngineKind.GriddedIon"/>,
/// <see cref="ElectricPropulsionEngineKind.PulsedPlasmaThruster"/>, and
/// <see cref="ElectricPropulsionEngineKind.MagnetoPlasmaDynamic"/>
/// (self-field, with optional applied-field per ADR-038). Wave-3
/// scaffolded kinds — <see cref="ElectricPropulsionEngineKind.Vasimr"/>,
/// <see cref="ElectricPropulsionEngineKind.Feep"/>, and
/// <see cref="ElectricPropulsionEngineKind.Hdlt"/> — currently throw
/// <see cref="System.NotImplementedException"/> at dispatch pending
/// phase-2 physics (Sprints EP.W4 / EP.W5 / EP.W6).
/// </summary>
/// <remarks>
/// Stateless. Every <see cref="Evaluate"/> call runs the full physics
/// stack followed by feasibility evaluation in
/// <see cref="ElectricPropulsionOptimization.GenerateWith"/>.
/// </remarks>
public sealed class ElectricPropulsionEngine
    : IEngine<ElectricPropulsionEngineDesign, ResistojetConditions, ElectricPropulsionResult>
{
    /// <summary>Singleton — the engine is stateless.</summary>
    public static readonly ElectricPropulsionEngine Instance = new();

    private ElectricPropulsionEngine() { }

    /// <inheritdoc />
    public string Family => EngineFamilies.ElectricPropulsion;

    /// <inheritdoc />
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/> is null.
    /// </exception>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/>
    /// belongs to a different engine family than this implementation.
    /// </exception>
    public ElectricPropulsionResult Evaluate(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions)
    {
        System.ArgumentNullException.ThrowIfNull(design);
        System.ArgumentNullException.ThrowIfNull(conditions);
        if (design.Family != Family)
            throw new System.ArgumentException(
                $"Design family '{design.Family}' does not match engine family '{Family}'.",
                nameof(design));
        if (conditions.Family != Family)
            throw new System.ArgumentException(
                $"Conditions family '{conditions.Family}' does not match engine family '{Family}'.",
                nameof(conditions));

        return ElectricPropulsionOptimization.GenerateWith(design, conditions);
    }
}
