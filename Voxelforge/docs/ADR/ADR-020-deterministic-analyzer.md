# ADR-020: `[Deterministic]` Roslyn analyzer

**Status:** Accepted (2026-04-29)
**Supersedes:** —
**Related:** ADR-009 (feasibility-gate discipline), ADR-017 (multi-chain parallel SA), [issue #209](https://github.com/poetac/voxelforge/issues/209).

## Context

Multi-chain SA's strict-determinism guarantee is load-bearing for reproducibility, audit trail, and bench-regression CI (ADR-017). Today the contract is enforced by integration tests — `MultiChainOptimizerTests.StrictDeterminism_SameSeedAndChains_ProducesIdenticalBest` runs identical seeds on the actual optimizer and asserts bit-identical `(BestParams, BestScore)`. The contract is documented in comments but nothing prevents drift: a careless `DateTime.Now` in a helper, an unseeded `new Random()`, or a `Guid.NewGuid()` in result-tagging would silently break the contract for an entire engine family before tests catch it.

A 2026-04-28 architecture review (recommendation #9) called out this gap explicitly: as the codebase grows — especially when the air-breathing pillars start — the integration-test cover gets more expensive to extend per family and slower to surface drift. A compile-time guard prevents drift at the keystroke instead of at the next CI run.

Three lower-effort alternatives were considered and rejected (see § Alternatives): **warning everywhere** (too soft), **error everywhere** (too strict — `DateTime.Now` in a UI build-stamp is fine), and **purely syntactic enforcement** (misses the transitive case, where a `[Deterministic]` method calls a helper that is itself non-deterministic). The chosen design is **opt-in marker + call-graph closure**: a method is checked iff it has `[Deterministic]` or some transitive caller does.

## Decision

A new `Voxelforge.Analyzers` project ships a Roslyn `DiagnosticAnalyzer` (`DeterministicAnalyzer`) that fires four rules at `DiagnosticSeverity.Error` inside `[Deterministic]` scope:

| Rule   | Pattern                                                                          |
| ------ | -------------------------------------------------------------------------------- |
| VFD001 | `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.{Now,UtcNow}` |
| VFD002 | `new Random()` (zero-arg constructor only — `new Random(seed)` is allowed)         |
| VFD003 | `Guid.NewGuid()`                                                                 |
| VFD004 | `Environment.TickCount`                                                          |

The `[Deterministic]` marker attribute lives in `Voxelforge.Core/Optimization/DeterministicAttribute.cs` (so consumers don't need to reference the analyzer assembly at compile time). Class-level application marks every method on the class.

### Initial marking

This PR marks six surfaces:

| Target                              | Placement              |
| ----------------------------------- | ---------------------- |
| `MultiChainOptimizer`               | class + both `Run` overloads |
| `SimulatedAnnealingOptimizer`       | class only (no `Run` method exists; the driver loop lives in `MultiChainOptimizer.Run`) |
| `CmaEsOptimizer`                    | class + `Run` |
| `NsgaIIOptimizer`                   | class + `Run` |
| `BayesianOptimizer`                 | class + `Run` |
| `RegenChamberOptimization.GenerateWith` | method only (the class also has UI-status-callback methods that aren't deterministic) |

The marking is intentionally narrow. Once the air-breathing pillar's optimizer surfaces stabilise (Step 1 of the scope-expansion roadmap), follow-up PRs can extend marking inward. `HybridSACmaEsOrchestrator` is unmarked in v1; it composes the marked CMA-ES + multi-chain Run methods, which the call-graph closure already covers transitively.

### Call-graph closure

The non-trivial half of the analyzer. Algorithm:

1. **Per-compilation eager build.** On `CompilationStartAction`, the analyzer walks every `MethodDeclarationSyntax`, `ConstructorDeclarationSyntax`, `AccessorDeclarationSyntax`, and `LocalFunctionStatementSyntax` in every syntax tree of the compilation. For each, it resolves the method symbol via `SemanticModel.GetDeclaredSymbol` and walks the body's `IOperation` tree for `IInvocationOperation.TargetMethod` and `IObjectCreationOperation.Constructor` — these become the outgoing call-graph edges. Lambdas / anonymous functions are *not* added as separate nodes; their bodies are already walked as descendants of the enclosing method's body operation. Local functions *are* added as separate nodes (they have distinct `IMethodSymbol` identity).
2. **Seed identification.** A method is a seed if it itself or its containing type carries `[Deterministic]`.
3. **Forward BFS.** From the seed set, walk the call graph forward to compute the full tainted set.
4. **Per-operation check.** On `RegisterOperationAction(Invocation | ObjectCreation | PropertyReference)`, find the enclosing user method (walking up through any lambda / local-function symbols), check membership in the tainted set, then match the operation against the four rule patterns. The whole tainted-set computation is `Lazy<>` — compilations with no `[Deterministic]` usage pay only an attribute-symbol lookup.

The call graph is intentionally **conservative on virtual / interface dispatch**: `IInvocationOperation.TargetMethod` returns the declared method, not all overrides. A `[Deterministic]` method calling an interface whose concrete implementation reads `DateTime.Now` is *not* flagged in v1. This avoids false-positives (we'd taint every override of every base method that any deterministic path touches), at the cost of false-negatives (a non-deterministic override doesn't fire). Future tightening could opt into "flag all overrides" via a stricter mode, gated by a configurable analyzer setting.

### VFD004 narrowing

The original brief proposed flagging both `Stopwatch.GetTimestamp()` and `Environment.TickCount` under VFD004. Audit showed 11 existing call sites of `Stopwatch.GetTimestamp()` inside the marked optimizer Run methods (MultiChain × 2, CMA-ES × 2, NSGA-II × 2, Bayesian × 3, Hybrid × 2). All are pure elapsed-time instrumentation: `swStart = ...; do work; swEnd = ...; result.Elapsed_ms = ...`. None affect `BestParams` or `BestScore` — they're observability, not flow control.

Including `Stopwatch.GetTimestamp()` as a hard error would require either:
- 11 `#pragma warning disable VFD004` suppressions (one per call site, contradicting the brief's "every Error needs to be a real defect" principle), or
- removing elapsed-time reporting from optimizer Run methods (lossy refactor for an instrumentation feature with real value).

So VFD004 is narrowed to `Environment.TickCount` only. `Environment.TickCount` is essentially never legitimate — 32-bit wraparound, Mono platform divergence, monotonic concerns — flagging it is high-precision. `Stopwatch.GetTimestamp()` remains permitted; the regression-guard test `StopwatchGetTimestamp_DoesNotFireVfd004` pins this contract so a future tightening can't silently re-include it.

A future revision could promote VFD004 to flag `Stopwatch.GetTimestamp()` *only when the result reaches a control-flow predicate* (data-flow analysis), but that's out of scope for v1.

### Wiring

Each project that uses `[Deterministic]` adds a `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false">` to `Voxelforge.Analyzers`. In this PR:
- `Voxelforge.Core.csproj` — Core has `[Deterministic]` on five optimizer types.
- `Voxelforge.csproj` (App) — App has `[Deterministic]` on `RegenChamberOptimization.GenerateWith`.

Roslyn analyzers run per-compilation; cross-assembly call closure is *not* analyzed in v1. Each project's `[Deterministic]` boundary is its own concern.

## Consequences

### Positive

- **Compile-time prevention of determinism drift.** A new optimizer author who writes `var t = DateTime.Now;` in a marked method gets a build error before tests run.
- **Greenfield-ready for air-breathing pillars.** When `RamjetOptimization.Run` ships, marking it `[Deterministic]` extends the same guarantee with no analyzer changes.
- **Lazy cost.** Compilations with no `[Deterministic]` usage pay only an attribute-symbol lookup. The full call-graph build runs once per compilation; benchmarks are not measurable on a clean build.
- **Existing optimizer code is determinism-clean.** Build is green after wiring (with VFD004 narrowed); no source changes required to satisfy the analyzer.
- **Regression guards.** 12 tests in `DeterministicAnalyzerTests` pin every rule (positive + negative cases) plus the VFD004 narrowing decision.

### Negative

- **Per-build cost.** Eager call-graph build adds ~50-200 ms to a clean Core compilation. Acceptable; not measurable on incremental builds because Roslyn caches semantic models.
- **Maintenance.** New non-deterministic APIs (e.g., `Random.Shared` if it were ever added inside a marked method) require a new VFD0NN rule. Low frequency.
- **Cross-assembly limitation.** Each consuming project must wire the analyzer separately. The first time air-breathing ships its optimizers, the new project will need its own `<ProjectReference OutputItemType="Analyzer">` entry. Trivial.

### Neutral

- **Solution count.** 10 → 11 projects (`Voxelforge.Analyzers` joins the family alongside `Voxelforge.Generators`).
- **PublicAPI surface.** Adds 1 public type (`Voxelforge.Optimization.DeterministicAttribute`) to `Voxelforge.Core/PublicAPI.Unshipped.txt`. Promotes to Shipped on next baseline rebase.

## Alternatives considered

1. **Warning everywhere.** Reject. With `TreatWarningsAsErrors` global, a warning would be the same as an error — but conceptually softer. The opt-in marker design means we want errors *only* in deterministic scope; warnings everywhere would either flood the build (every UI build-stamp triggers) or be suppressed-by-default (defeats the point).
2. **Error everywhere.** Reject. `DateTime.Now` in `BuildSheet.cs` ("**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm}") is fine — that's a report timestamp, not part of the optimization decision. Forcing those to fail would force a no-determinism escape hatch (`[NotDeterministic]`?) just to compile, and the escape hatch becomes the rule.
3. **Purely syntactic enforcement.** Reject. Without call-graph closure, only direct `DateTime.Now` calls inside `MultiChainOptimizer.Run` would fire. A helper that's called from Run (`Initialize` reading `DateTime.UtcNow.Ticks`, say) would silently break determinism. Call-graph closure is the value-add.
4. **Include `Stopwatch.GetTimestamp()` in VFD004.** Reject. See § VFD004 narrowing. 11 existing sites are pure instrumentation; flagging them is a textbook false positive.
5. **Source-generator marker emitter.** Reject. The `[Deterministic]` attribute is hand-applied to ~6 declarations; auto-emitting it from some convention would lock in the convention before we have evidence of the right cut. Hand-marked is fine for the foreseeable scale.
6. **Limitations as feature.** Reject (we picked this anyway). Documenting the v1 limitations (virtual dispatch, reflection, property/field initializers, Dictionary/HashSet enumeration order) means future tightening is a pure-positive change — not a contract bump.

## Verification

- **Pre-PR:** `main` HEAD = `e6055b8` (post-Track-A) — 2232 / 2233 tests pass + 1 skip.
- **Post-PR build:** `dotnet build voxelforge.sln` clean, 0 warnings, 0 errors (analyzer wired but no real source code triggers any rule).
- **Smoke test:** temporarily injecting `var x = System.DateTime.Now;` into `MultiChainOptimizer.Run` produces `error VFD001: 'DateTime.Now' reads wall-clock time…` at the injection site. Reverted before commit.
- **Analyzer tests:** 12 / 12 pass in `DeterministicAnalyzerTests` — direct + transitive + lambda + method-level-only + the 4 negative regression-guards (no marking, seeded Random, Stopwatch.GetTimestamp, end-to-end).
- **Regression test count:** 2232 → 2244 tests passing (+12 analyzer tests, no other deltas).

## References

- Architecture review recommendation #9 — original auditor recommendation that motivated this ADR.
- [ADR-009: Feasibility-gate discipline](ADR-009-feasibility-gates.md) — the gate-as-SSOT principle that determinism's audit trail depends on.
- [ADR-017: Multi-chain parallel SA](ADR-017-multi-chain-parallel-sa.md) — the strict-determinism contract this analyzer guards.
- [`MultiChainOptimizerTests.StrictDeterminism_SameSeedAndChains_ProducesIdenticalBest`](../../../Voxelforge.Tests/MultiChainOptimizerTests.cs) — the runtime invariant that this analyzer prevents from drifting.
- `Voxelforge.Analyzers/DeterministicAnalyzer.cs` — implementation.
- `Voxelforge.Tests/Analyzers/DeterministicAnalyzerTests.cs` — the 12-test pin.
- [Issue #209](https://github.com/poetac/voxelforge/issues/209) — work item.
