# ADR-037 — Release / versioning strategy

**Status:** Accepted (2026-05-13)
**Sprint:** Post-PR #489 follow-on
**Related:** [ADR-013 Benchmark JSONL schema versioning](ADR-013-benchmark-jsonl-schema.md) ·
[ADR-016 PublicApiAnalyzers tracking](ADR-016-infra-followups-A5-ivoxelhandle-B1.md) ·
[ADR-022 Design persistence schema versioning](ADR-022-design-persistence-schema-versioning.md)

## Context

After PR #489 + PR #497, voxelforge carries:

- ~5 300 `PublicAPI.Shipped.txt` entries (PR #54 / ADR-016 baseline).
- ~150 `PublicAPI.Unshipped.txt` entries added across PR #489 + PR #497 (Economics + SI + optimizer wrappers + EP scaffolds).
- 6 distinct per-pillar schema versions (Rocket v31, Airbreathing v12, EP v10, Marine v5, Nuclear v5, plus the in-repo benchmark JSONL v1).
- 37 ADRs (1–36 + 029a).
- 207 historical commits from the early project history, plus the later commit stream on `main` + branch PRs.
- One bench-fingerprint baseline directory per pillar (`Voxelforge.Benchmarks/baselines/<pillar>/`) carrying SA-run fingerprints across schema versions.

Five distinct versioning concerns coexist:

1. **Public API surface** — `PublicAPI.Shipped.txt` is the source of truth; `Unshipped.txt` accumulates pending additions.
2. **Per-pillar design-persistence schemas** — `*SchemaVersion.cs` per pillar; forward-only identity migrations.
3. **Benchmark JSONL schema** — ADR-013 v1; locked, future changes need new ADR.
4. **PicoGK kernel version** — currently 2.2.0; ADR-011 pin.
5. **Git tag / release history** — none today. No semver tag, no GitHub Release.

This ADR consolidates the convention: when does the project move from
"continuous integration on `main` with PRs gated on green CI" to a
**numbered release cadence**, what does a release artefact contain,
and how do the four other versioning concerns above interact with the
top-level release version?

## Decision

**D1. Top-level release cadence: semver, manual cuts.**

- Versioning scheme: `MAJOR.MINOR.PATCH`.
- Release cuts are MANUAL (the maintainer decides + tags). No nightly / scheduled releases.
- Pre-release suffixes: `-alpha.N`, `-beta.N`, `-rc.N` allowed; no GitHub-Releases auto-publish.

Target cadence (informal): every 2–4 weeks during active development.
Skip cuts when nothing user-visible has shipped.

**D2. Release trigger: PublicAPI movement.**

A release is appropriate when:

- One or more PRs have moved entries from `PublicAPI.Unshipped.txt` to `Shipped.txt` (the canonical "public surface stabilized" signal).
- OR a major user-facing capability ships (a new pillar Wave-1, a new optimizer algorithm, a new schema-bump-worthy field on the rocket or airbreathing pillar).

**Releases never happen on internal-only changes.** The bench-fingerprint refresh, ADR additions, test-coverage expansion, internal refactors — these stay on `main` without a release cut.

**D3. Per-pillar schema versions are independent of the top-level release.**

EP schema v10 (current) does NOT correspond to voxelforge release 10.x.y. The schema-version chain is a *forward-only data-format compatibility chain* per ADR-022; it advances when fields are added regardless of release-cadence considerations. A single voxelforge release may bracket schema bumps across multiple pillars; the release notes summarize "EP v6 → v8" or "Airbreathing v11 → v12" as part of the release-summary diff.

**D4. Bench-fingerprint baselines have their own versioning.**

ADR-013 v1 covers the JSONL schema; the baseline-PNG-fingerprint files themselves carry a hash of the canonical-design preset they were fingerprinted against. Bench-fingerprint refreshes (the periodic "re-baseline after a deliberate model improvement") are tagged by date + `git_sha`, NOT by voxelforge release number. The bench-regression workflow checks fingerprint-match against the most-recent baseline; baseline refresh is a sprint of its own and gets its own ADR if the procedure changes.

**D5. PicoGK kernel pin is independent of voxelforge release.**

Per ADR-011 + the 2026-05-04 PicoGK 2.0.0 adoption (PR #374): the PicoGK version is a separate pin documented in CSPROJ files + ADR-011. A voxelforge release advances when the PicoGK pin moves only if PicoGK introduces user-visible behavior changes — and even then, the PicoGK version itself doesn't drive voxelforge's MAJOR / MINOR bump.

**D6. Release notes structure.**

For each release cut:

```markdown
# Voxelforge v0.X.0 (2026-MM-DD)

## Headline
[One sentence on the most-significant user-visible capability.]

## Public-API delta
[Diff of PublicAPI.Shipped.txt — entries moved from Unshipped.]

## Schema bumps (per pillar)
[Pillar | Previous | Current | Identity-or-breaking | Notes]

## New ADRs
[List of ADRs added since the prior release.]

## Sprints shipped
[Grouped by track — Physics, Optimizer, SI, Validation, Docs.]

## Breaking changes
[None expected during 0.x.]

## Issues closed
[List of GitHub issues closed since the prior release.]
```

CHANGELOG.md `## Unreleased` content becomes the release-notes draft at cut time; afterwards the `## Unreleased` section is reset and the prior content moves under the new `## v0.X.0` header.

**D7. Initial release: defer until two conditions met.**

Voxelforge stays at "unreleased — main is the latest" until:

1. **Local-runner cleanup pass passes** (issue [#501](https://github.com/poetac/voxelforge/issues/501))
   — the 350+ tests on PR #497 verify green + the analyzer warnings clear.
2. **PR #489 cleanup pass closes** — pre-existing cluster-band fixture
   verification (also runner-blocked).

Once both close, cut `v0.1.0` capturing the entire post-PR-#489 + PR #497 state. This is the first release; everything before it is "pre-release / experimental." The 207 early-history commits stay in history but aren't released artefacts.

**D8. Major-version semantics for 0.x → 1.0 transition.**

The 0.x line indicates the public API is mutable. Going to 1.0 requires:

- Every pillar's `Voxelforge.<Pillar>.Core/` assembly has a stable PublicAPI committed (no `Unshipped.txt` entries that aren't intentionally pre-release).
- The cross-pillar coupling work (issue [#502](https://github.com/poetac/voxelforge/issues/502), ADR-035) has shipped at least NEP.W1.
- The `Voxelforge.Eval` subprocess oracle has demonstrated production use.

These three preconditions together are the "the public surface won't break next month" threshold. 1.0 means voxelforge is ready for external dependency reference. Today (PR #497 closure) is closer to 0.5 than to 1.0; current rough plan: 0.5 → 0.6 → 0.7 → 0.8 → 0.9 → 1.0 over ~6-12 months of releases.

## Consequences

**Positive:**
- Clear distinction between "API surface changed" (release-worthy) and "internal-only" (silent commits to main).
- Per-pillar schemas evolve independently of the top-level release cadence — sprint discipline is preserved.
- 0.x semantics document that the API IS mutable, encouraging early adopters to pin specific versions.

**Negative:**
- Manual release cuts mean releases can drift behind `main` — a busy 2-week sprint cycle may end with 30+ commits unreleased. Mitigation: schedule a release cut at the start of every other sprint review.
- No auto-publish to NuGet (voxelforge isn't NuGet-distributed today). When that changes, this ADR needs a supplement covering the package-publishing trigger.
- 0.x ↔ schema-version coupling is loose. A user reading "voxelforge 0.7.0" doesn't immediately know which EP schema version they're getting; they need to consult the release notes (per D6). Mitigation: release-notes table forces the schema-version statement.

## Alternatives considered

**A1. CalVer (YYYY.MM.PATCH) instead of SemVer.** Reject — semver communicates breaking-change risk; CalVer doesn't. Voxelforge has enough cross-pillar API surface that breaking changes matter even at 0.x speed.

**A2. Auto-cut every Friday release.** Reject — empty releases (Friday with no meaningful change) are noise. Manual cuts at the sprint-review cadence are the right shape.

**A3. Map top-level release to the rocket pillar's schema version.** Reject — five distinct versioning concerns; coupling them constrains all four secondary concerns to the rocket pillar's pace. The independent-axes model (D3 + D4 + D5) lets each evolve on its natural cadence.

**A4. Defer this ADR until first release.** Reject — without an explicit policy, the first release cut would have to also write the policy, and that policy would be retrofitted. Capturing the convention now (when no release has happened yet) keeps it clean.

## Implementation status

- **Today** (PR #497 closure approaching): No releases tagged. `main` is the latest. CHANGELOG.md `## Unreleased` captures the post-PR-#489 + PR #497 state.
- **First release (v0.1.0)**: Deferred per D7. Triggered when issues #501 + PR #489 cleanup both close.
- **PublicAPI.Shipped move**: Today everything PR #489 + PR #497 added sits in `Unshipped.txt`. At v0.1.0 cut: move all to `Shipped.txt`; reset `Unshipped.txt`.

## Follow-ons

- **Release-notes-generator tool** — a small CLI helper that walks `git log` between two tags + greps for `feat:` / `docs:` / `fix:` prefixes + emits a draft markdown release notes file matching the D6 template. Demand-gated — write when the first release cut needs it.
- **NuGet package publishing** — when voxelforge wants external dependency reference. Far-future; this ADR doesn't scope it.
- **Branching strategy** — currently `main` + PR branches only; no release branches. If long-running parallel maintenance branches become needed (e.g. v0.5.x bug fixes after v0.6.0 ships), a release-branch policy ADR follows.
