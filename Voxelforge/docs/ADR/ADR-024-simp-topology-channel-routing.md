# ADR-024 — SIMP density-field topology-optimized regen channel routing

**Status:** Accepted (2026-05-04)  
**Issue:** OOB-2 ([#198](https://github.com/poetac/voxelforge/issues/198))  
**Supersedes:** —  
**Related:** ADR-009 (feasibility gates), ADR-020 (determinism), `TopologyOptimizedChannels.cs`

---

## Context

The baseline regen cooling design uses a fixed number of axial channels
(e.g., 80 channels) with constant pitch everywhere along the chamber axis.
This uniform distribution under-cools the throat region (peak Bartz heat
flux ≈ 10–20 MW/m²) and over-cools the cylindrical barrel and exit bell
(flux ≈ 1–3 MW/m²). The thermal consequence is twofold:

1. The throat wall temperature is driven higher than necessary, pushing
   the optimizer toward thinner walls or higher coolant mass flow to
   compensate, both of which erode margin against the `WALL_TEMP` gate.
2. Barrel-section channels carry more coolant per unit heat load than
   needed, consuming pressure budget without proportional cooling benefit.

A density-field optimizer that redistributes `n_channels(x)` in proportion
to the local heat flux would — at the same total channel volume — concentrate
cooling where it is needed and relax it where it is not.

The Sprint P5 work (PH-16 et al.) hoisted Nusselt-factor computation out
of the inner thermal loop, making the per-station heat-flux solve ~10× 
faster and hence viable as an optimization source term. The SIMP sprint
closes the loop: the heat-flux output drives the channel allocation.

---

## Decision

Implement a **SIMP** (Solid Isotropic Material with Penalization)
topology optimizer using the **Optimality Criteria (OC)** method from
Sigmund (2001) to solve for the channel-density field `ρ(x) ∈ [ρ_min, 1]`
per axial station.

### Formulation

**Objective:** maximise cooling effectiveness

```
C = Σ_i  ρ(i)^p · q"(i) · ΔA_gas(i)
```

where `p = 3` (SIMP penalization drives intermediate densities toward 0/1),
`q"(i)` is the Bartz heat flux from a prior `RegenCoolingSolver` run, and
`ΔA_gas(i) = 2π·R(i)·L(i)` is the gas-side area element at station `i`.

**Volume constraint** (same total channel material as baseline):

```
Σ_i  n(i) · W(i) · H(i) · L(i)  ≤  V_target
V_target = Σ_i  N_base · W_base(i) · H(i) · L(i)
```

**OC update rule** (Sigmund 2001, eq. 9):

```
ρ_new(i) = clamp( ρ(i) · √(sens(i) / (λ · vol_sens(i))),  ρ_min, 1 )
```

where:
- `sens(i)     = p · ρ(i)^(p−1) · q"(i) · ΔA(i)`   (objective sensitivity)
- `vol_sens(i) = W_base(i) · H(i) · L(i)`            (volume sensitivity)
- `η = 0.5`                                           (OC damping, standard)
- `λ`                                                 Lagrange multiplier, found by
                                                       60-step bisection

**Integer extraction:**

```
n(i) = max(N_min, round(ρ(i) · N_base,  MidpointRounding.AwayFromZero))
```

with `N_min = 8` (structural floor).

### Analytical pressure drop model

For result reporting and test verification, a per-station Darcy-Weisbach
ΔP model uses fluid density and viscosity derived from the `StationResult`
fields via mass conservation and the Reynolds-number definition — no
fluid-table calls. This introduces ≤ 5 % error vs. a full re-solve;
acceptable for the optimizer's reporting layer.

### Why OC rather than MMA/GCMMA?

The Method of Moving Asymptotes (MMA) and its conservative approximation
(GCMMA) are more general and handle multiple inequality constraints. They
would require a NuGet dependency (e.g., NLopt) that would add the project's
first unmanaged / platform-native library outside PicoGK, violating ADR-015's
zero-new-native-deps rule. The OC update is analytically closed-form for a
single volume constraint, requires no external solver, and converges in
50–100 iterations for this problem class (Sigmund 2001 §4). Zero new
dependencies is a hard constraint for Sprint 1.

---

## Sprint-by-sprint scope

| Sprint | Deliverable |
|--------|-------------|
| **1** (this ADR) | `TopologyOptimizedChannels.Solve()` — Core-only physics solver. `ChannelTopology.TopologyOptimized` enum value + dispatcher wiring. No voxels, no schema bump, no SA integration. |
| **2** | `TopologyOptimizedChannelGeometry.cs` in `Voxelforge.Voxels` — walk `ChannelsPerStation[]`, sweep variable-pitch cylindrical SDF, wire into `ChamberVoxelBuilder` under `Family.TopologyOptimized`. Voxel-build test in `.StlExporter`. |
| **3** | Wire solver into `RegenChamberOptimization.GenerateWith` for the full physics loop; add `TOPOLOGY_CHANNEL_NOT_PRINTABLE` advisory gate (LPBF printability post-pass); end-to-end feasibility test vs. Helical topology. `Closes #198`. |

---

## Consequences

### Positive

- Throat wall temperature can be reduced by 50–200 K at the same total
  channel volume, improving margin against the `WALL_TEMP` gate without
  increasing coolant mass flow.
- The variable channel count field exposes a new spatial design axis that
  can be co-optimised with SA dimensions in Sprint 3.
- The analytical ΔP model provides an inexpensive preview of pressure-drop
  trade-offs for the feed-system stackup without a full re-solve.
- The OC solver is `[Deterministic]` (ADR-020) and introduces no new
  NuGet dependencies.

### Negative / deferred

- The analytical ΔP model derives ρ_c and μ_c from `StationResult`
  fields, not from direct fluid-table calls; ≤ 5 % error accepted.

### Sprint status (as of 2026-05-06)

- **Sprint 1** — Core SIMP solver + `ChannelTopology.TopologyOptimized = 9` shipped 2026-05-04 ([PR #378](https://github.com/poetac/voxelforge/pull/378)).
- **Sprint 2** — `TopologyOptimizedChannelImplicit` + variable-pitch voxel build path shipped 2026-05-05 ([PR #381](https://github.com/poetac/voxelforge/pull/381)).
- **Sprint 3** — End-to-end wiring through `RegenChamberOptimization.GenerateWith` + `TOPOLOGY_CHANNEL_NOT_PRINTABLE` advisory gate shipped 2026-05-05 ([PR #382](https://github.com/poetac/voxelforge/pull/382)). Closes [#198](https://github.com/poetac/voxelforge/issues/198).

---

## References

- Sigmund, O. (2001). "A 99 line topology optimization code written in Matlab."
  *Structural and Multidisciplinary Optimization*, 21(2), 120–127.
  https://doi.org/10.1007/s001580050176

- Bendsoe, M. & Sigmund, O. (2003). *Topology Optimization: Theory, Methods
  and Applications*. Springer, 2nd ed.

- Bartz, D.R. (1957). "A simple equation for rapid estimation of rocket nozzle
  convective heat transfer coefficients." *Jet Propulsion*, 27(1), 49–51.
