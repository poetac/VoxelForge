# Family allocations — pillar bit-mask + schema-version registry

> Single source of truth for `EngineFamilyMask` bit assignments and per-pillar
> schema-version constants. Update this file in lock-step with the
> `EngineFamilyMask` enum in
> [Voxelforge.Core/Optimization/GateRegistry.cs](../../Voxelforge.Core/Optimization/GateRegistry.cs)
> and the schema-version constants in each pillar's `IO/{Family}SchemaVersion.cs`.

## §1 — Engine-family bit-mask table

The `EngineFamilyMask` `[Flags]` enum in `GateRegistry.cs` reserves 32 bits for
engine-family discriminators. Each pillar may own one or more bits: the
top-level pillar bit (e.g. `ElectricPropulsion = 1 << 3`) plus optional
per-variant bits (e.g. `ElectricResistojet = 1 << 7`) to discriminate
sub-classifications when a gate is variant-specific.

| Bit | Mask name              | Status                      | Schema track                                        | Notes                                                                                   |
|-----|------------------------|-----------------------------|-----------------------------------------------------|-----------------------------------------------------------------------------------------|
|  0  | `RocketRegen`          | Live                        | rocket schema v31                                   | Bell-chamber regen-cooled rocket. Default mask for `RocketGates.RegisterAll()`.        |
|  1  | `RocketAerospike`      | Live                        | (shares rocket schema)                              | Aerospike (axisymmetric or linear) rocket. Mask for `AerospikeFeasibility.Evaluate`.   |
|  2  | `Airbreathing`         | Live                        | airbreathing schema v7                              | Ramjet, turbojet (with afterburner reheat), turbofan, scramjet, RBCC, gas-turbine, steam-turbine, pulsejet.                |
|  3  | `ElectricPropulsion`   | **Live (Wave-1, [PR #407](https://github.com/poetac/voxelforge/pull/407))**  | electric schema v1                                  | Electrothermal (resistojet) today; HET / MPD / ion / arcjet deferred to Wave-2.        |
|  4  | `NuclearPropulsion`    | **Live (Wave-1, PR #465)**  | nuclear schema v1                                   | NERVA-class solid-core NTR (LH2 propellant). Wave-2+ extends to bimodal NTR and Project Pluto nuclear ramjet. |
|  5  | Reserved               | —                           | —                                                   | Reserved for **PowerGeneration** if it forks from Airbreathing (gas-turbine + steam-turbine + Stirling + ICE). |
|  6  | Reserved               | —                           | —                                                   | Reserved for **MarinePropulsion** (AUV ramjet, surface-drone water-jet, Al/H₂O hybrid). |
|  7  | `ElectricResistojet`   | **Live (Wave-1, [PR #407](https://github.com/poetac/voxelforge/pull/407))**  | (shares electric schema)                            | Per-variant bit. Resistojet-specific gates register here so future HET/MPD gates don't accidentally fire on resistojet results. |
|  8  | `ElectricHallEffect`   | **Live (Wave-2, Sprint EP.W2.HET)** | (shares electric schema v2)                 | Hall-Effect Thruster. Goebel & Katz §3 Busch discharge model; BPT-4000 validation fixture. Gates live in `ElectricPropulsionFeasibility` kind-predicated HallEffect block per ADR-029 D6. |
|  9  | Reserved               | —                           | —                                                   | Reserved for `ElectricGriddedIon` — Wave-2.                                            |
| 10  | `ElectricArcjet`       | **Live (Wave-2, Sprint EP.W2.AJ)** | (shares electric schema v3)                  | Arcjet (electrothermal-with-plasma) — Maecker-Kovitya constricted-arc thermal model. MR-509 ATOS validation fixture. Gates live in `ElectricPropulsionFeasibility` kind-predicated Arcjet block. ADR-029's 2nd `IPlasmaState` consumer; rule-of-three watch is now "1 more variant promotes the abstraction to `Voxelforge.Core/Plasma/`". |
| 11  | Reserved               | —                           | —                                                   | Reserved for `ElectricMpd` (MPD thruster) — Wave-2.                                    |
| 12  | `ElectricPpt`          | **Live (Wave-2, Sprint EP.W2.PPT)** | (shares electric schema v4)                  | Pulsed Plasma Thruster — Solbes-Vondra ablation-discharge fit on solid PTFE; Aerojet EO-1 EP-12 validation fixture (~860 µN·s impulse bit, ~860 s Isp). Gates live in `ElectricPropulsionFeasibility` kind-predicated PPT block. ADR-029's 3rd `IPlasmaState` consumer; rule-of-three met — abstraction promoted to `Voxelforge.Core/Plasma/` per ADR-029a. |
| 13  | `Marine`               | **Live (Wave-1)**           | marine schema v2                                    | Marine vehicles — AUV mid-body / displacement hulls. Marine gates use `MarineGates.Evaluate` parallel evaluator. |
| 14  | `MarineHull`           | **Live (Wave-1, Wave-2 M2)** | (shares marine schema)                              | Per-variant bit covering both `HullFamily.Myring` (Wave-1) and `HullFamily.CylindricalHemi` (Wave-2 M2). No new bit needed — gates are family-agnostic. |
| 15–30 | Reserved              | —                           | —                                                   | Available for future pillars / variant bits. Annotate here when claimed.                |
| 31  | Reserved               | —                           | —                                                   | Sign-bit slot. Avoid; reserved for `All = ~0` semantics.                               |

Convention: top-level pillar bits cluster at the low end (bits 0–6); per-variant
bits cluster from bit 7 upward. A gate that applies to every variant of a
pillar registers with the pillar bit (e.g. `EngineFamilyMask.ElectricPropulsion`).
A gate that fires only for a specific variant registers with the variant bit
ORed with the pillar bit. The applicability filter in the registry treats the
bits as a flag set — a gate fires when `(result.FamilyMask & gate.Applicability) != 0`.

## §2 — Reserved → Live flip protocol

Adding a new pillar or variant bit is a four-step PR:

1. **Reserve in this table** as a single row in §1 with `Status = Reserved` and
   a one-line note describing the intent.
2. **Author a pillar spec** at `Voxelforge/docs/pillar-specs/{family}.md`
   covering §1–§10 (overview, SA dims, conditions, result, physics model, gate
   list, voxel build, fixture, deferred-variants note, sprint plan). The pillar
   spec's gate list pre-commits the `EngineFamilyMask` applicability for every
   gate before any code lands.
3. **Add the constant in `EngineFamilyMask`** in
   [Voxelforge.Core/Optimization/GateRegistry.cs](../../Voxelforge.Core/Optimization/GateRegistry.cs)
   with an inline comment linking to the pillar spec. Flip this table's
   `Status` to `Live (PR #N)`.
4. **Author or extend a `Voxelforge.{Family}.Core/IO/{Family}SchemaVersion.cs`**
   constant module (`Current`, `Known[]`, `IsSupported`). Pillar JSON
   persistence reads this on every load — bump on any breaking field change,
   never on additive changes.

Removing a bit is intentionally not supported. If a pillar deprecates a
variant, mark its row `Deprecated (date)` and keep the bit reserved — the
gate-ordering snapshot tests rely on stable bit positions across the codebase
lifetime.

## §3 — Per-pillar schema-version constants

| Pillar                 | Schema constant location                                                                            | Current | History                                                                            |
|------------------------|-----------------------------------------------------------------------------------------------------|---------|------------------------------------------------------------------------------------|
| Rocket                 | (embedded in `RegenChamberDesign` JSON header — see ADR-022)                                        | v31     | Documented per-bump in CHANGELOG; chain in CLAUDE.md "SA vector dimensions" row.   |
| Airbreathing           | [Voxelforge.Airbreathing.Core/IO/AirbreathingSchemaVersion.cs](../../Voxelforge.Airbreathing.Core/IO/AirbreathingSchemaVersion.cs) | v5      | v1 (Sprint A1), v2 (PR #388), v3 (PR #394), v4 + v5 (PR #400).                     |
| ElectricPropulsion     | [Voxelforge.ElectricPropulsion.Core/IO/ElectricPropulsionSchemaVersion.cs](../../Voxelforge.ElectricPropulsion.Core/IO/ElectricPropulsionSchemaVersion.cs) | v4      | v1 (Wave-1 — initial schema covering resistojet design + conditions); v2 (Wave-2 EP.W2.HET — adds 8 init-only HET fields with NaN/None defaults; identity migration); v3 (Wave-2 EP.W2.AJ — adds 5 init-only arcjet fields with NaN/None defaults; identity migration); v4 (Wave-2 EP.W2.PPT — adds 6 init-only PPT fields with NaN defaults; identity migration). |
| Marine                 | [Voxelforge.Marine.Core/IO/MarineSchemaVersion.cs](../../Voxelforge.Marine.Core/IO/MarineSchemaVersion.cs) | v2      | v1 (Sprint M.0 Wave-1 — initial schema); v2 (Wave-2 M2 — adds `HullFamily` with identity migration defaulting to `Myring`). |
| Nuclear                | [Voxelforge.Nuclear.Core/IO/NuclearSchemaVersion.cs](../../Voxelforge.Nuclear.Core/IO/NuclearSchemaVersion.cs) | v1      | v1 (Wave-1 — initial schema covering NERVA-class NTR design + conditions). |

When a pillar bumps schema, update both this table and the pillar spec's
relevant section. Identity migrations (defaults preserve bit-identical reads)
should be noted but do not require a CHANGELOG entry beyond the version-bump
row in CLAUDE.md.

## §4 — Cross-references

- [`ADR-026-multi-pillar-coordination.md`](ADR/ADR-026-multi-pillar-coordination.md)
  — multi-pillar coordination protocol (Definition-of-Done checklist, cross-pillar
  import discipline, Wave-2 plasma-chamber pre-commit).
- [`shared-abstractions-ledger.md`](shared-abstractions-ledger.md) — central
  registry of `IEngine<,,>` / `IObjective` / feasibility-evaluator
  implementations across all pillars.
