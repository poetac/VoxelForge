# voxelforge example designs

This folder is the **scaffold** for a public gallery of reproducible
designs. The intended layout pairs each input JSON with the bytes it
produces — a render, an STL (or its SHA-256 hash), and the
feasibility-gate verdict — so you can clone the repo, run one command,
and get the same bytes. **Today it holds the input scaffold only**; the
receipts (and renders / STL) land as designs are generated on a PicoGK rig.

## Why reproducibility hashes

voxelforge's SA optimiser and voxel pipeline are deterministic given
the same input and the same PicoGK version (pinned to 2.2.0 per
[ADR-011](../Voxelforge/docs/ADR/ADR-011-picogk-version-pinning.md)).
A reader who runs the same input should get a byte-identical STL.
[`RECEIPTS.md`](RECEIPTS.md) will record the expected SHA-256 for each
committed example — designed to be the hardest-to-fake credibility
signal in the repo. (A reproducibility CI job that regenerates a fixture
STL per PR and fails on hash drift is on the roadmap, not yet wired up.)

## Layout

```
examples/
├── README.md          ← this file
├── RECEIPTS.md        ← expected SHA-256 per example (populated per design)
└── <class>/
    ├── input.json          ← full OperatingConditions + RegenChamberDesign
    ├── render-side.png     ← hero render for the gallery
    ├── render-cutaway.png  ← cross-section showing regen channels
    ├── notes.md            ← one-paragraph "what's interesting here"
    └── output.stl          ← (optional; hash in RECEIPTS.md is canonical)
```

## Running an example

From the repo root, on a Windows + .NET 9 host:

```bash
dotnet run --project Voxelforge.Benchmarks -- \
    --in examples/<class>/input.json \
    --out examples/<class>/output.stl
```

Then verify (PowerShell):

```powershell
Get-FileHash examples/<class>/output.stl -Algorithm SHA256
```

Compare the hash against the row in [`RECEIPTS.md`](RECEIPTS.md). A
match means you've reproduced the design bit-identically.

## Populating this gallery

This folder currently contains the scaffold only. Populating it
requires running voxelforge on a Windows machine with the PicoGK
voxel kernel (cannot be done from a headless CI agent or from an AI
session without access to the rig).

**Recommended first designs to commit** (ordered by
demonstrate-the-span priority):

1. **Small pressure-fed** — ~5 kN LOX/CH4 at 5 MPa Pc, axial channels. The bread-and-butter reference case. Fast to run. Stress-tests the feed-system stackup gate.
2. **Mid-thrust with TPMS cooling** — ~50 kN LOX/CH4 with Gyroid topology. Shows off the TPMS path.
3. **Staged-combustion FFSC** — ~200 kN LOX/CH4 with both fuel-rich and ox-rich preburners. Exercises `PREBURNER_WALL_TEMP`, `TURBINE_POWER_DEFICIT`, `SHAFT_WHIRL` in anger.
4. **Aerospike plug** — ~100 kN LOX/CH4 at `ChannelTopology = Aerospike` with plug truncation 0.30. Shows the aerospike-parallel gate family.
5. **Monolithic aerospike engine** — fused chamber + turbopump + feed manifold + plug, single STL. The flagship demonstration.

For each: commit the input JSON, two renders, a short `notes.md`, and
add the SHA-256 row to `RECEIPTS.md`. The output STL is optional to
commit (can be large) — the hash is the canonical record.

## Adding your own design

Contributions welcome. Open a PR with a new subfolder, following the
layout above. Keep `input.json` human-readable (pretty-printed). Keep
renders under 500 KB each. If you have CFD or hot-fire data, a
`validation.md` alongside the notes is the gold standard.
