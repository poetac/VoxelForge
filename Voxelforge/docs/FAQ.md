# FAQ

## General

**Is voxelforge a production tool?**
No. It's a preliminary-design tool — it gets you into the feasibility
envelope quickly and emits a printable STL you can iterate on. Before
first fire, you need independent verification: 3-D CFD on the CFD field
export, FEA on the meshed STL, cold-flow on the injector. See
[`LIMITATIONS.md`](LIMITATIONS.md) for the full honest list.

**Who is this for?**
Engineers designing regeneratively-cooled rocket thrust chambers for
laser powder-bed fusion manufacture, in the 1 kN – 1 MN thrust range.
Small / medium aerospace teams, university propulsion groups, and solo
engineers building hardware outside a major prime.

**Why voxel-implicit geometry instead of mesh CAD?**
See [ADR-001](ADR/ADR-001-picogk-voxel-kernel.md). Short version:
mesh CAD struggles with the geometry LPBF actually unlocks — conformal
TPMS channels, monolithic chamber + turbopump + feed-manifold fusion,
aerospike plug cooling with internally-routed regen. In a voxel-
implicit kernel, a coolant channel isn't a swept surface — it's the
negative of a field clipped to a shell. Boolean ops are trivial;
topology changes are free.

---

## Running it

**What do I need?**
- Windows 10 or 11.
- .NET 9 SDK.
- 16 GB RAM minimum, 64 GB recommended for larger chambers — see [ADR-006](ADR/ADR-006-64gb-ram-constraint.md).
- PicoGK 2.2.0 (pulled automatically by the csproj; pinned, see [ADR-011](ADR/ADR-011-picogk-version-pinning.md)).

**Can I run this on macOS / Linux?**
Not the main app — it's `net9.0-windows` + WinForms. The test suite
and the `Benchmarks` console project will compile and run on Linux,
but voxel-rendering features require the Windows build.

**Can I use this headless / in CI?**
Partially. The `Benchmarks` console generates STLs without the UI
viewer. The `Tests` project runs pure-.NET tests that don't touch the
PicoGK `Library` (voxel-building tests live in `Benchmarks` by
[ADR-005](ADR/ADR-005-physics-tests-in-benchmarks.md)).

**How long does an SA run take?**
Depends on voxel size, thrust class, and whether you're building voxel
geometry every iteration or just scoring analytically. Analytical-only
SA on a small chamber finishes in tens of seconds; a voxel-building SA
on a 200 kN chamber at 0.4 mm session voxel is tens of minutes. The
`Benchmarks` project has CLI knobs for skipping voxel work during
exploration.

---

## What it can model

**Which propellant pairs are supported?**
LOX/CH4 (default), LOX/H2, LOX/RP-1. Each pair has its own CEA table
with C*, γ, Tc, Isp across an MR × Pc grid.

**Why not N2O4/MMH, H2O2/RP-1, or other storable / hypergolic pairs?**
The code path is ready — a new pair drops in as `Combustion/<Pair>Table.cs`
with the CEA data. Blocked on real CEA MR-vs-Pc sweeps of C* / γ / Tc;
a follow-on sprint once real data arrives. PRs welcome.

**Can this generate a hybrid engine?**
No. The `PropellantPair` abstraction assumes two propellants with
combustion-chamber-scale mixing. Hybrids need a solid-grain regression
model voxelforge doesn't have.

**Can it generate an aerospike plug nozzle?**
Yes. Set `ChannelTopology = Aerospike`. The builder dispatches to
`AerospikeBuilder.Build` instead of the bell-chamber pipeline, and four
aerospike-specific feasibility gates (plug wall T, coolant cavitation,
element clearance, injector face T) fire in parallel with the regen
gates. See [`GATES.md`](GATES.md).

**Can it generate TPMS coolant channels?**
Yes. Set `ChannelTopology` to `TpmsGyroid`, `TpmsSchwarzP`, or
`TpmsSchwarzD`. SA dims 18 (cell edge) and 19 (solid fraction) unlock.
Expect ±30 % on h and ±40 % on ΔP — TPMS correlations are newer than
the Dittus-Boelter family and have less data behind them.

**Can it fuse the chamber, turbopump, and feed manifold into one print?**
Yes. `MonolithicEngineBuilder.Build` fuses chamber + turbopump + feed
manifold (plus plug + annular throat for aerospike topologies) into a
single voxel body. Two monolithic-composition gates
(`MONOLITHIC_BODY_INTERSECTION`, `MONOLITHIC_TUBE_INTERSECTION`) enforce
non-overlap. Single STL out.

**Does it simulate combustion instability?**
Screen-level only. Crocco N-τ chug screen + acoustic-mode frequency
check. A `STABILITY_FAIL` is a red flag; a `Pass` is *not* a guarantee.
Real combustion-instability work needs a driving-response function
measurement.

---

## How it compares

**How does this compare to nTopology / Carbon / Creo?**
Commercial CAD-adjacent tools are general-purpose and closed. voxelforge
is purpose-built for regen thrust chambers + open source + coupled to a
physics pipeline. It's narrower but more opinionated; you trade breadth
for an optimiser that understands what "feasible" means for a rocket.

**Can I use voxelforge for other voxel-implicit geometry work?**
Voxel primitives (`UnionImplicit`, TPMS fields, cylinder + sphere SDFs)
live in [`Geometry/`](../Geometry/) and are reusable. The physics
stack and SA pipeline are rocket-engine-specific. If you're building a
different implicit-geometry tool, PicoGK directly is probably a better
starting point than forking voxelforge.

**Is this related to LEAP 71 / PicoGK?**
voxelforge depends on PicoGK 2.2.0 (LEAP 71's voxel-implicit kernel).
It's an independent project — LEAP 71 ships their own engineering-
capability layer above PicoGK; voxelforge is a separate take at
regen-chamber-scoped tooling. See `README.md` acknowledgements.

---

## Printing and operating hardware

**Can I actually print this on my LPBF machine?**
If your machine can hold a 30 µm layer thickness and you respect the
0.30 mm universal feature floor (enforced by `FEATURE_TOO_SMALL`), yes.
The voxel adequacy check + the LPBF feature-size gate screen out the
obvious failures. voxelforge does not simulate LPBF thermal history,
so residual-stress warping is on you — orient for overhangs, use
supports where the build-orientation advisor recommends them.

**Can I print this on a filament / SLA / polyjet printer?**
No. The gates assume metal LPBF (Inconel, CuCrZr, GRCop42). Wall
thicknesses that clear LPBF are too thin for polymer processes; the
geometry won't survive hot-fire regardless.

**Which metal alloys are supported?**
`WallMaterial` presets: GRCop42, CuCrZr, Inconel 625, Inconel 718.
Each carries `MaxServiceTemp_K`, yield vs temperature, conductivity,
and density. Other alloys drop in as a new `WallMaterial` record.

**How do I calibrate against real hardware test data?**
After a cold-flow or hot-fire run, set
`OperatingConditions.BartzScalingFactor` to the ratio of measured h_g
to predicted. The factor multiplies onto Bartz directly and is carried
through the full pipeline. 1.0 = literature Bartz (default).

---

## Contributing

**I want to add a new SA design variable.**
One attribute. See [ADR-012](ADR/ADR-012-adding-an-sa-design-variable.md).

**I want to add a new feasibility gate.**
Pattern-match on an existing gate in
[`FeasibilityGate.cs`](../../Voxelforge.Core/Optimization/FeasibilityGate.cs). Gate IDs
are stable strings; don't reuse. Add a row to [`GATES.md`](GATES.md)
and a paragraph to [`ADR-009`](ADR/ADR-009-feasibility-gates.md).

**I want to add a new propellant pair.**
Blocked on CEA data today. If you have real MR × Pc sweeps of C* / γ /
Tc, a new pair is `Combustion/<Pair>Table.cs` + `PropellantPairs.All`
registration + UI dropdown (picks up automatically via the enum).

**Where does the sprint history live?**
[`../CHANGELOG.md`](../../CHANGELOG.md) at the repo root. The full
per-sprint deliveries table also appears in `CLAUDE.md`.
