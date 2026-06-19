# Physics reference

This document lists the correlations voxelforge uses for heat transfer,
pressure drop, combustion stability, and feed-system sizing; their
canonical citations; and their validity + uncertainty bands. Source
files are linked inline so you can verify the implementation against
the reference.

voxelforge is a **preliminary-design tool**. The correlations here are
the right tools for sizing a chamber in the hundreds of hours, not for
certifying one for flight. Uncertainty bands below are the ones the
original authors published, not wishful numbers; treat any predicted
wall T or ΔP as having at least those bands on it.

---

## Gas-side heat transfer

### Bartz (baseline) <a id="bartz"></a>

**Citation:** Bartz, D.R. (1957). *A simple equation for rapid estimation of rocket nozzle convective heat transfer coefficients*. Jet Propulsion 27 (1), 49–51.

**Source:** [`HeatTransfer/BartzHeatFlux.cs`](../../Voxelforge.Core/HeatTransfer/BartzHeatFlux.cs).

$$
h_g = \frac{0.026}{D_t^{0.2}} \left(\frac{\mu^{0.2} C_p}{\Pr^{0.6}}\right)_0 \left(\frac{P_c}{C^*}\right)^{0.8} \left(\frac{D_t}{r_c}\right)^{0.1} \left(\frac{A_t}{A}\right)^{0.9} \sigma
$$

where

$$
\sigma = \left[ \tfrac{1}{2} \tfrac{T_{wg}}{T_c} \left(1 + \tfrac{\gamma-1}{2} M^2\right) + \tfrac{1}{2}\right]^{-0.68} \left[1 + \tfrac{\gamma-1}{2} M^2\right]^{-0.12}
$$

**Known bias of pure Bartz:**

- Over-predicts h_g at the throat by 10–30 %.
- Under-predicts h_g in the combustor barrel by 20–40 %.
- Net heat load typically accurate to ±25 % vs measured fires.

### Mayer-style boundary-layer acceleration correction

**Provenance:** the coefficient $C_\text{accel} = 80{,}000$ is hand-tuned so $f_\text{accel}$ cancels Bartz's own ~20 % throat over-prediction — a self-referential calibration. The relaminarisation *trend* is consistent with accelerating-flow studies such as NASA TN-D-3328 (Back, Massier & Gier, 1965), but the coefficient is **not** fitted to that report's tabulated data (no TN-D-3328 fixture ships in this repo).

At the throat, strong streamwise acceleration (favourable pressure
gradient) partially relaminarises the turbulent boundary layer,
suppressing heat transfer below the Bartz prediction. The acceleration
parameter

$$
K = \frac{\nu}{U^2} \frac{dU}{dx}
$$

is the standard threshold. For $K > \approx 3\times10^{-6}$ the BL
begins to relaminarise. voxelforge applies a Mayer-style Stanton-ratio
correction

$$
f_\text{accel} = \exp(-C_\text{accel} \cdot K), \quad C_\text{accel} = 80{,}000
$$

which yields $f_\text{accel} \approx 0.79$ at $K = 3\times10^{-6}$
(matching the typical Bartz over-prediction at the throat) and
$\approx 0.45$ at $K = 10^{-5}$ (strongly relaminarised). Passing zero
disables the correction.

### Combustor-barrel injector-mixing enhancement

In the barrel, injector-driven turbulence enhances heat transfer above
the pure-Bartz prediction. voxelforge applies a smooth amplification
that decays with axial distance from the injector:

$$
f_\text{mix} = 1 + A_\text{mix} \exp(-x / L_\text{mix})
$$

with $A_\text{mix} = 0.30$ and $L_\text{mix} = 2 \cdot D_\text{chamber}$.
Adds 30 % at $x = 0$ and decays to ≈10 % by the converging-section
entrance. Passing a large decay fraction (≥ 3) disables it.

The two corrections multiply onto the Bartz h_g independently; defaults
reduce exactly to classical Bartz so call sites pre-dating the
corrections are unaffected.

**Typical error band after corrections:** ±25 % on wall T, better near
the throat than for pure Bartz. Still a preliminary-design tool — for
flight-certification work, back this up with a 3-D conjugate-heat-
transfer CFD run.

---

## Coolant-side heat transfer

**Source:** [`HeatTransfer/CoolantCorrelations.cs`](../../Voxelforge.Core/HeatTransfer/CoolantCorrelations.cs).

Three correlations are available; the optimiser picks Sieder-Tate by
default.

### Dittus-Boelter (1930)

**Citation:** Dittus, F.W. & Boelter, L.M.K. (1930). *Heat transfer in automobile radiators of the tubular type*. University of California Publications in Engineering 2 (13), 443–461.

$$
\mathrm{Nu} = 0.023 \, \mathrm{Re}^{0.8} \, \mathrm{Pr}^{0.4}
$$

(heating; the 0.4 exponent assumes the fluid is being heated — which
is always true for a regen channel).

### Sieder-Tate property correction

Adds a viscosity-ratio factor to account for the temperature-driven
viscosity gradient across the coolant boundary layer:

$$
\mathrm{Nu} = 0.023 \, \mathrm{Re}^{0.8} \, \mathrm{Pr}^{0.4} \left(\frac{\mu_b}{\mu_w}\right)^{0.14}
$$

Bulk viscosity in the numerator, wall-temperature viscosity in the
denominator. Matters for cryogenic methane near the pseudocritical
transition where $\mu_b / \mu_w$ can swing 2× over the channel.

### Pizzarelli supercritical correction

For near-pseudocritical operation where Dittus-Boelter overshoots h_c:

$$
\mathrm{Nu} = 0.0185 \, \mathrm{Re}^{0.82} \, \mathrm{Pr}^{0.4} \left(\frac{\rho_w}{\rho_b}\right)^{0.1}
$$

**Validity:** $4{,}000 < \mathrm{Re} < 5\times10^6$, $0.5 < \mathrm{Pr} < 100$, $L/D > 10$.
**Error band:** ±20 % on h_c for well-behaved supercritical methane far from $T_\text{pc}$; ±40 % inside the pseudocritical band.

### Petukhov (1970) friction factor

**Citation:** Petukhov, B.S. (1970). *Heat transfer and friction in turbulent pipe flow with variable physical properties*. Advances in Heat Transfer 6, 503–564.

$$
f = (0.790 \ln \mathrm{Re} - 1.64)^{-2}
$$

(Darcy friction factor for smooth turbulent tube flow). Laminar
fallback for $\mathrm{Re} < 4000$: $f = 64 / \mathrm{Re}$.

Channel pressure drop:

$$
\frac{dP}{dx} = f \cdot \frac{\rho u^2}{2 D_h}
$$

**Error band on ΔP:** ±20 % for axial / helical channels. Wider for
TPMS (see below).

---

## TPMS coolant correlations

**Source:** [`HeatTransfer/TpmsCorrelations.cs`](../../Voxelforge.Core/HeatTransfer/TpmsCorrelations.cs).

TPMS (Triply Periodic Minimal Surface) lattices replace discrete
channels with a continuous porous medium. voxelforge supports Schwarz-P,
Schwarz-D, and gyroid. The Nu and friction correlations come from
LPBF-manufactured-TPMS test data; expect ±30 % on heat transfer and ±40 %
on pressure drop. The `TPMS_CELL_FEATURE_TOO_SMALL` gate enforces a
2.0 mm minimum strut thickness (stricter than the 0.30 mm universal
LPBF floor) because TPMS lattices suffer recoater-drag defects at
thinner struts.

---

## Combustion stability <a id="combustion-stability"></a>

**Source:** [`Combustion/Stability/`](../../Voxelforge.Core/Combustion/Stability/).

Three-mode screen: chug, buzz, screech.

- **Chug (low-frequency feed coupling):** Crocco N-τ feedback screen with $\Delta P_\text{inj} / P_c$ as the primary parameter. Huzel & Huang §8.3 gives the feasible band as `[0.15, 0.25]`; outside that the chug rating degrades to Marginal or Fail, which the feasibility gate picks up through `STABILITY_FAIL`.
- **Buzz (mid-frequency tangential mode):** longitudinal and tangential acoustic-mode frequency check against injector-pattern symmetry.
- **Screech (high-frequency transverse / tangential):** baffle / cavity analysis when a damping feature is present.

**Composite rating:** `Pass / Marginal / Fail`. `Fail` triggers
`STABILITY_FAIL`. The composite is conservative — `Marginal` scores are
penalised but not rejected.

---

## Injector sizing

**Source:** [`Injector/`](../../Voxelforge.Core/Injector/).

Element types: coax, impinging-doublet (unlike and like), pintle,
showerhead, swirl.

- **Per-element orifice sizing:** single-phase incompressible flow through a discharge coefficient Cd (default 0.75; Cd is now SA-tunable on dims 16 / 17 with [0.40, 0.95] band).
- **Atomisation:** Sauter mean diameter (SMD) correlations per element type.
- **Face-plate element density:** `ELEMENT_DENSITY_TOO_HIGH` gate at 0.7 elements/cm² (Huzel & Huang §8.2 rule-of-thumb for face cooling adequacy).

### Injector-face thermal

**Source:** [`HeatTransfer/InjectorFaceThermal.cs`](../../Voxelforge.Core/HeatTransfer/InjectorFaceThermal.cs) (regen) + [`HeatTransfer/AerospikeInjectorFaceThermal.cs`](../../Voxelforge.Core/HeatTransfer/AerospikeInjectorFaceThermal.cs) (aerospike).

Equilibrium model — Bartz-ish h_g on the combustion-gas side balanced
against bore-scale Dittus-Boelter h_back on the bore-cooling side,
solved for the face temperature T_face. When T_face > wall material
service limit, `INJECTOR_FACE_T_EXCEEDED` fires (regen) or
`AEROSPIKE_INJECTOR_FACE_TEMP` fires (aerospike).

---

## Start transient

**Citation:** hard-start threshold per Sutton *Rocket Propulsion Elements* 10th ed. §10.6.

**Source:** [`Combustion/StartTransientSim.cs`](../../Voxelforge.Core/Combustion/StartTransientSim.cs).

0-D lumped simulator, explicit Euler integration:

- Valves ramp linearly over `StartValveOpenTime_s` (ox / fuel independent if set non-zero).
- Propellant injected before `StartIgniterDelay_s` accumulates as pool mass.
- Pool mass folds into the hard-start spike estimate at ignition.
- Hard-start threshold: 50 % Pc overshoot (`DefaultHardStartFactor = 0.5`).

Opt-in via `OperatingConditions.IncludeStartTransient`. Fires
`HARD_START_RISK` above the threshold.

---

## Chilldown transient

**Source:** [`HeatTransfer/ChilldownTransient.cs`](../../Voxelforge.Core/HeatTransfer/ChilldownTransient.cs).

Lumped two-phase jacket model, integrated to steady state. Default
two-phase HTC of 5000 W·m⁻²·K⁻¹ sits in the Chen / Shah
transition-boiling envelope for LCH4 / LH2 against warm metal walls.

Opt-in via `IncludeChilldownTransient`. Fires `CHILLDOWN_BUDGET_EXCEEDED`
when integrated time > user budget. Skipped on non-cryogenic pairs
(RP-1) regardless of the flag.

---

## Feed-system pressure stackup

**Source:** [`FeedSystem/`](../../Voxelforge.Core/FeedSystem/).

Stackup sequence:

```
Tank ullage P
    │
    ▼  (line loss — Darcy-Weisbach)
    │
    ▼  (main valve — Cv equation)
    │
    ▼  (filter — preset catalogue with clean + dirty multiplier)
    │
    ▼  (umbilical — preset catalogue)
    │
    ▼  (injector ΔP — design-set fraction of Pc)
    │
    ▼
Predicted Pc  (compare against target)
```

- **Darcy-Weisbach line loss:** Petukhov friction factor (above) applied to straight-pipe length + diameter.
- **Main valve:** Cv equation (`ΔP [psi] = (W [gpm] / Cv)² × SG`).
- **Filter:** `FilterStandard` preset catalogue with `FilterContaminationFraction` ∈ [0, 1] linearly interpolating clean → end-of-life.
- **Umbilical:** `UmbilicalStandard` preset catalogue (disabled by default, `None`).
- **Injector:** `ΔP_inj / Pc` fraction from the injector pattern (dim 14).

Opt-in via `OperatingConditions.TankUllagePressure_Pa > 0`. Fires
`FEED_PRESSURE_INSUFFICIENT` when predicted Pc falls below target.

---

## Turbopump sizing

**Source:** [`FeedSystem/TurbopumpSizing.cs`](../../Voxelforge.Core/FeedSystem/TurbopumpSizing.cs).

Per-pump sizing with NPSH + rotordynamics + shaft-whirl checks.

- **Pump head:** $H = \Delta P / (\rho g)$, staged across `PumpStageCount` ∈ [1, 4].
- **NPSH:** NPSHA (available) vs NPSHR (required); `NPSH_INSUFFICIENT` fires when NPSHA < NPSHR.
- **Shaft critical speed:** first bending critical from beam theory; `SHAFT_WHIRL` fires when operating RPM lands within ±20 % of it.
- **Turbine power balance:** preburner enthalpy drop vs pump shaft power; `TURBINE_POWER_DEFICIT` fires when the supply < demand on any shaft.

Only runs when `EngineCycle != PressureFed`.

---

## Structural analysis

**Source:** [`Structure/`](../../Voxelforge.Core/Structure/).

Axisymmetric pressure-vessel + thermal-stress model:

- Hoop stress from internal Pc on the gas-side wall.
- Through-wall ΔT stress from the wall-T gradient.
- Safety factor = material yield / combined stress.

`YIELD_EXCEEDED` fires when minimum safety factor < 1.0. ±15 % on peak
stress — for flight work, back this up with an FEA sweep over the
manufacturing-tolerance envelope.

---

## Manufacturing

**Source:** [`Manufacturing/`](../../Voxelforge.Core/Manufacturing/).

- **LPBF minimum feature:** 0.30 mm universal floor (`FEATURE_TOO_SMALL`).
- **LPBF recoater-drag ceiling:** geometry-dependent; enforced through the voxel-adequacy 2/3-voxel rule.
- **Build-orientation advisor:** recommends orientation for overhang minimisation.
- **Residual-stress estimation:** lumped-thermal-gradient model, order-of-magnitude accuracy only.
- **Ablative recession:** opt-in via `AblativeMaterial`. Constant-q integral with safety factor 1.5 (Sutton §16.3 recession-correlation scatter).

---

## What's *not* in the physics stack

voxelforge's physics is preliminary-design scope. These belong to
downstream tools:

- 3-D conjugate-heat-transfer CFD. voxelforge exports CFD fields for
  ParaView / OpenFOAM ingestion; the flow itself is your tool.
- FEA stress on the printed part (residual stress, warping, buckling).
- Full-3D combustion chemistry (voxelforge uses tabulated CEA data).
- Multiphase / film-cooling distribution CFD.
- Acoustic mode analysis beyond the screen-level Crocco check.
- Plume / exhaust infrared / signature modelling.

See [`LIMITATIONS.md`](LIMITATIONS.md) for the full honest list.
