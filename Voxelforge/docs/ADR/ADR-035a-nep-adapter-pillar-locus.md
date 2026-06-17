# ADR-035a — NEP SI adapters live in pillar Core (D1 revision)

**Status:** Accepted (2026-05-16)
**Amends:** [ADR-035 D1](ADR-035-nep-cross-pillar-coupling-roadmap.md)
**Issue:** [#505](https://github.com/poetac/voxelforge/issues/505)

[ADR-035 D1](ADR-035-nep-cross-pillar-coupling-roadmap.md) located the
`NuclearBraytonComponent` + `ElectricPropulsionComponent` SI adapters in
`Voxelforge.Core/Integration/Components/`. The decision is correct in
spirit (preserve ADR-026 parallel-pillar discipline — neither pillar
should reference the other) but structurally impossible: every pillar
Core assembly already references `Voxelforge.Core` for shared base
types (`SystemComponent`, `ComponentNetwork`, `IObjective`), so placing
a `NuclearBraytonComponent` in `Voxelforge.Core/Integration/Components/`
would require `Voxelforge.Core` to reference `Voxelforge.Nuclear.Core`
— a circular dependency. The existing 22 PR #489 internal pillars
(Battery, ElectricMotor, …) sit inside `Voxelforge.Core` itself so
their adapters in the same assembly resolve cleanly; the four pillar-
Core assemblies that ship standalone don't have that escape valve.

**Revised D1:** the NEP cross-pillar adapters live in their respective
pillar Core assemblies:

- `Voxelforge.Nuclear.Core/Integration/NuclearBraytonComponent.cs`
  (references `Voxelforge.Core` + own pillar namespace).
- `Voxelforge.ElectricPropulsion.Core/Integration/ElectricPropulsionComponent.cs`
  (references `Voxelforge.Core` + own pillar namespace).

Cross-pillar wiring happens at the call site, where the orchestrator
already references both pillar assemblies:

```csharp
using Voxelforge.Nuclear.Integration;            // NuclearBraytonComponent
using Voxelforge.ElectricPropulsion.Integration; // ElectricPropulsionComponent

var net = new ComponentNetwork();
net.Add(new NuclearBraytonComponent("ntr", myNuclearDesign));
net.Add(new ElectricPropulsionComponent("ep", myEpDesign));
net.Connect("ntr", "BraytonElectricalOutput_W", "ep", "BusPower_W");
```

Neither pillar Core gains a reference to the other; ADR-026's parallel-
pillar discipline is preserved. The original ADR-035 decisions D2-D5
(three-step fidelity ladder, no new family bit, published fixture
targets, failure-mode catalogue) are unchanged.

**Test home.** The `Voxelforge.Tests` main test project does not
currently reference Nuclear or EP. A cross-pillar test that exercises
both adapters in a single `ComponentNetwork` needs to add those two
project references, or live in a new `Voxelforge.Nep.Tests`
assembly. Option chosen at the implementing-sprint level; this ADR
does not prescribe.
