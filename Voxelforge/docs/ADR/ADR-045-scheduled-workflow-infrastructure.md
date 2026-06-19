# ADR-045 — Scheduled-workflow infrastructure

**Status**: Accepted  
**Closes**: #650

## Context

Issues #651–#657 + #831 add recurring overnight benchmarks (nightly
fingerprint, BDN microbench, cross-pillar fixture report, multi-seed SA,
Pareto frontier, CFD verification, STL topology validation, auto-baseline
refresh). These share four infrastructure concerns: schedule orchestration,
artifact storage, failure notification, and provenance capture. Solving them
once avoids eight diverging copies of boilerplate.

## Decisions

**D1 — Cron schedule strategy.** Nightly jobs run at **02:00 UTC Mon–Fri**;
weekly jobs run at **03:00 UTC Saturday**. Stagger individual workflows by
5–10 min so the runner pair does not receive a burst of queued jobs at the
same second. Every scheduled workflow also exposes `workflow_dispatch` for
manual runs. The nightly / weekly split provides ≥1 h gap from the last
possible nightly slot so a stalled run cannot block the weekly slot.

**D2 — Artifact storage.** Raw bench outputs (JSONL, BDN HTML/CSV, CFD
reports, Pareto fronts) upload to **GitHub Actions artifacts** with a
30-day retention window — no external storage (S3, Pages) required at
current volume. Committed baseline files in `Voxelforge.Benchmarks/baselines/`
are the only content that persists in git. Nightly outputs are ephemeral;
the artifact history provides the 30-day trend window needed to spot drift.

**D3 — Failure notification.** Every job writes a **GitHub Job Summary**
(Markdown table) on both success and failure; failing steps emit `::error::`
annotations so the Actions UI flags the run at the top-level check list.
Windows Toast + email alerting is deferred to #640 (runner health monitoring)
— the template is designed so #640 can inject a final notification step
without breaking existing callers (all prior steps remain stable).

**D4 — Artifact naming convention.** Artifacts are named
`{bench-name}-{YYYY-MM-DD}-{sha7}` so the Actions artifact list is scannable
and per-day uniqueness is enforced. On-disk JSONL baselines keep the existing
`bench-sa-{preset}-{YYYY-MM-DD}.jsonl` convention from ADR-013.

**D5 — Provenance.** The template exports `VF_GIT_SHA` and `VF_MACHINE_ID`
as environment variables before the bench command runs. Callers embed these
in their JSONL `schema_version` / `git_sha` / `machine_id` fields per
ADR-013. The template does not inject provenance into output files — callers
control their own JSONL serialization.

## Implementation

Reusable workflow at `.github/workflows/_scheduled-bench-template.yml` with
a `workflow_call` trigger. Inputs: `bench-name` (string), `build-project`
(string, default `voxelforge.sln`), `bench-command` (string), `output-path`
(string/glob), `retention-days` (number, default 30), `timeout-minutes`
(number, default 60). Callers inherit: checkout, provenance env-var capture,
restore, Release build, bench-command execution, job-summary write, failure
annotation, artifact upload.

Individual scheduled workflows (#651–#657 + #831) each own their cron
trigger and call the template with `uses: ./.github/workflows/_scheduled-bench-template.yml`.
Concurrency groups are set by each caller, not the template, so different
bench workflows can run in parallel on the runner pair.

## Alternatives considered

- **Composite action** — more flexible per-step injection but does not own
  `runs-on` or `timeout-minutes`; callers must repeat the outer workflow
  boilerplate. Rejected: the shared boilerplate is the high-value part.
- **Shared PowerShell script** — no GitHub integration (no artifact upload,
  no job summary, no `::error::` annotations). Rejected: loses CI visibility.

## Consequences

- #651–#657 + #831 each need only a `uses:` line + their specific inputs.
- A long-running bench that overruns its cron slot will block the next day's
  queued run until the runner is free. Mitigated by per-job `timeout-minutes`.
- `bench-command` is injected into a `run:` step via `${{ inputs.bench-command }}`.
  Callers are committed workflow files (not runtime user input), and the consuming
  workflows trigger only on `schedule` / `workflow_dispatch` (repo-write required),
  so the interpolation is not reachable by untrusted fork input. Keep it
  trusted-input-only — never wire `pull_request`-supplied values into it.

## Cross-links

- ADR-013 — bench provenance schema (`schema_version`, `git_sha`, `machine_id`)
- #640 — runner health monitoring (future alerting transport)
- #651–#657, #831 — consumers of this template
