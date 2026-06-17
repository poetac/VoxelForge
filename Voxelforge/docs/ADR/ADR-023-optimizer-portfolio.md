# ADR-023 — Optimizer portfolio: SA primary + multi-algorithm complements

**Status:** Accepted (2026-05-01)
**Supersedes:** ADR-003's "Alternatives rejected" section (CMA-ES / NSGA-II / Bayesian
framing as rejected — those algorithms have since shipped as opt-in complements).
ADR-003 itself remains in force for SA-specific decisions.
**Related:** ADR-003 (SA design), ADR-017 (multi-chain SA), `IObjective.cs`.

## Context

[ADR-003](ADR-003-simulated-annealing.md) decided "use simulated annealing" and
listed CMA-ES, NSGA-II, and Bayesian optimization as rejected alternatives. Since
that ADR was written all three have been implemented and shipped:

- **CMA-ES** — T1.3, PR #173, 2026-04-29.
- **NSGA-II** — T2.4a, PR #174, 2026-04-29; live UI panel T2.4b, PR #327.
- **Bayesian (GP surrogate)** — OOB-4, PR #249, 2026-04-29; MLE auto-fit PR #297.
- **Hybrid SA+CMA-ES orchestrator** — T1.3 follow-on, PR #246, 2026-04-29.

These are not replacements for SA; they are opt-in complements via the `IObjective`
contract (PR #155, 2026-04-28). ADR-003's "Alternatives rejected" framing now
mischaracterises active production code. This ADR records the deliberate portfolio
architecture.

## Decision

**SA is the production default** for the main optimization loop and is not replaced
by any other algorithm. The three additional algorithms fill specific niches:

| Algorithm | Role | Entry point |
|-----------|------|-------------|
| `SimulatedAnnealingOptimizer` (multi-chain) | Primary: full rocket-pillar gate suite (64 gates as of 2026-06), discrete + continuous, warm-start | SA loop in `RegenChamberOptimization` |
| `CmaEsOptimizer` (via `HybridSACmaEsOrchestrator`) | Local refinement after SA convergence on the continuous sub-space | `--bench-sa` CLI hybrid mode |
| `NsgaIIOptimizer` | Multi-objective Pareto front exploration | Live UI panel (`NsgaIIPanelForm`), CLI |
| `BayesianOptimizer` (GP + EI/LCB) | Data-efficient search for expensive evaluation regimes | CLI / bench only |

All four algorithms consume `IObjective.Evaluate(double[]) → EvaluationResult` from
`Voxelforge.Core/Optimization/IObjective.cs`. Only SA is wired into the interactive
UI design loop.

## Why SA stays primary

1. **Many hard gates produce discontinuous responses** (~62 in the rocket pillar today; ~109 project-wide). CMA-ES covariance updates and
   GP surrogate regression both degrade when the response surface is dominated by
   `+∞` cliffs. SA treats `+∞` as uniformly unacceptable without needing a smooth
   objective; the others require feasible regions to be well-populated first.
2. **Discrete / categorical dimensions** (ChannelTopology, ElementType, EngineCycle)
   are natural for SA perturbation but awkward for CMA-ES (continuous only) and
   Bayesian (kernel must handle mixed spaces).
3. **Warm-start from UI baseline.** The interactive workflow relies on SA resuming
   from the designer's current settings. No equivalent concept in NSGA-II or
   Bayesian.
4. **Multi-chain parallelism already provides 4-8× speedup** (ADR-017) without
   requiring a new algorithm.

## Why the alternatives were not "rejected" but deferred

ADR-003 correctly noted the second-pass implementation cost of CMA-ES at the time
of writing. That cost became worth paying once:

- Multi-chain SA (ADR-017) proved the parallel-evaluation infrastructure.
- T1.4 source-generated binder (PR #172) made the SA vector AOT-clean, making
  CMA-ES on the same vector trivial to add.
- OOB-5 Sobol indices identified which dims actually move scoring, enabling
  meaningful Pareto objectives for NSGA-II.
- OOB-4's GP surrogate fit naturally on the same `IObjective` contract, enabling
  Bayesian search with no physics changes.

## `IObjective` contract

All four algorithms call `IObjective.Evaluate(double[]) → EvaluationResult` where:

- Input: the packed SA vector (34 dims, indices 0–33).
- Output: `EvaluationResult { Score: double, IsFeasible: bool, Violations: … }`.

The `+∞` score convention is preserved across all algorithms: infeasible designs
return `Score = double.PositiveInfinity`. CMA-ES and Bayesian require feasible
regions; the SA warm-start path ensures at least one feasible seed candidate exists
before handing off to these algorithms.

## Key files

- `Voxelforge.Core/Optimization/SimulatedAnnealingOptimizer.cs`
- `Voxelforge.Core/Optimization/MultiChainOptimizer.cs`
- `Voxelforge.Core/Optimization/CmaEsOptimizer.cs`
- `Voxelforge.Core/Optimization/NsgaIIOptimizer.cs`
- `Voxelforge.Core/Optimization/Bayesian/BayesianOptimizer.cs`
- `Voxelforge.Core/Optimization/HybridSACmaEsOrchestrator.cs`
- `Voxelforge.Core/Optimization/IObjective.cs`
