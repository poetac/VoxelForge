# Limitations

voxelforge is a **preliminary-design tool** — good for walking the
design space, narrowing the feasibility envelope, and emitting a
printable STL you can iterate on. It is not a flight-certification
tool, not a full-3D CFD solver, and not a stress-analysis package.
This document is the honest list of what it doesn't do, so you don't
find out the hard way.

If your question is "is this good enough to print and hot-fire?", the
answer is "probably, if you respect the bands below and do independent
verification on the things voxelforge doesn't model." If your question
is "is this good enough to fly?", the answer is no — no preliminary-
design tool is — and this document exists to be specific about why.

## Physics fidelity

- **Gas-side wall T: ±25–50 %** (Bartz-class correlation; see [`PHYSICS.md`](PHYSICS.md#bartz)). Pass the `WALL_TEMP` gate and you're inside the envelope where Bartz is usually right within 25 %. Get close to the gate limit and you're one correlation error away from a burn-through.
- **Coolant ΔP: ±20 %** for smooth turbulent channels; ±40 % for TPMS and ±40 % in the pseudocritical band.
- **Coolant outlet T: ±15 %** — driven by the h_g × h_c uncertainty product.
- **Structural safety factor: ±15 %** on peak stress. Axisymmetric model; does not capture stress concentrations at bolt holes, sensor bosses, flange transitions, or any point where the geometry isn't rotationally symmetric.
- **Combustion stability: screen only.** Crocco N-τ chug screen + acoustic-mode frequency check. A `STABILITY_FAIL` rating is a red flag; a `Pass` rating is not a guarantee — dynamic combustion instability needs a proper Rayleigh-criterion analysis with a real injector response function.

## What voxelforge does not model

**3-D flow.** All flow solvers (Bartz, Dittus-Boelter, stability, feed
stackup) operate on axisymmetric or 1-D station-by-station assumptions.
Azimuthal variation (injector pattern hot spots, film-cooling streak
patterns, transverse mode distortion) is invisible to the current
pipeline. voxelforge exports CFD fields for [ParaView](https://www.paraview.org/)
/ [OpenFOAM](https://openfoam.org/) ingestion so you can run the 3-D
solver; the 3-D run itself is your tool.

**Conjugate heat transfer.** Wall temperature is a steady-state
equilibrium between gas-side h_g and coolant-side h_c. Transient
response to throttling, startup overshoot propagation through the wall,
and circumferential conduction between hot and cold sectors of an
asymmetrically-loaded chamber are not captured.

**Printed-part stress.** The structural analysis is analytical:
pressure vessel + through-wall thermal gradient. Residual stress from
LPBF thermal history, post-print warping, support-removal distortion,
porosity effects, and anisotropic material response are not modelled.
Run an FEA sweep over the manufacturing-tolerance envelope before first
fire.

**Fatigue / cycle life.** Stress analysis is static-limit-state only.
Low-cycle fatigue on thermally-cycled regen channels — a real failure
mode on reusable engines — is not in scope. If you're planning > 5
cycles on the same hardware, add a fatigue analysis.

**Full combustion chemistry.** voxelforge uses tabulated CEA
(Chemical Equilibrium with Applications) data for C*, γ, Tc, Isp.
Finite-rate kinetics, non-equilibrium species freeze-out, and
boundary-layer chemistry effects are ignored.

**Acoustic mode analysis beyond screening.** Longitudinal + tangential
mode frequencies are computed but the screech / buzz screen flags *risk*
based on proximity, not actual damping. Combustion-instability work
needs a proper driving-response function measurement.

**Transient startup propellant distribution.** The start-transient
simulator is a 0-D lumped integrator. Dome fill transients, ox-fuel
mixing in the pre-ignition pool, hypergolic ignition dynamics, and
main-stage MR drift during start are modelled only in aggregate.

**Plume / exhaust signature.** Not modelled. Base heating, radiation,
infrared signature, altitude plume expansion — all out of scope.

**Ablative wall physics.** The ablative analysis is a constant-q
recession integral with a 1.5 safety factor. It does not model char
layer mechanical integrity, spallation, or material-specific anisotropic
recession.

## What voxelforge doesn't support yet (but could)

- **Propellant pairs beyond LOX/CH4, LOX/H2, LOX/RP-1.** Adding N2O4/MMH, H2O2/RP-1, N2O4/N2H4 is a data-entry job (CEA MR-vs-Pc sweeps of C* / γ / Tc). Blocked on real CEA table data, not on code capacity.
- **Hybrid propellant engines.** The propellant pair abstraction assumes two liquid / cryogenic propellants.
- **Detonation / rotating-detonation engines (RDE).** Steady-state chamber assumption is incompatible with RDE physics.
- **Pintle injector SA refinements.** Pintle element type is present; SA dims 26-27 cover diameter + sleeve hole count (Track H2 2026-04-27). Pintle-specific gap width + primary/secondary annulus ratios are not yet SA-promoted.
- **Multi-stage turbine refinement.** Pump side handles N ∈ [1, 4] stages; turbine is still single-stage impulse. Extend if a high-thrust (> 1 MN) design drives the need.
- **Preburner axial march.** `PreburnerCooling` is a lumped-parameter steady-state solver. A station-by-station analogue of the main chamber's `RegenCoolingSolver` is queued behind a real design hitting the `PREBURNER_WALL_TEMP` limit.
- **Validated free-piston Stirling output.** The Wave-1 Stirling pillar is modeled, but its cluster fit over-predicts free-piston power by 10–100×, so no defensible validation fixture lands until the mean-effective-pressure model is refined. Treat Stirling numbers as order-of-magnitude only.

## Operational constraints

- **Windows only.** `.NET 9` + `net9.0-windows` target; the UI is WinForms. The test + benchmark projects run on any .NET 9 host but the main app requires Windows 10 / 11.
- **64 GB RAM reference budget.** Chambers larger than ~100 mm OD or thrusts beyond ~10 kN need ≥ 0.8 mm voxel for exploration at 64 GB (see [ADR-006](ADR/ADR-006-64gb-ram-constraint.md)). Quiet / Balanced modes hard-block oversize allocations before voxelization starts.
- **Single-machine.** Multi-chain SA runs N parallel chains on the local CPU (auto-scales to `ProcessorCount − 2`, clamped 1-16); cross-machine distributed SA is not on the roadmap.
- **PicoGK 2.2.0 version-pinned** (see [ADR-011](ADR/ADR-011-picogk-version-pinning.md)). Minor-version drift has broken voxel ops before, and the pin is enforced in the csproj.

## What the feasibility gates *do not* guarantee

Passing every gate means "this design is not obviously broken in the
ways we know how to check." It does not mean:

- the design will fly,
- the design will even ignite reliably,
- the hardware will survive manufacturing tolerances,
- the hardware will survive more than one burn,
- the hardware will survive environmental loads (vibration, thermal cycling, transportation).

See [`GATES.md`](GATES.md) for the full project-wide gate catalogue — all 196 feasibility constraints across the five pillars (rocket · air-breathing · electric · marine · nuclear) — and exactly what each one checks.

## What to do with a voxelforge output before first fire

1. Run 3-D conjugate-heat-transfer CFD on the generated CFD field. Confirm the predicted wall T within ±25 %.
2. Run FEA on the meshed STL, sweeping over the LPBF manufacturing-tolerance envelope. Confirm no stress concentrations above the yield point.
3. Cold-flow the injector (water or GN₂) and confirm the pressure-drop curve matches the predicted ΔP within ±10 %. Calibrate the Bartz scaling factor (`OperatingConditions.BartzScalingFactor`) against the measured data.
4. Hot-fire at low Pc and short duration first. Instrument. Iterate.

Every engineering tool has a humility line. voxelforge's line runs
exactly here.
