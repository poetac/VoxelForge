# ADR-017 — Multi-chain parallel SA with strict-determinism elite migration

**Status:** Accepted, shipped 2026-04-25 via PR #62 (library) + PR #70 (production wiring).
**Supersedes (partially):** the pre-Sprint-14 deferral of "SA outer-loop parallelism" recorded in an earlier performance review. The deferral cited PicoGK's task-thread affinity (PicoGK pitfall #4) as the blocker — that was wrong; voxel ops never run on the SA hot path (they run at export time), so the SA loop has no PicoGK-affinity constraint.

## Context

The single-chain `SimulatedAnnealingOptimizer` ran one Random + one cooling schedule + one restart history per SA invocation. On a 16-thread workstation the SA loop saturated 1 core; the existing `ParallelBatchSize` knob (TIER A.2) achieved core saturation by evaluating N perturbations of the same SA candidate per step, but every batch step still followed one cooling trajectory and one restart history.

The 2026-04-23 optimization-infrastructure audit (project docs, "Tier 1 T1.1") identified multi-chain SA as the highest-leverage open item for the SA pipeline:
- **Throughput**: same as the existing parallel-batch path (cores saturated either way).
- **Search quality**: N independent searches with periodic elite migration are robust to multi-modal score landscapes (the cycle-balance + thermal trade-offs in expander designs are textbook examples).
- **Determinism**: with appropriate barrier discipline, multi-chain produces bit-identical results across runs at the same `(seed, chainCount, maxIterations)`.

## Decision

Adopt the **island model** with `Barrier`-enforced lockstep migration boundaries:

1. Spawn N `SimulatedAnnealingOptimizer` instances, each seeded with `baseSeed + i` for `i ∈ [0, N)`. Each chain has its own RNG, cooling schedule, restart counter, and stagnation history.
2. All N chains run in parallel via `Parallel.For(0, N, ...)`. Each chain's loop:
   - Generates a candidate (Sobol point during warmup; SA `NextCandidate` after).
   - Evaluates the candidate (consumer-supplied evaluator, called concurrently from N threads).
   - Reports the score back to its own SA via `ReportScore`.
   - At every `migrationCadence` (default 100) iterations, calls `barrier.SignalAndWait()`.
3. The `Barrier` post-phase callback runs **once per phase by exactly one (runtime-chosen) participant**. Determinism is preserved because the callback's logic is a pure-deterministic function of the inputs (chain best-scores at the synchronization point):
   - Find the global-best chain (lower score wins; ties broken by lower chain index).
   - Broadcast `(globalBestParams, globalBestScore)` to every other chain via `MigrateFrom`. Receiving chains replace their `_current` walk state but preserve their `_best` history.
4. Cancellation honoured at every iteration boundary AND every barrier; `Barrier.RemoveParticipant` is called when a chain finishes early (infeasible-exit, complete, or cancelled) so the remaining chains don't deadlock at the next barrier.
5. **Sobol warmup (T1.2)**: each chain's first `sobolWarmupCount` candidates (default 64) come from a non-overlapping slice of the global Sobol stream (`SobolSequence.ChainSlice(dim, count, sliceIndex, totalSlices)`). Replaces the SA's uniform-random initial-candidate generation with a quasi-Monte-Carlo low-discrepancy sequence — better-than-uniform initial coverage of the design space.

### Default chain count

`MultiChainOptimizer.DefaultChainCount() = Math.Clamp(Environment.ProcessorCount - 2, 1, 16)`. Reasoning:
- Leaves 2 cores headroom for the OS / IDE on a 20-core dev box (16 chains).
- On a 4-core CI runner: 2 chains.
- On a 1-core fallback: 1 chain (degenerates to pre-T1.1 single-chain SA — `MigrateFrom` no-ops when `N ≤ 1`).

User can override via `MultiChainCount` in `OptSettings` (0 = auto).

### Strict determinism contract

Same `(baseSeed, chainCount, maxIterations)` → identical `(BestParams, BestScore, WinningChain)` regardless of OS scheduler interleaving. Pinned by the `MultiChainOptimizerTests.StrictDeterminism_*` tests (16+ tests across determinism, migration semantics, Sobol warmup, default chain auto-scaling, barrier-doesn't-deadlock-on-early-infeasible-exit).

## Consequences

**Positive:**
- One UI checkbox (`chkMultiChainSa`) flips the entire SA pipeline from single-chain to N-chain.
- The existing single-chain + parallel-batch path is unchanged when the checkbox is off (`UseMultiChain = false` is the default).
- Production code paths that consume the SA result (viewer, Pareto front, batch save) work unchanged with multi-chain via the `FinalizeMultiChainOpt` analog of `FinalizeOpt`.
- Search quality improves on multi-modal landscapes without sacrificing throughput.

**Negative / accepted trade-offs:**
- The `Barrier` synchronization adds a small per-100-iteration sync cost. Negligible compared to the per-iteration thermal-solver cost (~50-200 ms).
- Bench-baselines (`bench-sa-*-2026-04-25.jsonl`) were captured against the single-chain trajectory; running the bench-regression workflow with `UseMultiChain = true` will produce different (likely better) scores. Baseline refresh is deferred until users validate the search-quality improvement on real designs.
- Per-chain best-breakdown is captured by the donor chain only; the migration step intentionally does NOT carry the breakdown across chains (avoids a serialization tax). Consumers that need a foreign chain's breakdown must look at the corresponding `Chains[i].BestBreakdown` in the result.

**Deferred follow-ups:**
- **P21 parallel station march** (per the perf-audit): originally planned to pair with T1.1's per-chain thread-local fluid caches, but the value is now diminished — with N parallel chains, N solvers run concurrently anyway. Deferred to a dedicated sprint if a real workload surfaces a need.
- **Chain-level breakdown migration**: if the UI later wants to compare per-chain trajectories, expose `MultiChainOptimizer.Result.Chains[i].BestParams` + an opt-in `BestBreakdown` carry.
- **CMA-ES per-chain inner refinement** (T1.3): shipped as
  `HybridSACmaEsOrchestrator` (PR #246, 2026-04-29) — SA outer loop
  (global search) + CMA-ES inner loop (local refinement) on the
  `IObjective` contract. See [ADR-023](ADR-023-optimizer-portfolio.md).

## Verification

- Unit tests: `MultiChainOptimizerTests.cs` (16+ tests) pin determinism, migration, Sobol seeding, default chain count, single-chain degeneration, barrier-doesn't-deadlock-on-cancel.
- Build: `dotnet build voxelforge.sln` clean (0 warnings, 0 errors) post-PR-#70.
- Test suite: 1486 / 1486 passing + 1 skip post-PR-#70.
- Public API surface: `Voxelforge.Core/PublicAPI.Unshipped.txt` updated with `MultiChainOptimizer`, `MultiChainOptimizer.Result`, `MultiChainOptimizer.ChainSummary`, `MultiChainOptimizer.ChainProgress`, `MultiChainOptimizer.Run(.., CancellationToken, Action<ChainProgress>?)`, `SimulatedAnnealingOptimizer.MigrateFrom`, `SobolSequence`, `SobolSequence.ChainSlice`.

## References

- Library: [`MultiChainOptimizer.cs`](../../../Voxelforge.Core/Optimization/MultiChainOptimizer.cs), [`SobolSequence.cs`](../../../Voxelforge.Core/Optimization/SobolSequence.cs), [`Optimizer.cs:MigrateFrom`](../../../Voxelforge.Core/Optimization/Optimizer.cs).
- Production wiring: [`MultiChainSession.cs`](../../Optimization/MultiChainSession.cs), `Program.cs:TryStartMultiChainOpt` / `PollMultiChainProgress` / `FinalizeMultiChainOpt`.
- UI: `RegenChamberForm.cs:chkMultiChainSa`, `ToolTipText.MultiChainSa`.
- Tests: [`MultiChainOptimizerTests.cs`](../../../Voxelforge.Tests/MultiChainOptimizerTests.cs).
- Audit context: earlier performance review of the optimization infrastructure.
