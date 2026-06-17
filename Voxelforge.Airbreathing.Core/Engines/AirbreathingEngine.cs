// AirbreathingEngine.cs — IEngine implementation for the air-breathing pillar.
//
// Sprint A Phase 1 (2026-05-04). Mirrors RocketEngine on the rocket side.
// Wraps the existing AirbreathingOptimization.GenerateWith pipeline so
// generic optimizers / consumers can dispatch through the IEngine contract
// without reaching into the airbreathing-specific static orchestrator.

namespace Voxelforge.Airbreathing.Engines;

using Voxelforge.Engines;

/// <summary>
/// Air-breathing engine implementation of the
/// <see cref="IEngine{TDesign,TConditions,TResult}"/> contract.
/// Dispatches across all five sub-step kinds (ramjet / turbojet / turbofan
/// / scramjet / RBCC) by handing off to <see cref="AirbreathingOptimization.GenerateWith"/>,
/// which selects the cycle solver via
/// <see cref="Voxelforge.Airbreathing.Cycles.AirbreathingCycleSolvers"/>.
/// </summary>
/// <remarks>
/// Stateless. Every <see cref="Evaluate"/> call runs the cycle solver +
/// feasibility evaluator end-to-end; mirrors the headless flow already
/// used by <see cref="Voxelforge.Airbreathing.Optimization.RamjetObjective"/>
/// + siblings.
/// </remarks>
public sealed class AirbreathingEngine
    : IEngine<AirbreathingEngineDesign, FlightConditions, AirbreathingResult>
{
    /// <summary>Singleton — the engine is stateless.</summary>
    public static readonly AirbreathingEngine Instance = new();

    private AirbreathingEngine() { }

    /// <inheritdoc />
    public string Family => EngineFamilies.Airbreathing;

    /// <inheritdoc />
    public AirbreathingResult Evaluate(AirbreathingEngineDesign design, FlightConditions conditions)
    {
        if (design is null) throw new System.ArgumentNullException(nameof(design));
        if (conditions is null) throw new System.ArgumentNullException(nameof(conditions));
        if (design.Family != Family)
            throw new System.ArgumentException(
                $"Design family '{design.Family}' does not match engine family '{Family}'.",
                nameof(design));
        if (conditions.Family != Family)
            throw new System.ArgumentException(
                $"Conditions family '{conditions.Family}' does not match engine family '{Family}'.",
                nameof(conditions));

        return AirbreathingOptimization.GenerateWith(design, conditions);
    }
}
