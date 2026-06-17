# Voxelforge — Tech Demo Script

**Audience:** engineering / design-review viewers who want to see the tool
design a printable regen chamber end-to-end.
**Duration:** ~12 minutes end-to-end; 5 minutes if you skip the optimizer.
**Tested on:** voxelforge v1.0.0 (current `main`, post-Sprint-32). .NET 9, PicoGK 2.2.0, Windows x64. Live test count, gate census, and SA dimensionality are surfaced at runtime via **Help → About** (`AboutInfo.cs`); see [`CLAUDE.md`](../CLAUDE.md) and [`CHANGELOG.md`](../CHANGELOG.md) for per-sprint feature history. **Aerospike path not yet in the demo script** — for an aerospike walkthrough run `dotnet run --project Voxelforge.Benchmarks -c Release -- --aerospike --propellant LOX_CH4 --thrust 20000 --pc 7e6 --eps 15 --plug 0.30 --channels --out aerospike.stl` directly. **Preburner** surfaces automatically on any design with `EngineCycle ≠ PressureFed` — see `result.Preburner`. **Monolithic engine** (chamber + turbopump + preburner + feed manifold fused into one STL) ships via `--monolithic` on the Benchmarks CLI; not in the form-driven script below.
**Outputs produced by the script:** one PNG of the viewer, one text report,
one 0.15 mm print-ready STL, one `.rcd.json` design file.

---

## 0. Before the call (3 minutes prep)

```
cd Voxelforge.StlExporter
dotnet build                                  # rebuilds main + exporter
cd ../Voxelforge.Tests
dotnet test --nologo                          # expect "Passed: 1321, Failed: 0"
cd ../Voxelforge
dotnet run                                    # launches viewer + form
```

If anything in the test suite fails, stop and triage — do not demo against
broken physics.

Have two terminals and one File Explorer window open. Have a throwaway
folder like `C:\demo\` ready so you know where saved files land.

---

## 1. Opening shot — the default design (30 s)

When the form opens, the task thread builds the default
**500 N / 1000 psia / MR 3.3 LOX/CH₄ CuCrZr chamber** with film cooling
*disabled*. Let it settle, then narrate:

> "What you're looking at is a regeneratively-cooled thrust chamber with
> 80 axial cooling channels, a CuCrZr copper alloy inner wall, and a
> throat diameter of ~7.8 mm. The colour is just the viewer's warm-copper
> material. The physics has already run — about 120 axial stations of
> coupled gas-side / wall / coolant-side heat transfer, Bartz with a
> Mayer acceleration correction at the throat, plus structural von-Mises
> checks."

Show the right-hand panels:
- **Thermal** → peak wall T around 2700–2900 K (the high number is
  expected without film cooling — CuCrZr would melt).
- **Structure** → min safety factor below 1, red pill.
- **Stability** → three pills (Chug / Screech / Composite). Mention:
  "The chug band is 15–25 % ΔP_inj / Pc — we assume 20 % at this
  stage, which is why Chug reads green."
- **Voxel adequacy** → Pass at 0.4 mm session voxels.

---

## 2. Enable film cooling — "the single biggest physics lever" (90 s)

Tick **Enable film cooling**. Set **Fuel fraction as film** to `0.05` and
**Slot height** to `0.6` mm. Regenerate fires automatically.

Peak wall T should drop to ~900–1100 K. Narrate:

> "A 5 % fuel bleed as a wall film is enough to drop the peak wall
> temperature below the CuCrZr service limit of 800–1000 K. Without
> this, no real LOX/CH₄ chamber survives. Isp takes a 0.8 × 5 / (1+MR)
> = ~0.9 % hit in exchange, which you'd spend anyway on ablatives or
> radiation cooling."

Point out the updated **Stability → Screech** frequencies
(L1 / T1 / T2 in kHz) and that they track `c = √(γRT_c) / geometry`.

---

## 3. Hard-fail demo — "the tool refuses to lie" (45 s)

This is the hard-fail-on-unsupported-propellant payoff.

Change the **Propellant pair** dropdown to
**N₂O₄ / MMH (hypergolic)  (unavailable)**.

The **banner under the dropdown turns red**:
`GENERATION DISABLED — Propellant pair N₂O₄/MMH is declared but its CEA
table is not populated. Generation is blocked to avoid a silent fallback
to LOX/CH₄ physics. Pick an implemented pair (LOX/CH4, LOX/H2, LOX/RP-1).`

The **Generate** and **Start Optimization** buttons go grey.

Narrate:

> "Before today the tool silently swapped in LOX/CH₄ physics behind the
> scenes — you could optimise a hypergolic spacecraft thruster and get
> LOX-methane predictions without warning. That's exactly the kind of
> footgun that gets a tech demo dismissed. Now the UI blocks the run,
> names the constraint, and tells you what pairs are supported."

Flip back to **LOX / CH₄** — the buttons come alive, the banner returns
to its neutral note.

---

## 4. Switch to LOX/H₂ — "one declaration, new physics" (60 s)

Select **LOX / H₂ (hydrogen)**. MR auto-snaps from 3.3 to the LOX/H₂
default of 4.0 (since 3.3 is outside the 3.0–7.0 band). Regenerate fires.

> "Same CuCrZr wall, same 500 N thrust, different gas, different coolant.
> The solver auto-switched from MethaneFluid to HydrogenFluid — you can
> see it in the table range and the pseudocritical region flag. Isp
> jumps about 90 s because H₂ has 1/4 the molecular weight; peak wall
> T drops because H₂ has ~5× the specific heat at this pressure, so
> the jacket is a much better heat sink."

Flip back to LOX/CH₄ for the rest of the demo.

---

## 5. Run the optimizer (2 minutes of talking, 20 min of compute)

Set **Iterations** to 300 and **Seed** to 1. Pick the **Balanced** profile.
Tick **Warm-start from current design**. Click **Start Optimization**.

The label under the button updates every couple of seconds with
`iter N / 300  score=... best=...`. Narrate over the warm-up:

> "This is simulated annealing over 24 continuous variables — chamber
> contour + cooling-channel layout + injector pattern + topology
> (TPMS / Pintle / aerospike) + cycle-balance knobs (preburner MR,
> flange projection). All 24 are tagged with `[SaDesignVariable]` per
> ADR-010, so the registry is the single source of truth. At the top
> of every evaluation we run a 38-gate feasibility check — wall T,
> yield, LPBF features (overhang, trapped powder, drain path), coolant
> outlet T + ΔP, stability (chug + screech), injector face T + element
> density, feed-system Pc, ignition energy + modality, NPSH margin,
> turbine power deficit, instrumentation clash, plus the aerospike-
> parallel and monolithic-only gates. Any of those sets the score to
> +∞ so the annealer can't settle on an infeasible minimum. There's
> also a voxel-adequacy gate that escalates to a feasibility violation
> when a feature would be sub-2-voxels at the export resolution."

After the run finishes, click **Save Pareto CSV…** in the Optimization
group → `C:\demo\pareto.csv`. Open it in Excel or pandas:

> "Header line is `iteration,peak_wall_t_k,coolant_dp_pa,mass_g`. Every
> non-dominated point on the front, in the order SA encountered them.
> Lets the user run their own trade-study analysis without re-running
> the SA — pareto-plot in matplotlib, fit a Pareto frontier, query
> 'what's the lightest design under 1100 K wall T' with one pandas
> filter."

If the clock is tight, stop after ~50 iterations and click **Stop**.
Otherwise let it run while you go to step 6 and come back.

### Optional: hands-off batch mode

For a demo where you want to show the end-to-end "set preferences, walk
away, come back to files" story:

1. Scroll down to the **Batch optimization (automated run + save)**
   group.
2. Click **Browse…** and pick an empty folder.
3. Leave the four save checkboxes at their defaults (JSON, STL, report
   ON; Pareto CSV OFF unless the audience cares).
4. Click **Run Batch & Save**.

SA runs with whatever settings are in the Optimization group above
(iterations, seed, profile, warm-start, parallel). When it finishes,
the folder fills with four files sharing a timestamp prefix:

```
2026-04-22_14-30-05_design.rcd.json    full design + conditions
2026-04-22_14-30-05_chamber.stl        session-voxel printable mesh
2026-04-22_14-30-05_report.txt         predictions + Pareto footer
2026-04-22_14-30-05_summary.txt        run metadata
```

Narrate:

> "This is the hands-off mode. I don't have to sit at the workstation
> and watch the progress bar. I set my operating point, my scoring
> profile, the iteration budget, and where I want the files, and the
> tool writes the design, the STL, and the text report when SA
> finishes. I can queue up overnight runs for a half-dozen different
> thrust classes and come back in the morning to a folder of printable
> candidates."

### Optional: turn off live preview for heavy editing sessions

Tick **Live preview (regen on every edit)** OFF in the Actions group
when editing many fields in a row. Every NumericUpDown / ComboBox /
CheckBox change normally fires a full physics + voxel rebuild (~6 s
each at 0.4 mm voxels). With live preview OFF, edits stage silently
and you click **Generate** exactly once when ready.

---

## 6. Export print-ready STL at 0.15 mm (90 s)

While the optimizer runs, the "Export STL…" button still works because
it fires against `_lastResult`. In the **Mesh resolution (preview vs.
export)** group on the left, set **Export voxel (mm)** to `0.15`.
Click **Export STL…** in the "Export & save" group, save to
`C:\demo\chamber_0p15.stl`.

> "The preview voxel is fixed at 0.40 mm for the whole session —
> PicoGK's library is a process-global singleton, so you can't switch
> mid-run. But the export voxel is independent: anything smaller than
> the session voxel routes through a headless subprocess that
> re-voxelises the same generation code at the finer resolution.
> About 30 seconds at 0.15 mm for this size."

Open the .stl in Windows 3D Viewer or Meshmixer to prove it's a real
watertight mesh.

---

## 7. Export report and save design (30 s)

Click **Export Report…** → `C:\demo\chamber_report.txt`. Open it.

> "Text report, one screen per section — fidelity stamp at the top
> telling you this is **not qualified for flight**, then derived quantities,
> thermal, structure, stability, injector pattern, voxel adequacy,
> solver diagnostics. Human-readable, greppable, no hidden units."

Click **Save Design…** → `C:\demo\chamber.rcd.json`. Open and show the
structured JSON.

> "Round-trips cleanly. The StlExporter subprocess uses exactly this
> schema to rebuild the design at any voxel size."

---

## 8. Optional — opt in to a transient analysis (90 s)

For audiences who care about start dynamics or chilldown budgeting:

1. Expand the **Start transient (§30, opt-in)** group on the left.
2. Tick **Enable start-transient simulator**.
3. Leave the defaults (100 ms valve ramp, 50 ms igniter delay, 1 s sim).
4. Click **Generate**.

> "This is a lumped 0-D model: linear valve open ramp drives propellant
> through the dome, the dome fills, propellant starts injecting, the
> chamber pressure follows a first-order lag with τ_c = V_chamber /
> (c*·A_t). We track unburned propellant pooling pre-ignition and
> estimate a hard-start spike when the igniter fires. The chart at the
> bottom of the group overlays Pc(t) (blue), valve position (gray
> dashed), and dome fill fraction (orange) on a 0-1.1 normalised axis."

To demo a hard-start mitigation, push **Igniter delay (ms)** to 250
and click **Generate**. Watch the "Peak Pc overshoot" label go red
with `⚠ HARD START`. Then drop the **Fuel valve override (ms)** to
60 (lead the fuel side) and regenerate — the staged start typically
brings the overshoot back into range.

---

## 9. Close — what the tool is and isn't (45 s)

> "What you saw: preliminary-design-grade prediction, ±25 % vs CFD, ±50 %
> vs a first fire. Rank-the-candidates fidelity, not certify-flight
> fidelity. That's stamped on every report. The 1321 regression tests
> lock the CEA tables, the contour continuity, the film cooling decay,
> the Bartz boundary-layer corrections, the proof test, the Monte-Carlo
> tolerance sweep, the stability screening, the injector orifice sizing,
> the rocket feasibility gates, the voxel-adequacy gate, the hard-fail-on-
> unsupported-propellant path, the cycle solvers (GG, OpenExpander,
> ClosedExpander, ORSC, FFSC, TapOff, ElectricPump), the LPBF
> printability analysis (overhang, trapped powder, drain-path), and
> the per-pair ignition-requirements gates. If any headline physics
> number moves, a test fails loudly."

If asked about the roadmap, the canonical reference is `CLAUDE.md`
(sprint history + "Next sprints" plan) plus the ADRs in
`Voxelforge/docs/ADR/`.

What's left: physics deepening (full Crocco / Cheng response coupling,
Rayleigh-integral stability proxy, baffle / acoustic-cavity sizing
recommendations) and validation against a printed-and-fired article.
None of those are blocking the next demo.

---

## Troubleshooting during the demo

| Symptom | Fix |
|---------|-----|
| Form renders as a blank white page | Build flaked on GroupBox autosize; close and `dotnet run` again |
| Export STL says "Exporter not found" | You rebuilt the main project but not StlExporter — `cd ../Voxelforge.StlExporter && dotnet build` |
| Peak wall T is zero or ±huge | Axial-conduction pass regressed to explicit flux corrector; check `RegenCoolingSolver.cs` Pass 2 |
| Generate button stays grey after selecting LOX/CH₄ | UPGRADE 5 banner is stuck — toggle the dropdown to another entry and back |
| Optimizer reports `[INFEASIBLE] VOXEL_RESOLUTION` every iteration | Session voxel is too coarse for the feature sizes SA is exploring; restart with a smaller session voxel |
| Start-transient chart stays blank after Generate | Group is collapsed by default — expand "Start transient (§30, opt-in)" and tick "Enable start-transient simulator", then Generate |
| Engine-cycle "Total shaft" stays at "—" | Cycle dropdown is on **PressureFed** (default) — switch to GasGenerator / ElectricPump / OpenExpander to size the pumps |
| Save Pareto CSV says "no Pareto front to export" | SA hasn't run yet, or `_lastParetoSnapshot` is empty — start an optimization first |

If a new symptom surfaces that isn't in the table above, capture it
in `CLAUDE.md` (PicoGK pitfalls section) or the relevant ADR.
