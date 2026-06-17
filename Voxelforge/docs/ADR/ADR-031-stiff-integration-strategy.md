# ADR-031 — Stiff-system integration strategy (Crank-Nicolson over BDF2)

**Status:** Accepted (2026-05-12)
**Sprint:** SI.W21
**Related:** PR #489 Sprints SI.W5 (Euler) · SI.W6 (RK4) · SI.W14 (LastResolvedInputs)

## Context

PR #489 (multi-pillar burst, 2026-05-12) shipped the `ComponentNetwork`
ladder Sprints SI.W1 – SI.W20 — DAG sequential evaluator, Gauss-Seidel
iterative solver, stateful components, time-domain integration via
explicit-Euler (SI.W5) and classical 4-stage Runge-Kutta (SI.W6), plus
adapters for 22 pillars including time-varying stateful components for
battery, hydrogen storage, flywheel, and electrolyser.

Both shipped integrators are **explicit / conditionally stable**:
explicit Euler is stable for `dt < 2 / |λ_max|` where `λ_max` is the
largest eigenvalue of the linearised Jacobian, and RK4 is only
marginally less restrictive. The motivating stiff cases that crash both
integrators at engineering-realistic `dt`:

- **Battery thermal runaway** — exothermic reaction with positive
  feedback; effective time-constant collapses from minutes to
  milliseconds as `T` climbs. Explicit integrators need sub-millisecond
  `dt` near runaway, exploding history-array memory.
- **LH₂ tank boil-off with large heat-leak** — fast surface flash
  coupled to slow bulk warming; eigenvalues spread O(10³).
- **RC-dominated DC-DC converter electrical loads** — switching
  transients impose O(10⁵) Hz dynamics on top of O(1) Hz battery /
  thermal dynamics.
- **Large heat-capacity radiator thermal masses** — slow modes (hours)
  coupled with fast control loops (ms) in spacecraft thermal-control.

The user-visible gap: a future power-system / propulsion-stack
simulation that triggers any of these regimes cannot complete in a
reasonable wall-clock budget under SI.W5 or SI.W6.

## Decision

**D1.** Add `IntegrationMethod.CrankNicolson` to the existing
discriminator, dispatching to a new `AdvanceCrankNicolson` arm in
`TimeStepIntegrator`. Existing `ExplicitEuler` / `Rk4` paths remain
bit-identical (the switch only routes when `method == CrankNicolson`).

**D2.** Physics is the **Crank-Nicolson implicit trapezoid rule**:

```
y(t+dt) = y(t) + (dt / 2) · [f(t, y(t)) + f(t+dt, y(t+dt))]
```

Solved by **fixed-point iteration** starting from an explicit-Euler
predictor. Each inner iteration re-solves the algebraic network at the
candidate `y^(k)`, reads `ComputeDerivatives`, and updates `y^(k+1)`.
Convergence criterion: per-state `|Δy| ≤ atol + rtol · |y|` with
`atol = 1e-9`, `rtol = 1e-7`; iteration ceiling 25 per tick.

**D3.** **Crank-Nicolson over BDF2.** Rationale:

| Property | Crank-Nicolson | BDF2 |
|---|---|---|
| Order of accuracy | 2 | 2 |
| Stability | A-stable (full LHP) | A(α)-stable, α ≈ 86° |
| State-history needed | `y(t)` only | `y(t)` + `y(t-dt)` |
| First-step bootstrap | None (single-step) | Needs Euler or RK warm-up |
| Single-tick locality | Yes | No |
| Fixed-point iteration friendly | Yes | Yes |

Both are L²-stable (decay-mode-preserving). BDF2's `y_{n-1}` requirement
forces a bootstrap step + a history buffer; CN avoids both, simplifying
the integration with `SystemSnapshot` / `RestoreSnapshot` (SI.W20 warm-
start) that already covers single-tick state.

**D4.** **Fixed-point inner iteration over Newton-Krylov.** Rationale: the
algebraic-network solve in `ComponentNetwork.Solve` / `SolveIterative` is
the implicit Jacobian source — it re-evaluates every component at the
candidate state. Fixed-point iteration over the trapezoidal recurrence
converges for systems with `|λ · dt / 2| < 1` (the L∞ contraction
condition). For severely-stiff systems (Jacobian eigenvalues spanning
> 10⁶), CN-with-fixed-point loses contraction and the iteration ceiling
trips. Such systems need full Newton — **deferred** as Sprint SI.W22 /
ADR follow-on.

**D5.** Stay deterministic + reproducible. Same network + same `dt` +
same initial state → bit-identical history across runs (pinned by the
test `CrankNicolson_Deterministic_RepeatedRuns`).

## Consequences

**Positive:**
- A-stability handles battery / LH₂ / RC / radiator stiffness without
  sub-tick `dt`.
- Single-step locality + no history buffer simplify the warm-start
  contract (compatible with `SystemSnapshot` / `RestoreSnapshot`).
- Closed-form recurrence `y_{n+1} = y_n · (1 - λdt/2) / (1 + λdt/2)` on
  linear-decay test problems matches exactly — order-2 verification.
- ~150 LOC added to `TimeStepIntegrator.cs` + one enum value; no
  PublicAPI surface change (everything internal).

**Negative:**
- Each tick costs 1 (predictor) + up to 25 (CN iterations) network
  solves. On non-stiff systems CN is 5×-25× slower than Euler at the
  same `dt`. Mitigation: only dispatch CN when stiffness is expected;
  the existing Euler / RK4 paths stay the defaults.
- Fixed-point iteration loses contraction on severely-stiff systems.
  Mitigation: documented as a Sprint SI.W22 follow-on; flag in the
  stiff-test fixture that λ=50 lives near the boundary.
- Two new constants (`CrankNicolsonAbsoluteTolerance`,
  `CrankNicolsonRelativeTolerance`, `CrankNicolsonMaxIterations`) live
  on the `TimeStepIntegrator` class. They are public (for diagnostic
  override) but currently constant; promote to settings if a real
  workload hits the iteration ceiling.

## Alternatives considered

**A1. BDF2.** Rejected per D3 — history buffer + bootstrap step. Same
order, same stiff stability, more state-tracking complexity.

**A2. Implicit Euler (BDF1).** Rejected — order-1 accuracy on systems
where order-2 (CN) costs the same number of network solves per tick.

**A3. SDIRK / ESDIRK (stiffly-accurate diagonal-implicit RK).** Rejected
— substantial code complexity (stage coefficients, embedded-error
estimator). CN is the right order-of-magnitude tool for the current
stiffness envelope.

**A4. Defer to a real stiff system surfacing.** Rejected — the existing
SI.W7 (StatefulBattery), SI.W10 (StatefulFlywheel), SI.W13
(StatefulElectrolyser) all carry positive-feedback / large-capacitance
modes that will hit stiffness as soon as the demo subsystems get
realistic time-constants. SI.W21 is the right pre-emptive layer.

## Implementation

`Voxelforge.Core/Integration/TimeStepIntegrator.cs` (+90 LOC for
`AdvanceCrankNicolson` + 3 helper methods); `IntegrationMethod.cs`
(+1 enum value with documentation). 7 unit tests in
`Voxelforge.Tests/Integration/CrankNicolsonStiffSolverTests.cs` pin:

- Stiff stability (λ=100, dt=0.1: Euler diverges, CN stays bounded).
- Non-stiff convergence (analytical `e^-1` within 3 decimals).
- Order-2 scaling (halving `dt` drops error ≥ 3×).
- Moderately-stiff handling (λ=50 stays bounded over `t=[0,1]`).
- Closed-form recurrence match (`y_{n+1} = y_n · (1 - λdt/2) / (1 + λdt/2)`).
- Cross-method invariant (CN beats Euler at same `dt` on non-stiff decay).
- Strict determinism (repeated runs → bit-identical history).

No PicoGK touch, no schema bump, no analyzer / generator changes, no
PublicAPI manifest entry needed (`IntegrationMethod` is internal).

## Follow-ons

- **Sprint SI.W22 — Adaptive step-size controller.** Uses CN's
  iteration count as the local-error proxy; reduces `dt` when CN
  iterations climb. Pairs with all three integrators.
- **Sprint SI.W23 — Event detection on port zero-crossings.** Find
  `t*` where a port value crosses a threshold (battery SoC = 0.20,
  tank empty); return the state snapshot at `t*` for diagnostics.
- **Sprint SI.W24 (deferred) — Newton-Krylov for severely-stiff
  systems.** Triggered when a real workload hits the
  `CrankNicolsonMaxIterations` ceiling.
