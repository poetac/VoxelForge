# ADR-040: Family-token SSOT for VFA analyzer

**Status:** Accepted (2026-05-16)
**Supersedes:** —
**Related:** ADR-026 §7 (cross-pillar import discipline), ADR-020 (analyzer policy)
**Issue:** [#554](https://github.com/poetac/voxelforge/issues/554)

## Context

Audit finding F-1 (2026-05-16 architecture audit) showed that the family-purity analyzer pair `CrossFamilyImportAnalyzer` (VFA001) and `FamilyNamespacePurityAnalyzer` (VFA002) had been silently no-op'ing on the Electric Propulsion and CFD pillars since those pillars shipped. The analyzers detect family-specific assemblies by matching the `{Family}` segment of `Voxelforge.{Family}.{Sub}` assembly names against a private `KnownFamilyTokens` list duplicated inside each analyzer. The list contained `Electric` instead of `ElectricPropulsion` and was missing `Cfd` entirely; any assembly outside the list was treated as "not a family assembly" and skipped.

The roster of active pillars lives in `Voxelforge/docs/family-allocations.md` §1 (the human-facing bit-mask + schema-version table) but no machine-readable link connected that roster to the analyzer's detection list. The two analyzer copies of the list could also drift from each other.

Both failure modes — drift between the analyzers, and drift between the analyzers and the live pillar roster — share a single root cause: the token list was hard-coded inside the analyzer files instead of sitting in a named SSOT that both analyzers consume and that the new-pillar checklist points at.

## Decision

### D1 — Single token list inside the analyzer assembly

Family tokens for VFA detection live in `Voxelforge.Analyzers/FamilyTokens.cs` as an internal static class exposing an `ImmutableArray<string> All` field. Both `CrossFamilyImportAnalyzer` and `FamilyNamespacePurityAnalyzer` consume `FamilyTokens.All` rather than maintaining their own copies. The list is sorted alphabetically.

### D2 — SSOT stays inside the analyzer assembly, separate from `EngineFamilies`

The SSOT lives in `Voxelforge.Analyzers/FamilyTokens.cs` (netstandard2.0) — NOT in `Voxelforge.Core/Engines/EngineFamilies.cs` — because analyzer projects cannot take a runtime dependency on Core. The two lists serve different purposes and are kept independent:

- `Voxelforge.Core/Engines/EngineFamilies.cs` enumerates runtime engine-family discriminator strings used by dispatch and pillar selection.
- `Voxelforge.Analyzers/FamilyTokens.All` enumerates assembly-name path segments consumed by the VFA pair at compile time.

Two parallel enumerations is the deliberate tradeoff; collapsing them would require an analyzer-to-Core reference that Roslyn disallows.

### D3 — New-pillar checklist (3 steps)

Adding a new pillar requires updating three locations in the same PR:

1. Append the token to `FamilyTokens.All` in `Voxelforge.Analyzers/FamilyTokens.cs` (alphabetical insertion).
2. Add a row to `Voxelforge/docs/family-allocations.md` §1 (the bit-mask + schema-version table).
3. Add `<ProjectReference Include="..\Voxelforge.Analyzers\Voxelforge.Analyzers.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` to every csproj for the new pillar (`.Core`, `.Tests`, `.Voxels`, `.StlExporter` — four projects per pillar).

## Consequences

### Positive

- VFA001 and VFA002 now fire correctly on the Electric Propulsion and CFD pillars; audit F-1 is resolved.
- Future pillar additions have a single token-list edit location and a documented 3-step checklist, eliminating the silent-skip failure mode that affected EP and CFD.

### Negative

- Two parallel "family" enumerations exist (`FamilyTokens.All` in the analyzer assembly, `EngineFamilies` in Core). The tradeoff is acceptable because analyzers cannot reference Core; D2 documents the boundary.
- Audit finding F-2 (missing `OutputItemType="Analyzer"` wirings on 8 pillar csprojs) remains open and is tracked as a Team B follow-up. The token fix is necessary but not sufficient for full VFA enforcement on those projects — a project that does not reference the analyzer at all never runs VFA001/VFA002 regardless of the token list.

## References

- Audit finding F-1 — 2026-05-16 architecture audit
- [Issue #554](https://github.com/poetac/voxelforge/issues/554)
- ADR-026 §7 (cross-pillar import discipline)
- `Voxelforge/docs/family-allocations.md` §1 (pillar roster)
- `Voxelforge.Analyzers/FamilyTokens.cs` (the SSOT)
