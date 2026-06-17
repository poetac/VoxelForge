# ADR-033 — Network validation + diagnostic strategy

**Status:** Accepted (2026-05-12)
**Sprint:** Post-PR #489 follow-on
**Related:** PR #489 Sprints SI.W16 (PowerBalance) + SI.W18 (NetworkValidator) ·
[ADR-031 Stiff integration](ADR-031-stiff-integration-strategy.md)

## Context

The `ComponentNetwork` ladder (PR #489 Sprints SI.W1 – SI.W20) ships a
runtime graph + integrator. Wiring mistakes (unfed inputs, dropped
connections, unit-mismatched ports) and conservation errors (energy /
mass / charge not balancing across time) only surfaced one at a time:

- **SI.W18** (NetworkValidator) catches static topology errors:
  unconnected outputs, unfed inputs, overdetermined inputs, multiple
  sources, cycles. Run at design-build time before `Solve()`.
- **SI.W16** (PowerBalance) catches runtime `_W` port imbalances per
  tick. Run on the resolved port snapshot after `Solve()`.

Two gaps remained:

1. **Unit-suffix consistency.** The port-naming convention (`_W`, `_A`,
   `_V`, `_kgs`, `_K`, `_Pa`, …) carries SI units in the port name, but
   nothing enforces it. A connection wiring `Voltage_V` → `Current_A` is
   structurally legal under SI.W18 but physically wrong.

2. **Time-aggregated conservation.** PowerBalance gives per-tick
   instantaneous net power [W]. The *cumulative* energy [J] across the
   run (and analogous mass [kg] / charge [C]) is what mission-level
   planning + drift-residual diagnostics actually need.

This ADR captures the post-PR-#489 extensions (SI.W24 + SI.W25 + SI.W26)
that close both gaps, plus the strategy for future validation +
diagnostic additions.

## Decision

**D1. Layered validation.** Three layers, each consumed independently:

- **Static (build-time)**: `NetworkValidator.Validate(network)` runs
  before `Solve()`. Catches structural errors that would otherwise
  surface as `InvalidOperationException` at solve time, or as silent
  data corruption (unit mismatch).
- **Per-tick (solve-time)**: `TimeHistoryAnalytics.PowerBalance(history)` /
  `MassFlowBalance(history)` / `CurrentBalance(history)`. Consume the
  resolved port snapshots and report aggregate balance per tick.
- **Cumulative (post-run)**: `TimeHistoryAnalytics.CumulativeEnergy_J(history)` /
  `CumulativeMass_kg` / `CumulativeCharge_C`. Trapezoidal-rule integrals
  over the per-tick balance series.

The layers are independent: a network that passes static validation
may still show power imbalance per tick (solver residual / time-step
artefact); a network with clean per-tick balance may still drift
cumulatively (numerical noise compounding). Each layer surfaces a
different failure mode.

**D2. Suffix-driven discovery.** Both the static unit-mismatch check
(SI.W24) and the per-tick balance reporters (SI.W16 / SI.W25) discover
ports by name-suffix matching. The recognized SI suffix set lives in
`NetworkValidator.RecognizedUnitSuffixes` (private) and currently spans
25 entries (`_W`, `_kW`, `_kWh`, `_J`, `_Nm`, `_V`, `_A`, `_kg`, `_kgs`,
`_g`, `_gs`, `_Pa`, `_bar`, `_m3`, `_m3s`, `_L`, `_Ls`, `_K`, `_C`,
`_m`, `_mm`, `_m2`, `_mm2`, `_rad`, `_rads`, `_rpm`, `_s`, `_ms`, `_hr`,
`_N`).

Suffixes outside the recognized set act as descriptors (`_total`,
`_frac`, `_avg`, `_max`, `_min`) and skip the SI-unit checks. This is
deliberate: a port named `Efficiency_frac` is dimensionless; enforcing
unit-consistency on it would yield false-positive warnings.

**D3. Severity bands.**

- Unit mismatch → **Warning** (not Error). Some legitimate cross-unit
  conversions exist (a voltage-to-current divider component will wire
  `Voltage_V` to a derived `Current_A` port on purpose). The warning
  surfaces the mismatch for human review without blocking the solve.
- Unfed input → **Error**. The solve cannot proceed; the optimizer would
  exception at runtime. Catch it before then.
- Overdetermined input (both external feed + internal connection) →
  **Warning**. The external feed wins (SI.W2 contract); the warning
  flags the ambiguity for human review.
- Multiple sources for one input → **Error**. Last-wired wins, which is
  fragile + version-dependent.
- Cycles → **Warning**. `Solve()` throws but `SolveIterative()` accepts.
  The warning tells the user to switch methods.

The static `NetworkValidator.Validate(network).IsValid` predicate is
true iff `ErrorCount == 0`; warnings + infos don't gate validity.

**D4. Strict suffix matching, not pattern matching.** A port named
`Power_W_total` is NOT matched by `_W` (only by `_total`, which is a
descriptor and skipped). This avoids false positives at the cost of
forcing the naming convention.

**D5. Aggregators are pure functions of `IReadOnlyList<TimeHistorySnapshot>`.**
All four post-run helpers (PowerBalance, MassFlowBalance,
CurrentBalance, plus the three Cumulative_* variants) accept the
history slice + return a fresh list. No state on `TimeStepIntegrator`,
no side effects. Multiple aggregators can run on the same history
slice; results are independent.

## Consequences

**Positive:**
- Three orthogonal diagnostic layers, each catching a distinct failure
  mode. Composition is mechanical (run all three after every demo run
  in CI).
- Suffix-driven discovery means new pillars / components automatically
  participate in the balance + unit checks without code coordination
  — just name your ports with SI suffixes.
- Cumulative aggregators give the data shape needed for mission-level
  analysis (total energy delivered, total propellant consumed) without
  per-pillar wiring.

**Negative:**
- The recognized-suffix set is a moving target. Components that
  introduce novel units (e.g. a future radio link adding `_dBm`) must
  add the suffix to `RecognizedUnitSuffixes` or accept that their ports
  skip the check. Drift here is silent until someone notices.
- Unit-mismatch as Warning (not Error) means a careless user can ship
  unit-mismatched designs that solve cleanly + produce garbage. The
  alternative (Error) would block legitimate converters; Warning is the
  right severity band for now.
- Cumulative aggregators compute O(n) work per call. For a 100k-tick
  history this is fast (~1 ms), but a future need for streaming /
  windowed aggregation would require new API surface.

## Alternatives considered

**A1. Per-component balance check.** Reject — would require every
component adapter to declare a power-dissipation port + the validator
to subtract it. Too invasive for the demonstrated value (the suffix-
driven aggregate balance catches the common failure modes already).
Revisit if a real workload exposes a per-component leak the aggregate
misses.

**A2. Stronger unit-mismatch severity.** Reject (this ADR D3) — would
require an "intentional conversion" escape hatch on every legitimate
cross-unit connection. The warning + human review path is lighter.

**A3. Drop the post-run aggregators, keep only per-tick.** Reject —
mission-level mass / energy budgets need the cumulative form. Adding
it to the API surface now is cheap; retrofitting later would force
callers to write their own trapezoidal-rule wrapper.

**A4. Make the suffix set discoverable at runtime (e.g.
`NetworkValidator.RegisterUnitSuffix("_dBm")`).** Reject — adds a new
registration surface for marginal value. The hardcoded set is easier
to audit. Promote to dynamic registration only when the suffix
catalogue exceeds ~50 entries.

## Implementation status

Live in `Voxelforge.Core/Integration/`:

- `NetworkValidator.cs` — SI.W18 (build-time topology) + SI.W24 (unit
  suffix) static-analysis pass. Returns `ValidationReport` with
  Info / Warning / Error severities + per-category Issues.
- `TimeHistoryAnalytics.cs` — SI.W16 (`PowerBalance`) + SI.W25
  (`MassFlowBalance`, `CurrentBalance`) per-tick reporters + SI.W26
  (`CumulativeEnergy_J`, `CumulativeMass_kg`, `CumulativeCharge_C`)
  cumulative aggregators.

Tests in `Voxelforge.Tests/Integration/`:

- `NetworkValidatorTests.cs` (SI.W18 baseline)
- `UnitSuffixConsistencyTests.cs` (SI.W24, +6 tests)
- `AggregateBalanceTests.cs` (SI.W25, +9 tests)
- `CumulativeAggregatorTests.cs` (SI.W26, +12 tests)

## Follow-ons

- **SI.W27 — Component-tagged balance.** Filter the aggregate balance
  by component tag (e.g. only "thermal-management" components, only
  "EV-powertrain"). Useful for subsystem-level diagnostics.
- **SI.W28 — Streaming aggregator.** Replace `IReadOnlyList<…>` with
  `IAsyncEnumerable<…>` for long-running simulations where the history
  doesn't fit in memory. Demand-gated (no real workload triggers it
  today).
- **Per-component energy bookkeeping.** Each adapter declares which of
  its ports are "source" vs "sink"; the per-component balance reports
  dissipation. Higher-fidelity than the aggregate; matches A1 above —
  revisit when warranted.
- **Newer recognized-unit suffixes.** Add `_dBm` (RF link budgets),
  `_T` (magnetic flux density — already used by EP.W3.AF
  `MpdAppliedFieldStrength_T`), `_S` (siemens / conductance), `_H`
  (henry / inductance) if/when pillars introduce them.
