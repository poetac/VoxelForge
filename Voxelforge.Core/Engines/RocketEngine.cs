// RocketEngine.cs — IEngine implementation for the rocket-regen pillar.
//
// Sprint A Phase 1 (2026-05-04). Thin wrapper over the existing
// `RegenChamberOptimization.GenerateWith` + `FeasibilityGate.Evaluate`
// pipeline so generic optimizers / consumers can dispatch through the
// IEngine contract instead of reaching into the rocket-specific static
// orchestrator.
//
// Phase 1 keeps voxel building OFF the IEngine surface: the headless
// optimizer / scoring path does not need voxels, and routing
// `IVoxelGenerator` through this seam would re-couple Core to the
// rocket-specific `ChamberVoxelBuilderAdapter`. Voxel-driven flows
// (StlExporter, kiosk) continue to call `RegenChamberOptimization.GenerateWith`
// directly with the appropriate adapter.

using System.Collections.Generic;
using Voxelforge.Optimization;

namespace Voxelforge.Engines;

/// <summary>
/// Rocket-regen engine implementation of the
/// <see cref="IEngine{TDesign,TConditions,TResult}"/> contract.
/// </summary>
/// <remarks>
/// Stateless. Every <see cref="Evaluate"/> call runs the full
/// <see cref="RegenChamberOptimization.GenerateWith"/> pipeline followed
/// by <see cref="FeasibilityGate.Evaluate"/>. The headless evaluation
/// (no voxel build) is identical to what
/// <c>Voxelforge.Eval</c> + <c>RamjetObjective</c>-style call sites
/// already use, so wrapping it costs no measurable overhead.
/// </remarks>
public sealed class RocketEngine
    : IEngine<RegenChamberDesign, OperatingConditions, RocketEngineResult>
{
    /// <summary>Singleton — the engine is stateless.</summary>
    public static readonly RocketEngine Instance = new();

    private RocketEngine() { }

    /// <inheritdoc />
    public string Family => EngineFamilies.Rocket;

    /// <inheritdoc />
    public RocketEngineResult Evaluate(RegenChamberDesign design, OperatingConditions conditions)
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

        var generation = RegenChamberOptimization.GenerateWith(
            conditions, design, voxelSize_mm: 0.0, skipVoxelGeometry: true);
        var gateResult = FeasibilityGate.Evaluate(generation);

        // Sprint A Phase 2: split gate violations into hard vs. advisory
        // by looking up each violation's ConstraintId in the GateRegistry.
        // Unknown IDs (e.g. synthetic test violations) default to hard so
        // they are never silently dropped from the violations list.
        var hard     = new List<FeasibilityViolation>(gateResult.Violations.Length);
        var advisory = new List<FeasibilityViolation>();
        foreach (var v in gateResult.Violations)
        {
            if (GateRegistry.TryGetById(v.ConstraintId, out var desc)
                && desc!.Severity == GateSeverity.Advisory)
                advisory.Add(v);
            else
                hard.Add(v);
        }

        return new RocketEngineResult(
            Generation: generation,
            Violations: hard,
            Advisories: advisory);
    }
}
