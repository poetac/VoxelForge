# ADR-029a — IPlasmaState abstraction promoted (rule-of-three met)

**Status:** Accepted (2026-05-08)
**Amends:** [ADR-029 D1](ADR-029-plasma-chamber-abstraction.md)
**Sprint:** EP.W2.PPT

PPT is the third `IPlasmaState` consumer (after HET in [PR #473](https://github.com/poetac/voxelforge/pull/473)
and Arcjet in [PR #477](https://github.com/poetac/voxelforge/pull/477)). Per
[ADR-029 D1](ADR-029-plasma-chamber-abstraction.md) the rule-of-three watch
fires: `IPlasmaState` moves from
`Voxelforge.ElectricPropulsion.Core/Plasma/` to `Voxelforge.Core/Plasma/`,
unblocking cross-pillar consumers (e.g. nuclear-electric propulsion in a
hypothetical Wave-3) without an EP-pillar reference. The interface contract
is unchanged — three properties (`IonExitVelocity_ms`, `BeamCurrent_A`,
`PlumeDivergenceHalfAngle_rad`); only the namespace (`Voxelforge.Plasma`)
and assembly home (`Voxelforge.Core`) change. Concrete implementations
(`HetPlasmaState`, `ArcjetPlasmaState`, `PptPlasmaState`) remain in the EP
pillar.
