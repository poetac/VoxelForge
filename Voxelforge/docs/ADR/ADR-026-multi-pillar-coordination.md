# ADR-026 — Multi-pillar coordination: verification tracks and subprocess oracles

**Status:** Accepted (2026-05-06)
**Issue:** T2.3 ([#160](https://github.com/poetac/voxelforge/issues/160))
**Supersedes:** —
**Related:** ADR-015 (Core/Voxels/App split), ADR-019 (gate registry), ADR-021 (orchestrator decoupling), ADR-025 (IEngine abstraction)

---

## Context

As voxelforge expands from a single rocket pillar into a multi-pillar platform (rocket,
airbreathing, electric propulsion, marine, CFD verification), the solution now contains
projects that fall into two structurally distinct categories:

1. **Engine pillars** — implement `IEngine<TDesign,TConditions,TResult>` (ADR-025),
   carry SA design variables, feasibility gates, and a schema-versioned design record.
2. **Verification tracks** — consume artifacts produced by pillars (design records,
   contour geometry, VTI exports) and emit calibration drift reports. They have no
   `IEngine` adapter, no `IObjective`, no SA dims, no schema bump.

Without an explicit convention, verification-track projects risk being incorrectly
structured as pillar projects (wrong target framework, wrong reference graph, wrong
CI policy).

This ADR establishes the conventions for verification tracks, with the CFD validation
oracle (`Voxelforge.Cfd.*`) as the first concrete implementation.

---

## Decision

### 1. Solution-folder convention

Engine pillars live in named solution folders (`Airbreathing/`, `Marine/`, etc.).
Verification tracks live in a single `Verification/` solution folder regardless of
which pillar they validate.

### 2. Target framework

Verification-track `*.Core` and `*.Tests` projects use `net9.0` (cross-platform), not
`net9.0-windows`. They may reference `Voxelforge.Core` (also `net9.0`) but must never
reference `Voxelforge` (the main app, WinForms + PicoGK, windows-only) or any
`*.Voxels` project.

### 3. Subprocess oracle contract

Verification oracles that shell out to external tools (SU2, ParaView, OpenFOAM, etc.)
follow the `Voxelforge.Eval` precedent:

- Locate binary via `Environment.GetEnvironmentVariable("SU2_RUN")` → `PATH` fallback.
- Return `null` (not throw) when the binary is absent — CI-safe.
- Emit errors as structured exceptions (`Su2ConvergenceException`, etc.), never
  unstructured stderr.
- Exit codes: `0` = clean, `1` = usage/arg-parse error, `2` = fatal init error.

### 4. CI skip pattern for external-tool tests

Tests that require a binary not bundled with the repo are tagged:

```csharp
[Fact(Skip = "Requires SU2 on PATH — set SU2_RUN env var")]
```

or via a `RequiresSU2` xUnit trait with a corresponding filter:

```bash
dotnet test --filter "Category!=RequiresSU2"
```

The CI workflow for verification tracks is informational (non-gating) when the binary
is absent. It must never block a green build on the rocket or other pillars.

### 5. Shared-abstractions discipline

If a verification track introduces a type that could be consumed by multiple pillars or
other verification tracks, it must be added to `Voxelforge/docs/shared-abstractions-ledger.md`
before the PR merges. The default is: keep types in the verification project. Promote
to `Voxelforge.Core` only when ≥ 2 callers exist.

### 6. Definition-of-Done checklist (§4.6)

Every verification-track PR must confirm:

- [ ] `dotnet build voxelforge.sln` — zero errors, zero warnings.
- [ ] `dotnet test --filter "Category!=RequiresSU2"` — all non-SU2 tests green.
- [ ] `VFA001` + `VFA002` + `VFD001`–`VFD006` analyzer rules clean.
- [ ] `shared-abstractions-ledger.md` updated if any shared-shape types introduced.
- [ ] `CHANGELOG.md` entry under `## Unreleased`.
- [ ] External tool install path documented in `cfd-validation-spec.md` (or equivalent).

---

## Consequences

### Positive

- Verification tracks can be built and tested on Linux CI without PicoGK or WinForms.
- The `null`-on-missing-binary contract means CI never fails on a missing SU2 install;
  drift reports degrade gracefully rather than breaking the rocket test suite.
- Pillar teams don't need to know about verification infrastructure; the ledger provides
  a single authoritative list of shared types.

### Negative / deferred

- Verification projects are not covered by the `bench-regression.yml` CI workflow.
  A separate `cfd-validation.yml` workflow (Sprint C.4) will provide nightly drift
  reporting once SU2 is available on CI runners.
- The `net9.0` target means verification tests can't directly import `Voxelforge.Voxels`
  geometry; if wall-T validation ever needs voxel-resolution geometry, a headless
  adapter in Core will be required.

---

## References

- ADR-025 (`IEngine<TDesign,TConditions,TResult>` abstraction)
- ADR-015 (Core/Voxels/App split — the architectural precedent this ADR extends)
- `Voxelforge.Eval/Program.cs` (subprocess oracle reference implementation)
- Issue [#160](https://github.com/poetac/voxelforge/issues/160) (T2.3 CFD validation)
- Menter, F.R. (1994). "Two-equation eddy-viscosity turbulence models for engineering
  applications." *AIAA Journal*, 32(8), 1598–1605.
