# ADR-041: Internal-by-default discipline for new pillar Core types

**Status:** Accepted (2026-05-16)
**Supersedes:** —
**Related:** ADR-016 (PublicApiAnalyzers wiring), ADR-026 (multi-pillar coordination)
**Issue:** #565

## Context

The project's pillar Cores (`Voxelforge.Airbreathing.Core`,
`Voxelforge.ElectricPropulsion.Core`, `Voxelforge.Marine.Core`,
`Voxelforge.Nuclear.Core`, plus the rocket surface in `Voxelforge.Core/`)
house the bulk of the physics + optimization code. Each pillar grew
organically and the share of types marked `public` has crept upward
without an explicit gating step.

A pillar's true external contract is small: the `IEngine<,,>` adapter
(ADR-025), its `*Objective.Build` factory, perhaps a few result records
and the schema-versioning entry points. Everything else — helpers,
intermediate types, internal solvers, voxel-side adapter classes — has
no consumer outside the assembly and should default to `internal`.

Default-public is the path of least resistance in C# and produces a
wider blast radius for routine refactors. PR 4 (#551) demonstrated this:
removing 6 public surfaces in `RegenChamberOptimization` required
`*REMOVED*` entries in `PublicAPI.Unshipped.txt` and 54 caller migration
sites even though no external consumer of those surfaces existed — the
surface was public only because nothing forced the author to choose
otherwise at the point of introduction.

## Decision

**D1 — Default to `internal`.** Every new type introduced under
`Voxelforge.{Pillar}.Core/**` (and under `Voxelforge.Core/**`
subnamespaces beyond the small set of "true" public surfaces — engine
adapters, objective factories, persistence entry points, the registered
optimizer + integration surfaces) defaults to `internal`. Promotion to
`public` requires (a) a one-line justification in the PR description
naming the consumer ("required by the SU2 oracle subprocess",
"cross-pillar coupling per ADR-035", "consumed by `Voxelforge.Voxels`
adapter", etc.) AND (b) a `PublicAPI.Unshipped.txt` entry produced by
the analyzer quick-fix.

**D2 — Existing surface is grandfathered.** This ADR is forward-only.
The 2026-05-16 architecture audit (public-API pass) flagged 3 over-public utilities in
`Voxelforge.Core/Combustion/` for demotion (tracked by Team B under
issue #559); other demotions are case-by-case and not blanket-scoped by
this ADR. The `PublicAPI.Shipped.txt` baselines stay as they are.

**D3 — PR review checklist.** PR review prompts a checkbox of the form
"every new `public` type / member has an explicit reason in the PR
description." Codifying this in `.github/PULL_REQUEST_TEMPLATE.md` is a
follow-up tracked separately and is out of scope here.

## Consequences

**Positive:**
- Smaller per-pillar public surface ⇒ less `PublicAPI.{Shipped,Unshipped}.txt`
  churn on refactors and renames.
- Clearer contract boundary: the public surface becomes the documented
  one (engine adapter + objective + persistence) instead of "whatever
  happened to be `public`".
- Enables `internal sealed` performance optimizations (JIT devirt) on
  types that previously had to stay `public` defensively.

**Negative:**
- Occasional friction when a `*.Tests` project needs visibility — solved
  by the `InternalsVisibleTo` attribute already wired in every Core
  `.csproj` for its sibling `*.Tests` (and for `*.Voxels` /
  `*.StlExporter` where needed).
- Requires PR-review discipline to maintain. An analyzer that flags
  newly-introduced `public` types without a corresponding PR-description
  marker would be possible but is deferred until the discipline shows it
  is needed.

## References

- The 2026-05-16 architecture audit (public-API pass)
- Issue #565
- ADR-016 (PublicApiAnalyzers wiring — RS0016 / `PublicAPI.{Shipped,Unshipped}.txt`)
- PR 4 (#551) as the motivating recent example (6 public surfaces
  removed, 54 caller migrations, no external consumer).
