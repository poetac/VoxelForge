# voxelforge — developer guide

Orientation for working in this repository: what it is, how it's laid out,
how to build it, and the hard-won pitfalls that aren't derivable from the
code alone.

## What this repo is

Computational engineering & additive-manufacturing tools built on
voxel-based implicit geometry (PicoGK 2.2.0, .NET 9, Windows Forms).

A multi-pillar physics-based propulsion + power + storage optimizer.
Active production pillars: **rocket** (LOX/CH4 / LOX/H2 / LOX/RP-1 regen
bell + aerospike + dual-bell + E-D + monoprop + hybrid); **air-breathing**
(ramjet, turbojet ± afterburner, turbofan, scramjet, RBCC, gas turbine,
Rankine steam, pulsejet, turboprop, turboshaft, LACE, RDE); **electric
propulsion** (resistojet, HET, arcjet, PPT, GIT, MPD ± applied-field, FEEP,
HDLT, VASIMR); **marine** (AUV displacement + planing + semi-displacement);
**nuclear** (NERVA-class NTR, bimodal NTR-Brayton); **CFD verification**
(SU2-based). 22+ Wave-1 internal pillars under `Voxelforge.Core/` cover
energy / structural / RF / thermal-management surfaces (battery, PV, wind,
hydro, electrolyser, fuel cell, flywheel, Stirling, TEG, heat-pipe,
heat-exchanger, radiator, refrigeration, pump, compressor, motor, tankage,
antenna, chemical reactor, aerostructures, H₂ storage, solar thermal, hybrid
rocket). System Integration (`Voxelforge.Core/Integration/`) and Economics
(`Voxelforge.Core/Economics/`) layers compose these. A single CLI fuses
multiple subsystems into one printable STL.

## Project structure

| Project | Role |
| --- | --- |
| `Voxelforge.Core/` | Headless rocket physics library + 22 Wave-1 internal pillars (energy/structural/thermal) + System Integration + Economics + Plasma. No PicoGK, no WinForms. Referenced by every other project. |
| `Voxelforge.Voxels/` | PicoGK voxel/SDF builders. References Core. |
| `Voxelforge/` | Main WinForms app — UI, voxel adapters, `--airbreathing` mode dispatch. |
| `Voxelforge.Tests/` | xUnit rocket + cross-cutting test suite. |
| `Voxelforge.Airbreathing.{Core,Tests,Voxels,StlExporter}/` | Air-breathing pillar (net9.0; Core + Tests are PicoGK-free). |
| `Voxelforge.ElectricPropulsion.{Core,Tests,Voxels,StlExporter}/` | EP pillar. |
| `Voxelforge.Marine.{Core,Tests,Voxels,StlExporter}/` | Marine pillar. |
| `Voxelforge.Nuclear.{Core,Tests,Voxels,StlExporter}/` | NTR pillar (net9.0). |
| `Voxelforge.Cfd.{Core,Tests}/` | CFD verification pillar (SU2 oracle). |
| `Voxelforge.Benchmarks/`, `.MicroBenchmarks/` | Console benchmarks + BenchmarkDotNet suite. |
| `Voxelforge.StlExporter/` | STL export subprocess. |
| `Voxelforge.Renderer/` | Optional Blender PBR / HDRi renderer. |
| `Voxelforge.Eval/` | `voxelforge-eval` subprocess oracle. |
| `Voxelforge.Analyzers/` | Roslyn analyzers — `[Deterministic]` (VFD001-016) + family-purity (VFA001-002) + `PragmaSuppressionAnalyzer`. |
| `Voxelforge.Generators/` | Incremental source generator emitting `[SaDesignVariable]` accessor tables. |
| `Voxelforge.Avalonia/`, `.Spike.Avalonia/`, `.Kiosk/` | Avalonia migration + kiosk surfaces. |

`voxelforge.sln` ties it together. `dotnet build voxelforge.sln` is the
canonical full build.

## Build & run

```bash
# From repo root
dotnet restore
dotnet build voxelforge.sln
dotnet test  Voxelforge.Tests/Voxelforge.Tests.csproj
dotnet run --project Voxelforge/Voxelforge.csproj
```

Pre-push verification under `TreatWarningsAsErrors`:

```bash
dotnet build voxelforge.sln -c Release -p:TreatWarningsAsErrors=true --verbosity minimal -nologo
```

## Build environment & CI signal (hard-won)

**SDK pin.** `global.json` pins **.NET 9.0.x** with `rollForward: latestFeature` — deliberately *not* rolling to a newer major (a 10.x Roslyn shifts analyzer defaults under `TreatWarningsAsErrors`; see #670). A machine carrying only a newer major — e.g. the cloud/web container currently ships **.NET 10** — then fails every `dotnet` call with `NETSDK… A compatible .NET SDK was not found`. To compile-check there without committing the relaxation:

- Set `rollForward` to `latestMajor` in `global.json`, build, then `git checkout -- global.json`. **Never commit the relaxation.**
- The `net9.0-windows` projects (`Voxelforge`, `*.Voxels`, `Voxelforge.Tests`, `Voxelforge.Benchmarks`, `Voxelforge.Avalonia`) need `-p:EnableWindowsTargeting=true` to compile on Linux.
- This is a **compile** check only — test *execution* needs the net9.0 runtime (absent on the .NET-10 container). Let the `core (linux)` CI job run the tests.

**Where a cross-platform test goes.** A PicoGK/WinForms-free **Core** test belongs in **`Voxelforge.Core.Tests`** (`net9.0`, friend of Core via `InternalsVisibleTo`, runs on the Linux CI leg). **`Voxelforge.Tests` is `net9.0-windows`** (PicoGK + WinForms) and only runs on the self-hosted Windows runner — a cross-platform test placed there silently never runs in Linux CI. The per-pillar `*.Tests` (Marine/Nuclear/EP/Airbreathing/Cfd) are net9.0 and mirror this.

**Reading CI.** The lone self-hosted Windows runner is a SPOF (#13/#51) and is often offline; its jobs (`rocket-tests`, `analyzers-and-typecheck`, `*-tests`, `bench-*`, `contract-checks`, `changelog-check`) then sit **queued** and **cancel after ~24 h** — infra, not a regression. The six **`*(linux)`** jobs (`core-linux-tests.yml`) are the reliable signal, and `core (linux)` is where Core tests actually execute. A PR is safe to merge on `mergeable_state: unstable` when the Linux leg is green and only self-hosted jobs are stuck (`changelog-check` is non-blocking; `bench-regression.yml` is a soft-gate). The sandbox git proxy also blocks deletion pushes (`git push --delete` → 403); rely on GitHub's auto-delete-head-branches or delete merged branches in the web UI.

## Hardware guidance

Voxel memory scales with the cube of resolution. For thrust > 10 kN or
outer-diameter > 100 mm, use ≥ 0.8 mm voxel for exploration. 16 GB RAM is
the practical floor; 64 GB+ is recommended for larger chambers.
`MegaScaleEnvelope.Recommend(thrust, budgetBytes)` rescales preset
allocations to any RAM budget (see ADR-006). Quiet/Balanced modes
hard-block oversize allocations up front.

## PicoGK pitfalls (hard-won, not derivable from code)

1. `Smoothen(d)` destroys features < 2d. Cap at 25 % of minimum feature thickness. At 0.6 mm wall (Schwarz P necks ~70 % of nominal), safe `d ≤ 0.15 mm`.
2. Never `BoolSubtract` through TPMS-filled regions — cut simple shells/cylinders first, then `BoolAdd` TPMS material after.
3. Cleanup passes can create fragments — keep cleanup `d` below the wall-safe smoothing value.
4. Voxel ops must run on the **task thread only**. UI marshals via `SharedState`; viewer event loop is on the main thread.
5. C# records auto-generate `with`-expression cloning. Custom `Clone()` on a record is a CS8859 compile error. Use `x with { }`.
6. `FlowDirection` clashes with `System.Windows.Forms.FlowDirection` under `<UseWindowsForms>true</UseWindowsForms>`. Use `CoolantFlowDirection`.
7. Categorical/non-numeric UI choices must be preserved across the optimizer's `Unpack(baseline)` via `baseline with { … }`, otherwise SA silently reverts them on every candidate.
8. **xUnit + PicoGK**: under PicoGK 2.0.0+, `using var lib = new Library(voxel_mm); using var libScope = LibraryScope.Set(lib);` lets xUnit construct + dispose Library repeatedly without issue. Subprocess pattern retained only for: CLI-surface tests, cross-process determinism, cross-platform test projects targeting net9.0 (not net9.0-windows).
9. **`[InlineData()]` on a `public` `[Theory]` method raises CS0051** even when `InternalsVisibleTo` is set. The signature check is independent of consumer visibility. Either pass the enum's `(int)` ordinal and cast inside the test body, or promote the enum to `public` (and add to `PublicAPI.Unshipped.txt`).

## Analyzer trip-wires

- **RS0016** (PublicApiAnalyzers): new public API requires an entry in `Voxelforge.Core/PublicAPI.Unshipped.txt` (`Type.Member = value -> ReturnType`).
- **CA1859** (perf): test variable typed against an interface fails under `TreatWarningsAsErrors=true`. Use `Assert.IsAssignableFrom<I>(...)`.
- **CA1310** (locale): `string.StartsWith("FOO_")` fails. Use `string.StartsWith("FOO_", System.StringComparison.Ordinal)`.
- **VFD013 / VFD014 / VFD015**: static-mutable-field read, FP-time-loop, unstable sort comparer — all flagged inside `[Deterministic]` or `IObjective` scope. Suppress with `[SuppressMessage("Voxelforge.Determinism", "VFD0XX")]` plus an inline comment ONLY if you've manually verified the case is safe (e.g., ties are impossible by construction for VFD015). See ADR-042.
- **VFD016**: `MathF.Clamp(...)` call → error. `System.MathF` has no `Clamp` method. Use `Math.Clamp(value, min, max)` (float overload available since .NET Core 2.0). Fires globally — not scoped to `[Deterministic]`.

## Dependency policy

Voxelforge has a **zero-new-native-dependency** rule (ADR-024 scope): no new
P/Invoke surfaces, no new C/C++ DLLs to ship, no new platform-specific
binaries beyond the existing PicoGK / Magick.NET / Blender / SU2 native
stack. Adding one requires a dedicated ADR documenting the rationale + the
cross-platform shipping plan.

New **managed NuGet packages** are allowed when the license is permissive
(MIT, Apache 2.0, BSD-2/3, MS-PL) and the addition rides an ADR
justification — same bar as adding a new algorithm or pillar. The
Dependabot-managed bumps (`.github/dependabot.yml`) handle routine version
updates without an ADR; new top-level package adds need the ADR.

## Attribution policy

This project does not carry AI-tool attribution in any artifact:

- Commits must NOT include `Co-Authored-By: ...` or any AI co-author footer. Commits stay attributed to the human author who wrote / authorised them.
- PR descriptions must NOT include a "Generated with" or any equivalent tool-attribution footer.
- Documentation, ADRs, comments, and issue bodies must NOT name an AI tool as an author or contributor. Reference an AI tool only in a true technical context (a feature that genuinely depends on an LLM call), never as attribution.
- Historical commits before 2026-05-06 (`a097a4a`) contain prior co-author lines; those are immutable history and are not rewritten. The policy applies forward only.

If a tool, hook, or template defaults to inserting one of these footers, override it.

## Where to find context

- **[`CONTRIBUTING.md`](CONTRIBUTING.md)** — branching, PR, test, and code-style conventions.
- **[`CHANGELOG.md`](CHANGELOG.md)** — sprint-indexed delivery log; SSOT for "what shipped".
- **[`ROADMAP.md`](ROADMAP.md)** — forward-looking public roadmap.
- **[`Voxelforge/docs/ADR/`](Voxelforge/docs/ADR/)** — architecture decision records. `ADR/README.md` is the index.
- **`Voxelforge/docs/PHYSICS.md`, `GATES.md`, `LIMITATIONS.md`, `DESIGN_VARIABLES.md`, `FAQ.md`** — public-facing reference docs.
- **[`Voxelforge/docs/physics-cascade-status.md`](Voxelforge/docs/physics-cascade-status.md)** — known physics-correctness gaps with pinned-failure tests + file:line pointers. Cross-reference BEFORE judging a CI red as a regression.
- **`git log --oneline --no-merges`** — per-commit history is the SSOT for any specific change.
