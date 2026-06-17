# ADR-038 — Electric-propulsion voltage-band widening for modern HV engines

**Status:** Accepted (2026-05-14)
**Sprint:** B.1
**Closes:** [#506](https://github.com/poetac/voxelforge/issues/506)
**Related:** [ADR-036 D3](ADR-036-validation-tolerance-ladder.md) (rules for new fixtures) ·
[ADR-029 D6](ADR-029-plasma-chamber-abstraction.md) (HET gate origin) ·
[ADR-034](ADR-034-electric-propulsion-wave-3-roadmap.md) (Wave-3 EP roadmap)

## Context

Voxelforge's Wave-1 / Wave-2 electric-propulsion feasibility gates pin two voltage hard-bands derived from a 2008-era Goebel & Katz cluster of the most-flown thrusters of the early-2000s:

| Gate | Hard band (pre-B.1) | Cluster basis |
|---|---|---|
| `HET_DISCHARGE_VOLTAGE_OUT_OF_BAND` | `[150, 500] V` | BPT-4000 (300 V), SPT-100 (300 V) — Goebel & Katz §3.6 |
| `GIT_BEAM_VOLTAGE_OUT_OF_BAND` | `[300, 2 000] V` | NSTAR (1 100 V), NEXT-C (1 800 V) — Goebel & Katz §5 |

PR #497 added four published-engine fixtures whose operating points sit outside these bands by design:

| Fixture | Variant | Operating voltage | Pre-B.1 status |
|---|---|---|---|
| HiVHAc | HET | 600 V | OOB (> 500 V) |
| TAL | HET | 300 V | in band |
| NEXIS | GIT | 7 500 V | OOB (> 2 000 V) |
| HiPEP | GIT | 8 000 V | OOB (> 2 000 V) |

Until B.1, the three OOB fixtures shipped with tracking-guard tests (`_AwaitsBandWidening_OutOfBandGateFiresOnly`) that asserted the gate fires — the fixtures were correct by physics but locked behind a stale band. Modern HV-class HET designs (HiVHAc, BHT-8000, HERMeS) operate routinely at 600–800 V; modern HV-class GIT designs (NEXIS, HiPEP, T6 derivatives) operate at 5–10 kV. The Wave-1 / Wave-2 solvers (Busch HET, Child-Langmuir grid) remain valid across these regimes — the bands, not the physics, are the limiting factor.

ADR-036 D3 already governs band motion: "Tightening below documented bands needs calibration; widening above needs documented physical justification." This ADR is the physical-justification record for the widening.

## Decision

**D1. HET discharge-voltage band widened to `[100, 1 000] V`.**

| Bound | Pre-B.1 | Post-B.1 | Justification |
|---|---|---|---|
| Floor | 150 V | 100 V | Busch HET model retains semi-empirical validity down to ~100 V (lower-power TAL family runs near 200 V; sub-150 V is achievable on small-orifice / low-mass-flow HET configurations). Below 100 V the discharge does not reliably ionise xenon — Goebel & Katz §3.4 reports breakdown floor ≈ 90–110 V on Hall accelerators. |
| Ceiling | 500 V | 1 000 V | Covers HiVHAc (600 V) + BHT-8000 (600–800 V) + HERMeS (600 V) cluster + provides headroom for HV-Hall research thrusters. Above 1 kV, channel-wall erosion in dielectric-wall HET designs (BN, alumina-SiC) becomes the binding constraint and is captured separately by `HET_ANODE_OVERHEAT`. The Busch ion-acceleration term scales monotonically with √V_d and stays valid in this regime. |

**D2. GIT beam-voltage band widened to `[200, 12 000] V`.**

| Bound | Pre-B.1 | Post-B.1 | Justification |
|---|---|---|---|
| Floor | 300 V | 200 V | The Child-Langmuir space-charge-limited beam current is a closed-form analytic expression valid for any V_b > 0; the floor exists only to flag designs below NSTAR-class operating points (typical low-Isp T6 / RIT-22 configurations sit at 700–1 100 V). 200 V is the practical lower bound where the beam extraction optics remain workable on standard grid geometries. |
| Ceiling | 2 000 V | 12 000 V | Covers NEXIS (7.5 kV) + HiPEP (8.0 kV) + provides headroom for kilovolt-class NEP concepts (NASA Glenn HiPEP successor studies target 10 kV). The Child-Langmuir physics is closed-form and remains valid arbitrarily high until grid impingement / sputtering becomes the binding constraint — those are flagged separately by `GIT_PERVEANCE_LIMIT_EXCEEDED` (geometric) and `GIT_GRID_LIFETIME_BELOW_FLOOR` (advisory). |

**D3. No advisory or soft gates added or removed.** B.1 is a pure-numeric widening of two hard-band constants. The ADR-036 D3 rule (widening above needs physical justification) is satisfied by D1 + D2 above; no other gates need touching.

**D4. Fixture tracking-guard tests flip from "gate fires" to "gate does not fire + feasible".** The three `_AwaitsBandWidening_OutOfBandGateFiresOnly` tests in HiVHAc / NEXIS / HiPEP fixtures are renamed to `_Within<Gate>_AfterB1` and assert:

```csharp
Assert.DoesNotContain(result.Violations,
    v => v.ConstraintId == "<HET_DISCHARGE_VOLTAGE_OUT_OF_BAND|GIT_BEAM_VOLTAGE_OUT_OF_BAND>");
Assert.True(result.IsFeasible);
```

If a future band-tightening lands, the rename collides with the prior name, which is the desired trip-wire.

**D5. Unit tests update voltage choices.** The `HetFeasibilityTests` + `GitFeasibilityTests` low-/high-edge unit tests previously used voltages that bracket the old band (100 V low / 600 V high for HET; 100 V low / 3 000 V high for GIT). After widening, the high-edge values land inside the new band; they're moved to 1 500 V (HET) and 15 000 V (GIT). Low-edge HET stays at 100 V is now in-band, so the low-edge test moves to 50 V. Low-edge GIT at 100 V is still OOB and unchanged.

**D6. Bench-fingerprint refresh scope.** SA design spaces for HET and GIT in `Voxelforge.ElectricPropulsion.Core/Optimization/` use Wave-1 voltage bounds that sit inside the pre-B.1 hard bands; widening the gate band does not expand the SA bounds and so does not alter any optimiser trajectory. No bench-fingerprint refresh is required by this ADR. A future sprint that explicitly extends the SA bounds (HV-Hall objective, high-Isp GIT objective) would need its own fingerprint refresh — that is out of B.1's scope.

## Consequences

**Positive:**

- The three published-engine fixtures (HiVHAc / NEXIS / HiPEP) now assert feasibility against the actual published operating point rather than a workaround.
- Modern HV-class HET / GIT designs are admissible in feasibility evaluation without an ADR-036 D3 review for every new fixture.
- The Wave-1 / Wave-2 fixtures (BPT-4000, SPT-100, TAL, NSTAR, NEXT-C) remain in-band — the bands widen, never tighten.

**Neutral:**

- The widened bands are still hard gates; designs above 1 000 V (HET) or 12 000 V (GIT) still fire OOB. The next band motion would require a fresh ADR.
- This ADR documents physical-validity bounds. The narrower envelope-of-best-performance is a separate concern (advisory) and is not pinned by the hard band.

**Negative:**

- Designs that previously failed safety-by-OOB (e.g. a misconfigured 700 V HET that should have flagged) now have to fail through other gates (anode overheat, mass utilisation, cathode life). The other HET / GIT gates have always been the binding constraints for any well-formed design — the voltage band was operating-cluster-shape rather than physics-validity — so this is a documentation correction, not a real regression.

## Rejected alternatives

- **Make the band soft (advisory).** Rejected because the OOB envelope IS a hard-physics constraint at the extreme edges (sub-100 V HET ionisation failure; > 15 kV grid breakdown). Hard gates remain appropriate; the widening covers the operating envelope without removing the hard-failure cliff.
- **Per-thruster-class bands.** Rejected because design.Kind is already the discriminator — splitting HET into "low-V" / "HV-Hall" sub-kinds introduces complexity without a corresponding feasibility-distinguishing physical phenomenon at the cluster envelope.
- **Drop the band entirely.** Rejected — the band catches design-input errors (V_d = 5 V, V_b = 50 kV) before the solver wastes CPU on a guaranteed-infeasible run.

## References

- Kamhawi H., Haag T.W., Mathers A.D. (2014). "Investigation of a High Voltage Hall Accelerator (HiVHAc) at NASA Glenn." IEPC-2013-444.
- Polk J.E. et al. (2003). "Performance of the NEXIS Ion Engine." AIAA-2003-4711.
- Foster J.E. et al. (2004). "The High Power Electric Propulsion (HiPEP) Ion Thruster." AIAA-2004-3812.
- Goebel D.M., Katz I. (2008). _Fundamentals of Electric Propulsion: Ion and Hall Thrusters._ JPL Space Science & Technology Series.
- Voxelforge `ADR-029-plasma-chamber-abstraction.md` D6 (Wave-2 HET gates).
- Voxelforge `ADR-036-validation-tolerance-ladder.md` D3 (band-motion policy).
