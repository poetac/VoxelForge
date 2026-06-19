# ADR-029 — Plasma-chamber abstraction (Wave-2 HET pre-commit)

**Status:** Accepted (2026-05-08)
**Issue:** [#466](https://github.com/poetac/voxelforge/issues/466)
**Supersedes:** —
**Related:** ADR-022 (schema versioning), ADR-025 (`IEngine<,,>`), ADR-026 §6 (Wave-2 plasma-chamber pre-commit), ADR-019 (gate registry)

---

## Context

The Electric Propulsion pillar shipped Wave-1 (resistojet) with `ElectricPropulsionEngineKind.HallEffect = 3` reserved at [`ElectricPropulsionEngineKind.cs:52`](../../../Voxelforge.ElectricPropulsion.Core/ElectricPropulsionEngineKind.cs#L52) and bit 8 reserved in [`family-allocations.md` §1](../family-allocations.md#§1) for `ElectricHallEffect`. Wave-2 introduces plasma physics — ion velocity, plume divergence, magnetic confinement — that the electrothermal solvers (`ElectrothermalHeaterSolver`, `IsentropicNozzleSolver`) do not model.

[ADR-026 §6](ADR-026-multi-pillar-coordination.md) and [`pillar-specs/electric-propulsion.md` §9](../pillar-specs/electric-propulsion.md) explicitly defer Wave-2 to a follow-up ADR-029 audit settling: (a) plasma-state abstraction shape, (b) variant-dispatch idiom, (c) conditions-record strategy, (d) validation-tolerance contract, (e) SA bounds, (f) gate strategy. This ADR is that audit. Hall-Effect Thruster (HET) is the first concrete consumer; arcjet / gridded ion / MPD inherit the contract.

---

## Decision

### D1. `IPlasmaState` placement

Introduce `IPlasmaState` at `Voxelforge.ElectricPropulsion.Core/Plasma/IPlasmaState.cs` with three read-only properties common to all plasma-variant engines:

- `IonExitVelocity_ms`
- `BeamCurrent_A`
- `PlumeDivergenceHalfAngle_rad`

Concrete `HetPlasmaState : IPlasmaState` adds `MagneticField_T`, `MassUtilization`, `BeamEfficiency`, `DischargePower_W`. The interface stays in the EP pillar (not `Voxelforge.Core`) until **rule of three** is met: HET + MPD + GriddedIon. At that point a follow-on ADR promotes `IPlasmaState` to `Voxelforge.Core/Plasma/`. This mirrors the deferred-promotion discipline in [`shared-abstractions-ledger.md` §5](../shared-abstractions-ledger.md#§5).

### D2. Variant-dispatch pattern

Adopt a `switch (design.Kind)` block inside [`ElectricPropulsionOptimization.GenerateWith`](../../../Voxelforge.ElectricPropulsion.Core/ElectricPropulsionOptimization.cs), mirroring [`MarineOptimization.cs:39-45`](../../../Voxelforge.Marine.Core/MarineOptimization.cs#L39-L45):

```csharp
return design.Kind switch
{
    ElectricPropulsionEngineKind.Resistojet => RunResistojetPipeline(design, cond),
    ElectricPropulsionEngineKind.HallEffect => RunHetPipeline(design, cond),
    _ => throw new NotSupportedException(/* arcjet / ion / MPD reserved */),
};
```

The Wave-1 `if (Kind != Resistojet) throw` arm becomes the `_ => throw` default. Future plasma variants extend the switch additively. Rejected: a registry-driven dispatcher (the rocket-style `GateRegistry` predicate signature is rocket-shaped per [ADR-026 §9 risk #2](ADR-026-multi-pillar-coordination.md) and not in this audit's scope).

### D3. Conditions-record strategy

Keep `ResistojetConditions` (do **not** rename or generalise). HET-specific design knobs (DischargeVoltage, DischargeCurrent, MagneticField, AnodeRadius, ChannelLength, XenonMassFlow, AnodeMaterial, CathodeType — eight fields total) ride on `ElectricPropulsionEngineDesign` as `init`-only properties with NaN/sentinel defaults. Resistojet ignores them; HET reads them.

Rationale: the design record's class-level comment at [`ElectricPropulsionEngineDesign.cs:1-13`](../../../Voxelforge.ElectricPropulsion.Core/ElectricPropulsionEngineDesign.cs#L1-L13) already chose "single record over per-kind subtypes so the SA framework's reflection-driven binding works without special-casing." This ADR ratifies that choice for HET. Wave-3 (the third concrete plasma engine) reopens the question of splitting conditions records.

### D4. Validation-tolerance contract

Wave-2 HET fixtures assert **±20 % thrust, ±15 % Isp** against the published anchor (BPT-4000 — Aerojet Rocketdyne, 4.5 kW, 300 V / 15 A / 16 mg/s Xe / B = 0.02 T → T ≈ 0.242 N, Isp ≈ 1543 s).

Wider than MR-501B's ±10 % / ±8 % ([`ElectricPropulsionFixture_MR501B.cs:37-39`](../../../Voxelforge.ElectricPropulsion.Tests/Validation/ElectricPropulsionFixture_MR501B.cs#L37-L39)) because the Busch discharge model (Goebel & Katz §3) lacks ionisation-rate calibration that real BPT-4000 hardware exhibits. Future tightening uses the `[Fact(Skip = "...")]` idiom with calibration justification — the model is [`ElectricPropulsionFixture_MR501B.cs:84-103`](../../../Voxelforge.ElectricPropulsion.Tests/Validation/ElectricPropulsionFixture_MR501B.cs#L84-L103).

### D5. SA bounds (HET design vector)

6-dim, mirroring `ResistojetObjective`'s 6-dim layout. Bounds derived from Goebel & Katz *Fundamentals of Electric Propulsion* §3 Table 3-1 (BPT-4000 / SPT-100 / PPS-1350 cluster):

| Dim | Variable                | Bounds                        | Citation                          |
|----:|-------------------------|-------------------------------|-----------------------------------|
| 0   | DischargeVoltage_V      | 200 – 400 V                   | Goebel & Katz §3.4                |
| 1   | DischargeCurrent_A      | 5 – 25 A                      | Goebel & Katz §3.4                |
| 2   | MagneticField_T         | 0.01 – 0.03 T                 | Goebel & Katz §3.6 (peak in channel) |
| 3   | AnodeRadius_mm          | 20 – 60 mm                    | Goebel & Katz §3.3 (annular geom)  |
| 4   | ChannelLength_mm        | 15 – 40 mm                    | Goebel & Katz §3.3                 |
| 5   | XenonMassFlow_kgs       | 5e-6 – 3e-5 kg/s              | Goebel & Katz §3.4                 |

Bind-time clip on `DischargeVoltage_V × DischargeCurrent_A ≤ conditions.BusPower_W_avail`, mirroring the resistojet bus-power clip at [`ResistojetObjective.cs`](../../../Voxelforge.ElectricPropulsion.Core/Optimization/ResistojetObjective.cs).

### D6. Gate strategy

Single `ElectricPropulsionFeasibility.Evaluate` entry point, kind-predicated. Existing 5 hard + 5 advisory resistojet gates wrap in `if (design.Kind == Resistojet)`; new 6 HET gates wrap in `if (design.Kind == HallEffect)`:

| ConstraintId                        | Severity | Meaning                                                |
|-------------------------------------|----------|--------------------------------------------------------|
| `HET_DISCHARGE_VOLTAGE_OUT_OF_BAND` | Hard     | V_d outside 150–500 V envelope                         |
| `HET_ANODE_OVERHEAT`                | Hard     | Anode wall T > material limit (graphite 2000 K, BN 1500 K, Al₂O₃-SiC 1900 K) |
| `HET_MAGNETIC_FIELD_INSUFFICIENT`   | Hard     | B-field too weak to confine electrons (Hall parameter < 100) |
| `HET_PLUME_DIVERGENCE_EXCESSIVE`    | Advisory | half-angle θ > 30° (cosine loss > 13 %)                |
| `HET_CATHODE_LIFE_LIMIT`            | Advisory | cathode operating I > 1.2 × rated I (life-curve hot end) |
| `HET_MASS_UTILIZATION_LOW`          | Advisory | η_m < 0.85 (under-ionised plasma)                      |

Rejected: a separate `HetFeasibility` static class (duplicates the helper-method pattern; per-pillar unification deferred to ADR-027 follow-on per [`shared-abstractions-ledger.md` §3](../shared-abstractions-ledger.md#§3)).

---

## Alternatives rejected

- **Per-kind separate `Evaluate{Kind}` static class.** Duplicates the Evaluate*/advisory-list-builder helper-method patterns inside `ElectricPropulsionFeasibility`. Net cost > net benefit at 11 gates total.
- **Registry-driven gates per ADR-019.** The `GateRegistry` predicate signature is `Action<RegenGenerationResult, …>` — rocket-shaped. Per-pillar unification is a separate audit (referenced as ADR-027-or-later in [`shared-abstractions-ledger.md` §3](../shared-abstractions-ledger.md#§3)).
- **Abstract `PlasmaConditions` base class.** Premature abstraction — only HET as concrete consumer today (rule of one). Reopens at Wave-3.
- **Promote `IPlasmaState` to `Voxelforge.Core` immediately.** Single concrete consumer; would freeze a HET-shaped contract before MPD / ion physics are characterised. Rule-of-three watch in [`shared-abstractions-ledger.md` §5](../shared-abstractions-ledger.md#§5) tracks the trigger.

---

## Consequences

### Positive

- Resistojet path is bit-identical pre/post (covered by [`ElectricPropulsionFixture_MR501B`](../../../Voxelforge.ElectricPropulsion.Tests/Validation/ElectricPropulsionFixture_MR501B.cs) + 5 solver test files + `ScaffoldingSmokeTests`).
- HET landing requires only schema v1 → v2 identity migration, 6 new gates, `IPlasmaState` shared abstraction — no rewrite of Wave-1 code.
- Future plasma variants (arcjet / GriddedIon / MPD) extend the `Kind` switch and the kind-predicated gate blocks additively.

### Negative / deferred

- `IPlasmaState` may need to widen at Wave-3 if MPD/ion expose orthogonal state (e.g. accel-grid voltages, current-density tensors). The audit at rule-of-three is the right time to widen.
- The kind-predicated gate dispatch produces a per-kind ConstraintId emission ordering. Adding a `GateOrderingSnapshotTests` for EP later requires snapshotting per-kind — out of scope for this ADR.
- Gate-registry unification across pillars (rocket = registry, EP / airbreathing / marine / nuclear = parallel evaluator) remains deferred to a future ADR per [`shared-abstractions-ledger.md` §3](../shared-abstractions-ledger.md#§3).

---

## References

- [ADR-025 — `IEngine<TDesign,TConditions,TResult>` engine-family abstraction](ADR-025-iengine-engine-family-abstraction.md)
- [ADR-026 — multi-pillar coordination](ADR-026-multi-pillar-coordination.md)
- [`pillar-specs/electric-propulsion.md` §9 — Wave-2 plasma-chamber abstraction trigger](../pillar-specs/electric-propulsion.md)
- [`family-allocations.md` §1 — bit-mask registry (bit 8 reserved for `ElectricHallEffect`)](../family-allocations.md)
- [`shared-abstractions-ledger.md` §5 — rule-of-three watch](../shared-abstractions-ledger.md)
- Goebel, D. M., & Katz, I. (2008). *Fundamentals of Electric Propulsion: Ion and Hall Thrusters*. JPL Space Science and Technology Series. §§3, 6, 7.
- Aerojet Rocketdyne, "BPT-4000 Hall-Effect Thruster" datasheet (4.5 kW, xenon, ~270 mN, ~1800 s Isp).
