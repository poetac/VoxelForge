# ADR-003 — Simulated annealing optimizer

**Status:** Accepted
**Date:** 2026-04-21 (documented; decision predates the v4.3 era)

## Context

The design-space optimization problem:
- **34** continuous variables today (see [ADR-012](ADR-012-adding-an-sa-design-variable.md)
  for how they're declared via `[SaDesignVariable]`).
- **53** hard feasibility gates (see [ADR-009](ADR-009-feasibility-gates.md))
  that produce `+∞` scores on violation.
- Expensive evaluations (~3 ms physics-only, ~30 s with full voxel build).
- No gradients available — gates are discontinuous, physics has clamps.
- Parallel evaluation possible per-candidate; voxel build must serialize
  on the PicoGK task thread.

Alternatives considered: CMA-ES, NSGA-II, Bayesian (Gaussian process),
grid search, gradient-based via autodiff.

## Decision

Use **simulated annealing** (`Optimization/Optimizer.cs`) with:
- Auto-cooling rate fit to iteration budget.
- 50/50 mixed perturbation (all-dim exploration + single-dim exploitation).
- Stagnation-detect restart at 35 % perturbation + 40 % reheat.
- Warm-start from UI baseline.
- 8-way parallel batch for per-candidate physics-only evaluation
  (`OptSettings.ParallelBatchSize`).
- Persistent-infeasibility early exit (default 60-consecutive-∞ streak).

## Alternatives rejected (for the primary optimization role)

> **Note (2026-04-29):** CMA-ES, NSGA-II, and Bayesian optimization have
> all been implemented since this ADR was written — as *opt-in complements*,
> not replacements. SA remains the production default. See
> [ADR-023](ADR-023-optimizer-portfolio.md) for the full portfolio
> architecture and the rationale for why each algorithm occupies its
> current role.

- **CMA-ES** — benefits marginal at 34 dims when 53 hard gates produce
  highly discontinuous responses; SA handles the `+∞` gate cliff more
  naturally. CMA-ES has since shipped (T1.3 / PR #173) as a local-
  refinement complement via the hybrid orchestrator.
- **NSGA-II** — multi-objective is desirable but a Pareto front is
  tracked *alongside* SA's scalar score; full NSGA-II was deferred until
  a pulling need existed. NSGA-II has since shipped (T2.4a / PR #174)
  as a Pareto-exploration complement with a live UI panel.
- **Bayesian (GP)** — expensive at high dims; gates make the response
  surface discontinuous. Bayesian optimization has since shipped
  (OOB-4 / PR #249) as a data-efficient search complement for small
  iteration budgets.
- **Gradient-based** — no gradients available; gates are discontinuous,
  physics has clamps. Gradient-based methods remain rejected.

## Consequences

Positive:
- Shipping cost was ~1 week.
- Handles the 53 hard gates correctly: SA treats `+∞` as uniformly
  unacceptable; a new-best that's finite is always accepted.
- Deterministic given same seed (critical for reproducible runs and the
  per-iteration RNG seeding the harness depends on).

Negative:
- The original `Bounds[]` debt has been **resolved**: Sprint 6 Track A +
  Sprint 7 Track C migrated `Bounds`, `Pack`, and `Unpack` to a
  registry-driven implementation. Adding a new SA variable is now a
  one-line `[SaDesignVariable]` attribute — see
  [ADR-012](ADR-012-adding-an-sa-design-variable.md).
- The `IObjective` interface (PR #155, 2026-04-28) provides the
  optimizer-neutral evaluation contract; the hybrid SA+CMA-ES orchestrator
  (PR #246) builds on it. All four portfolio algorithms consume
  `IObjective.Evaluate` — see [ADR-023](ADR-023-optimizer-portfolio.md).

## Test coverage

- `OptimizerTests.cs` covers the reheat / restart / warm-start paths.
- `Phase4PerfTests.SkipMfgAnalysis_SAEvaluate_PicksSameScore` defends the
  parallel-batch shortcut doesn't change selection.
- `Phase9ResourceBudgetTests` covers persistent-infeasibility exit.
