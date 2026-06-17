# ADR-035 — Nuclear-Electric Propulsion (NEP) cross-pillar coupling roadmap

**Status:** Accepted (2026-05-12)
**Sprint:** Post-PR #489 follow-on
**Related:** [ADR-026 Multi-pillar coordination](ADR-026-multi-pillar-coordination.md) ·
[ADR-034 EP Wave-3 roadmap](ADR-034-electric-propulsion-wave-3-roadmap.md) ·
PR #489 Sprint NU.W3 (bimodal NTR + Brayton)

## Context

After PR #489 + PR #497, the codebase carries:

- **Nuclear pillar** with NU.W1 (NERVA NTR Wave-1, PR #465) + NU.W2 fuel-pin + NU.W3 bimodal NTR with He Brayton cycle + NU.W4 fuel material variants + NU.W5 uranium enrichment tiers. The bimodal Brayton path produces *electrical* output as a byproduct of the thermal cycle — a natural NEP power source.
- **Electric Propulsion pillar** with 6 implemented kinds + 2 reserved slots (VASIMR design scaffold + future FEEP/HDLT). Every kind takes `BusPower_W_avail` as a top-level input and clips its SA design vector accordingly.

**The two pillars do not yet talk to each other.** A user designing a nuclear-electric vehicle today must:

1. Run the Nuclear pillar separately to size the bimodal NTR + Brayton cycle.
2. Read off `BraytonElectricalOutput_W`.
3. Pipe that number manually into `ResistojetConditions.BusPower_W_avail`.
4. Run the EP pillar.

PR #489's `ComponentNetwork` (`Voxelforge.Core/Integration/`) is the obvious binding site — it already wires power between components (e.g. `BatteryComponent.PackElectricalPower_W` → `MotorComponent.BusVoltage_V`). A `NuclearBraytonComponent` + `ElectricPropulsionComponent` pair would let the SI layer solve the coupled system without manual plumbing.

This ADR captures the roadmap for closing that gap.

## Decision

**D1. NEP coupling sits in the SI layer, not the pillars.** Neither the Nuclear nor the EP pillar gets a direct reference to the other. The coupling happens via SI adapters living in `Voxelforge.Core/Integration/Components/`:

- New `NuclearBraytonComponent : SystemComponent` wrapping `NtrCycleSolver` (bimodal path). Exposes:
  - Inputs: `H2MassFlow_kgs` (NTR thrust mode), `HeatExchangerEfficiency` (Brayton-side optional override).
  - Outputs: `Thrust_N` (NTR thrust), `IspVacuum_s`, `BraytonElectricalOutput_W`, `ReactorThermalPower_W`, `CoreOutletTemperature_K`, `WasteHeatToRadiator_W`.

- New `ElectricPropulsionComponent : SystemComponent` wrapping `ElectricPropulsionOptimization.GenerateWith`. Exposes:
  - Inputs: `BusPower_W` (from the bimodal Brayton output above), `PropellantMassFlow_kgs`.
  - Outputs: `Thrust_N`, `IspVacuum_s`, `ThrustEfficiency`, `WasteHeat_W`.

The wire is then a 1-line `network.Connect("ntr", "BraytonElectricalOutput_W", "ep", "BusPower_W")`.

**D2. Coupling fidelity ladder.** Three ladder steps, each shippable:

| Sprint | Scope | Fidelity |
|---|---|---|
| **NEP.W1** | Adapter scaffolds — both components live, the connection wires `BraytonElectricalOutput_W → BusPower_W` cleanly. Use existing solver outputs verbatim. | Algebraic-only steady-state. |
| **NEP.W2** | Cross-pillar mass balance — propellant accounting from both subsystems (NTR-mode H₂ + EP-mode Xe/Ar) into a unified `TotalPropellantMass_kg` rollup via SI.W26 cumulative aggregators. | Adds mass-budget realism. |
| **NEP.W3** | Variable-Isp dispatch — when paired with VASIMR (EP.W4 phase 2, issue #498), expose a `MissionTimelineDispatcher` that switches the bimodal Brayton + VASIMR between high-thrust (low Isp) and low-thrust (high Isp) regimes based on a mission-segment definition. | Adds mission-level realism. |

NEP.W3 is the most-cited NEP design study reason — the variable-Isp dispatch is what makes NEP attractive over chemical+chemical alternatives. It's also the most-complex; defer until VASIMR physics ships.

**D3. EngineFamilyMask coordination.** No new mask bit needed for NEP — the coupling is composition, not a new engine kind. The `NuclearBraytonComponent` reports `Nuclear` family; the `ElectricPropulsionComponent` reports whatever the inner EP kind reports. NEP-specific gates (e.g. NEP_REACTOR_POWER_BELOW_EP_DEMAND) would register against `Nuclear | ElectricPropulsion` (mask combination); no new bit.

**D4. Validation fixture targets.** Three real NEP designs in the public literature:

- **NASA NEP-class Mars cargo vehicle** (Borowski 2003): MW-class bimodal NTR + 4-5 MW HET array. Anchor: `BraytonElectricalOutput_W ≈ 1 MW`, supports ~50 N HET array at ~3000 s Isp.
- **JIMO (Jupiter Icy Moons Orbiter, cancelled)** Prometheus reactor + Ion array. Anchor: 100 kW class.
- **TEM (Transport Energy Module, Roscosmos)** megawatt-class NEP demo. Anchor: 1 MW.

NEP.W1 ships with the JIMO 100 kW anchor (smallest, easiest to validate); NEP.W2+ adds the others.

**D5. Failure modes the cross-pillar coupling exposes.**

- **Cycle-iterative solver requirement.** The current ComponentNetwork has `Solve()` (acyclic) and `SolveIterative()` (Gauss-Seidel, cycle support, SI.W3). NEP is acyclic in the simple case (Brayton → EP, no back-pressure feedback), so `Solve()` suffices. If a future fidelity step adds radiator-back-coupling (waste-heat rejection from EP affecting Brayton-cycle efficiency), the network has a cycle and needs `SolveIterative()`. Should be documented but not blocking.
- **Mass-flow mismatch.** NTR-mode + EP-mode use different propellants (H₂ + Xe/Ar respectively). The two `PropellantMassFlow_kgs` ports must NOT be unified — they're different propellants with different storage / thermal / handling requirements. The aggregate `SI.W26 CumulativeMass_kg` aggregator does the right thing by tracking each separately if they're named `H2MassFlow_kgs` + `XeMassFlow_kgs` (no shared port-name).
- **Power-budget violation.** EP designs that demand more power than the Brayton cycle delivers will silently under-perform unless a hard `NEP_POWER_BUDGET_EXCEEDED` gate is added. NEP.W1 must ship this gate.

## Consequences

**Positive:**
- NEP design studies become single-network workflows; no manual cross-pillar value piping.
- The variable-Isp VASIMR + NEP combination becomes the canonical demo (matches Chang Diaz's published mission concepts).
- Component-tagged balance reports (SI.W27) make NEP subsystem accounting straightforward — `PowerBalanceFor(hist, c => c.StartsWith("nep_"))` gives the full NEP-system balance in one call.

**Negative:**
- Two more SI component adapters to maintain (~30-50 LOC each + tests).
- Cross-pillar coupling introduces coupling-validation responsibilities that don't fit cleanly under either pillar's existing audit boundary. ADR-026's parallel-pillar discipline relaxes here — NEP is the first genuinely cross-pillar feature shipped via the SI layer.
- VASIMR + NEP combination depends on EP.W4 phase 2 (issue #498) shipping first. NEP.W3 should NOT block NEP.W1/W2.

## Alternatives considered

**A1. Direct cross-pillar dependency in `Voxelforge.ElectricPropulsion.Core` (referencing `Voxelforge.Nuclear.Core` for the bimodal output).** Rejected per ADR-026's parallel-pillar discipline. Cross-pillar dependencies create transitive build coupling that ripples through every consumer.

**A2. New "NEP" pillar at `Voxelforge.Nep.Core` that owns both NTR and EP.** Rejected — duplicates physics already shipped in the two pillars; introduces a third schema chain; ADR-026's parallel-pillar discipline says cross-pillar work is composition, not a new pillar.

**A3. Defer NEP entirely until a real mission study triggers it.** Rejected — the bimodal Brayton output is already there (NU.W3 shipped), and EP already takes `BusPower_W_avail`. The wire is the only missing piece; not landing it forces every external NEP-curious user to write the wire themselves.

## Implementation status

**Not yet started.** This ADR scopes the work; the actual sprints land separately.

Prerequisites currently met:
- ✅ Nuclear pillar bimodal NTR (NU.W3, PR #489)
- ✅ ElectricPropulsion pillar full Wave-2 portfolio (PR #489)
- ✅ SI ComponentNetwork (SI.W1-W20, PR #489)
- ✅ SI.W27 component-filtered balance (PR #497, this branch)
- ✅ ADR-026 multi-pillar coordination protocol

Prerequisites pending:
- ⏳ EP.W4 phase 2 VASIMR physics for NEP.W3 variable-Isp dispatch (issue [#498](https://github.com/poetac/voxelforge/issues/498))

## Sprint sequence

| Sprint | Effort | Status |
|---|---|---|
| **NEP.W1** — `NuclearBraytonComponent` + `ElectricPropulsionComponent` adapters + JIMO 100-kW fixture + `NEP_POWER_BUDGET_EXCEEDED` gate | ~2d | Not started |
| **NEP.W2** — Cross-pillar mass balance (H₂ + Xe/Ar separate accounting) + NASA NEP-class Mars cargo fixture | ~1-2d | Not started |
| **NEP.W3** — Variable-Isp dispatch (VASIMR-aware mission-segment switching) + TEM-class fixture | ~2-3d, depends on #498 | Not started |

## Follow-ons

- Once NEP.W3 ships, a complementary NTR-ramjet cross-pillar coupling (nuclear thermal + a marine hybrid ramjet) becomes the next obvious cross-pillar work.
- ADR-036 (TBD) — broader "cross-pillar coupling" framework if NEP-style coupling proves repeatable. Would generalize the `XxxComponent : SystemComponent` adapter pattern.
- Optimizer-side integration: future `CostObjective.PerOutputUnit` calls with the NEP combined-system as the inner objective (mission cost / thrust trade) — composes cleanly with the existing wrapper stack (ADR-032).
