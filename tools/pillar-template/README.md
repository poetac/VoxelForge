# Pillar scaffolding protocol

Use this protocol when bootstrapping a new engine-family pillar from scratch.
It documents the find-and-replace approach used for the Marine pillar (Sprint M.0,
2026-05-05) and standardises it for future pillars.

## Prerequisites

Before writing any code:

1. **File the pillar spec** — copy `Voxelforge/docs/pillar-specs/_template.md`
   to `Voxelforge/docs/pillar-specs/{family-id}.md` and fill every section.

2. **Claim EngineFamilyMask bits** — add rows to `family-allocations.md` and
   uncomment / add the bits in `GateRegistry.cs` `EngineFamilyMask` in the
   same PR.

3. **Add the family constant** — add `public const string {Family} = "{family-id}";`
   to `Voxelforge.Core/Engines/EngineFamilies.cs`.

## Project structure

Each pillar needs exactly 4 projects:

| Project | TargetFramework | Role |
|---|---|---|
| `Voxelforge.{Family}.Core` | `net9.0` | Headless physics — design records, conditions, result, solvers, gates, IO, objectives |
| `Voxelforge.{Family}.Tests` | `net9.0` | xUnit test suite (no PicoGK dependency) |
| `Voxelforge.{Family}.Voxels` | `net9.0-windows` | PicoGK SDF/voxel builders |
| `Voxelforge.{Family}.StlExporter` | `net9.0-windows` | Headless exe for subprocess STL export |

ProjectReference chain:
```
Tests        → Core
Voxels       → Core
StlExporter  → Voxels → Core
```

## Find-and-replace steps

Starting from the Airbreathing pillar as template:

1. Copy the four `Voxelforge.Airbreathing.*` directories to
   `Voxelforge.{Family}.*`.

2. Find-and-replace in all `.cs` and `.csproj` files:
   - `Voxelforge.Airbreathing` → `Voxelforge.{Family}`
   - `Airbreathing` → `{Family}` (namespace, type prefixes)
   - `airbreathing` → `{family-id}` (family string literals)
   - `AirbreathingEngineDesign` → `{Family}Design` (type names)
   - `FlightConditions` → `{Family}Conditions` (type names)
   - `AirbreathingResult` → `{Family}Result` (type names)

3. Regenerate GUIDs for the four `.csproj` project references. GUIDs
   must be unique across the solution. Use any GUID generator;
   format: `{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}`.

4. Remove all physics/cycle-solver implementation files (keep only
   the interface and scaffold stubs). The new pillar's physics comes
   from its pillar spec, not from Airbreathing.

5. Add the four projects to `voxelforge.sln` under a `Pillars/{Family}/`
   solution folder. Copy the GlobalSection entries from an existing
   Airbreathing project block and replace GUIDs.

6. Run `dotnet build voxelforge.sln` — only `ScaffoldingSmokeTests` should
   exist; the build must be green before physics PRs open.

## Smoke test checklist

`ScaffoldingSmokeTests.cs` must assert:
- `EngineFamilies.{Family} == "{family-id}"`
- `new {Family}Engine().Family == "{family-id}"`
- `{Family}Design has Family == "{family-id}"`
- No cross-pillar namespace references (grep in test body or rely on VFA001)

## Stop conditions

Wave 1 of any pillar ships one variant only. Multi-variant expansion
(surface hulls for marine, turbofan for airbreathing) is Wave 2+.
Wait for Team P's VFA001 before merging Sprint M.1+ (physics PRs).
