// RocketEngineResult.cs — IEngineResult adapter for the rocket pillar.
//
// Sprint A Phase 1 (2026-05-04). The rocket-side `RegenGenerationResult`
// carries the raw physics output (thermal stations, structural stress,
// manufacturing report, ...) but does NOT include a feasibility-violation
// list directly — those are computed by `FeasibilityGate.Evaluate` as a
// separate pass. To match the `IEngineResult` contract we wrap both.

using System.Collections.Generic;
using Voxelforge.Optimization;

namespace Voxelforge.Engines;

/// <summary>
/// Bundles a <see cref="RegenGenerationResult"/> together with its
/// feasibility-gate verdict. Implements <see cref="IEngineResult"/> so
/// generic optimizers / consumers see a uniform shape across families.
/// </summary>
/// <param name="Generation">
/// The raw physics result from <see cref="RegenChamberOptimization.GenerateWith"/>.
/// Family-specific consumers (UI panels, STL export, build sheet) cast back
/// to this type to access thermal stations, structural margins, etc.
/// </param>
/// <param name="Violations">Hard-constraint violations from the feasibility gate.</param>
/// <param name="Advisories">
/// Reserved for future use. The rocket-side <see cref="FeasibilityGate"/>
/// emits hard + advisory violations into the same list today; the
/// <see cref="GateSeverity"/> metadata lives on the descriptor, not on
/// the violation. Phase 2 of Sprint A will refactor
/// <see cref="FeasibilityGateResult"/> to split them so this field can
/// carry advisory entries cleanly. For now it is always empty for rocket
/// results (matches pre-Sprint-A behaviour bit-identically).
/// </param>
public sealed record RocketEngineResult(
    RegenGenerationResult Generation,
    IReadOnlyList<FeasibilityViolation> Violations,
    IReadOnlyList<FeasibilityViolation> Advisories) : IEngineResult
{
    /// <inheritdoc />
    public bool IsFeasible => Violations.Count == 0;
}
