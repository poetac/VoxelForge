# ADR-025 — `IEngine<,,>` engine-family abstraction (Sprint A Phase 1)

**Status:** Accepted (Phase 1 shipped 2026-05-04; Phase 2 in progress — partial via per-family `EngineObjectiveAdapter` wrappers; rocket-pillar `RegenObjective` migration tracked in #623; Phase 3 dropped 2026-05-17 — no concrete trigger emerged).
**Date:** 2026-05-04.
**Closes:** memo recommendation #1 (deferred since 2026-04-29 per the rule of three).

## Context

The architecture review's recommendation #1 — `IEngine<TDesign,TResult>`
+ `IEngineDesign` + `IEngineResult` interfaces — was explicitly deferred per
the rule of three: designing the abstraction off rocket alone risked
calcifying around rocket-shaped concerns. The trigger was "after rocket +
ramjet + turbojet exist as 3 concrete implementations."

By 2026-05-04 voxelforge has shipped **6 concrete engine families**:

- Rocket-regen (bell + aerospike topologies — `RegenChamberDesign` /
  `OperatingConditions` / `RegenGenerationResult`).
- Air-breathing × 5: ramjet (sub-step 1a), turbojet (1b), turbofan (1c),
  scramjet (1d), RBCC (1e) — all share `AirbreathingEngineDesign` /
  `FlightConditions` / `AirbreathingResult` with internal dispatch on
  `AirbreathingEngineKind`.

The rule-of-three condition is satisfied. Phase 1 of Sprint A introduces
the unifying contract.

## Decision

Introduce a **ternary** `IEngine<TDesign, TConditions, TResult>` interface
in `Voxelforge.Core/Engines/` together with three marker interfaces.

### Why ternary, not the memo's binary

The memo sketched `IEngine<TDesign, TResult>` with a shared `OperatingConditions`
parameter. In practice the rocket envelope (`OperatingConditions`: propellant
pair, MR, Pc) and air-breathing envelope (`FlightConditions`: flight Mach,
altitude, fuel) are fundamentally different domains. Forcing a synthetic
union would be the worst kind of premature abstraction — exactly the trap
the rule-of-three rule is designed to avoid.

The ternary form lets each family carry its natural conditions type while
still presenting a uniform optimizer surface via `IEngineConditions.Family`
for runtime dispatch.

### Interface surface

```csharp
public interface IEngineDesign     { string Family { get; } }
public interface IEngineConditions { string Family { get; } }
public interface IEngineResult
{
    IReadOnlyList<FeasibilityViolation> Violations { get; }
    bool IsFeasible { get; }
    IReadOnlyList<FeasibilityViolation> Advisories { get; }
}
public interface IEngine<TDesign, TConditions, TResult>
    where TDesign : IEngineDesign
    where TConditions : IEngineConditions
    where TResult : IEngineResult
{
    string Family { get; }
    TResult Evaluate(TDesign design, TConditions conditions);
}
public static class EngineFamilies
{
    public const string Rocket       = "rocket";
    public const string Airbreathing = "airbreathing";
}
```

### Concrete adapters (Phase 1)

- `RegenChamberDesign : IEngineDesign` and `OperatingConditions : IEngineConditions`
  — `Family => EngineFamilies.Rocket`.
- `AirbreathingEngineDesign : IEngineDesign` and `FlightConditions : IEngineConditions`
  — `Family => EngineFamilies.Airbreathing`.
- `AirbreathingResult : IEngineResult` directly (already carried Violations +
  IsFeasible + Advisories).
- `RocketEngineResult : IEngineResult` — new wrapper bundling
  `RegenGenerationResult` with feasibility-gate output (rocket side does not
  carry violations on the result type today; Phase 2 may refactor).
- `RocketEngine : IEngine<RegenChamberDesign, OperatingConditions, RocketEngineResult>`
  — wraps `RegenChamberOptimization.GenerateWith` + `FeasibilityGate.Evaluate`.
- `AirbreathingEngine : IEngine<AirbreathingEngineDesign, FlightConditions, AirbreathingResult>`
  — wraps `AirbreathingOptimization.GenerateWith`.

Both engines are stateless singletons exposed via `Instance`.

## Phasing

**Phase 1 (this commit, 2026-05-04):** introduce contracts + adapters +
contract tests. Existing optimizer / UI / CLI dispatch is **not migrated**
— call sites continue to invoke `RegenChamberOptimization.GenerateWith`
and `AirbreathingOptimization.GenerateWith` directly. The change is purely
additive; the rocket pipeline never regresses (the architecture review's
verification rule).

**Phase 2 (deferred):** migrate the optimizer (multi-chain SA / CMA-ES /
NSGA-II / Bayesian) to consume the IEngine contract generically. Today the
optimizer wrapping (`RamjetObjective`, `TurbojetObjective`, etc.) closes
over the family-specific `GenerateWith` signature; refactoring to a single
`IEngine`-driven `IObjective` adapter eliminates the per-family duplication.
Estimated 3-5 days.

**Phase 3 (dropped 2026-05-17 via #623):** the UI / CLI dispatch
migration was originally framed as switching `RegenChamberForm`,
`Voxelforge.StlExporter`, `Voxelforge.Eval`, and `Voxelforge.Kiosk` to
take an `IEngine` instance. The decision-cleanup audit (#623) found
that no concrete trigger has emerged — the current per-topology
dispatch in the UI is working, and the Avalonia migration (ADR-027)
will rewrite that surface anyway. Phase 3 is formally dropped; the
abstraction landed via the Phase 1 + 2 work is sufficient.

The split kept Phase 1 reviewable (~250 LOC + 7 contract tests) and
de-risked the optimizer-migration work, which had wider blast radius.

## Trade-offs accepted

- **Family discriminator is a string, not an enum.** Strings are extensible
  without recompiling consumers; new families (marine ramjet, gas turbine,
  steam Rankine, fuel cells) drop a new constant into `EngineFamilies` and
  new adapters drop in. An enum would force a type-system update on every
  pillar boundary, defeating the goal.
- **Rocket-side `Advisories` is empty in Phase 1.** The rocket
  `FeasibilityGate.Evaluate` mixes hard + advisory violations into a single
  list today (severity lives on the `FeasibilityGateDescriptor`, not the
  `FeasibilityViolation`). Splitting them is its own refactor; Phase 2 (or
  a focused gate-API sprint) does it. Test
  `IEngineResult_AdvisoriesAreEmptyForRocketUntilPhase2` pins the contract
  so a future change can't silently regress.
- **Voxel building stays out of the IEngine surface.** The headless
  `Evaluate(design, conditions)` path doesn't need voxels and routing
  `IVoxelGenerator` through the interface would re-couple `Voxelforge.Core`
  to `Voxelforge.Voxels`. Voxel-driven flows continue to call
  `RegenChamberOptimization.GenerateWith` directly with the appropriate
  adapter (current ADR-021 seam unchanged).
- **PublicAPI surface grows by ~37 entries.** All in the new
  `Voxelforge.Engines` namespace plus two `Family` getters on
  `RegenChamberDesign` + `OperatingConditions`. Tracked in
  `PublicAPI.Unshipped.txt`; promotes to Shipped on the next API-stabilization
  pass (when Phase 2 lands).

## Verification

- `dotnet build voxelforge.sln`: 0 warnings, 0 errors under
  `TreatWarningsAsErrors=true`.
- 7 new contract tests pass (4 rocket-side, 3 airbreathing-side):
  family-string matching, `Evaluate` parity with the legacy pipeline,
  family-mismatch rejection, advisory-list contract.
- All 2491 + 306 pre-existing tests still green — the change is additive.

## Related

- [ADR-021](ADR-021-orchestrator-decoupling.md) — orchestrator decoupling /
  IVoxelGenerator seam. ADR-021 made `RegenChamberOptimization.GenerateWith`
  voxel-generator-pluggable; ADR-024 makes the orchestrators themselves
  pluggable behind a generic `IEngine`.
- Architecture review (2026-04-28) — recommendation #1 source.
  Recommendation #3 (`IThermodynamicState`) remains deferred to a
  separate sprint; air-breathing's thermo today is a parallel
  `AirbreathingFuelTables` shape, and unifying with rocket's
  `IPropellantTable` is its own ~3-4-day effort.
- Sprint 0 trim (2026-04-29) reserved this work as the rule-of-three
  follow-on. Sprint A Phase 1 closes that ticket.
