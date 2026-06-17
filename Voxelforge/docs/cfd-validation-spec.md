# CFD validation spec — Team C verification track

**Status:** Active (Sprint C.0, 2026-05-06)
**Issue:** [#160](https://github.com/poetac/voxelforge/issues/160) (T2.3 — CFD validation closed loop)
**ADR:** [ADR-026](ADR/ADR-026-multi-pillar-coordination.md) (multi-pillar coordination; verification-track conventions)
**Scope:** `Voxelforge.Cfd.Core/` + `Voxelforge.Cfd.Tests/` — no `IEngine`, no SA dims, no schema bump.

---

## Purpose

Grounds Bartz wall-temperature predictions in real physics by running an SU2 axisymmetric
RANS solve over the chamber/nozzle contour and feeding the result into
`CalibrationPosterior.Calibrate()` (existing 5-knob MAP, PR #335 / OOB-1). The output
is a per-preset **calibration drift report** — how far the Bartz prediction drifts from
SU2 ground truth and what MAP value of `BartzScalingFactor` (knob #3) minimises the error.

This is a **verification track**, not an engine pillar. See ADR-026 §1.

---

## SU2 installation (Windows)

SU2 is **not bundled** with the repo. Install once per machine; Team C code fails fast
with a clear message when `SU2_CFD.exe` is absent.

### Step-by-step (tested with SU2 v8.5.0 "Harrier", Windows, MPI build)

1. **Install Microsoft MPI** (required for the MPI-enabled build):
   - Download `msmpisetup.exe` from https://aka.ms/msmpi
   - Run as Administrator. Default path: `C:\Program Files\Microsoft MPI\Bin\`
   - Verify: `mpiexec --version` (after opening a new terminal) should print `Microsoft MPI … version 10.x.x`
   - Minimum required: msmpi ≥ 10.1.2

2. **Extract SU2** (zip archive):
   ```
   Extract to: C:\SU2\
   Result:     C:\SU2\bin\SU2_CFD.exe
               C:\SU2\bin\SU2_SOL.exe
               C:\SU2\bin\SU2_DEF.exe
               C:\SU2\bin\SU2_GEO.exe
               C:\SU2\bin\SU2_DOT.exe
               C:\SU2\bin\SU2\         (Python scripts)
               C:\SU2\bin\FADO\        (adjoint framework, not needed for Team C)
   ```
   > Avoid spaces in the path — SU2 Python scripts fail on `C:\Program Files\SU2\`.

3. **Set environment variables** (user scope; run in PowerShell):
   ```powershell
   [Environment]::SetEnvironmentVariable("SU2_RUN", "C:\SU2\bin", "User")
   [Environment]::SetEnvironmentVariable("PYTHONPATH", "C:\SU2\bin", "User")
   $p = [Environment]::GetEnvironmentVariable("Path", "User")
   [Environment]::SetEnvironmentVariable("Path", "$p;C:\SU2\bin", "User")
   ```
   Open a **new** terminal after setting these.

4. **Verify**:
   ```powershell
   SU2_CFD.exe --help          # should print "SU2 v8.x.x"
   mpiexec -n 2 SU2_CFD.exe --help   # two identical lines
   ```

### Locating the binary from C# (`Su2Runner.cs`)

```csharp
string? dir = Environment.GetEnvironmentVariable("SU2_RUN");
string exe = Path.Combine(dir ?? string.Empty, "SU2_CFD.exe");
if (!File.Exists(exe))
    exe = "SU2_CFD.exe";   // fallback: rely on PATH
if (!IsOnPath(exe))
    return null;            // CI-safe: caller skips run, returns null
```

---

## SU2 configuration choices

### Turbulence model: SST (Menter 1994)

`KIND_TURB_MODEL= SST` is preferred over `KE` (standard k-ε) for converging-diverging
nozzle geometry because SST handles adverse pressure gradients more reliably
(Menter 1994 §4; Wilcox 2006 *Turbulence Modeling for CFD* §4.5). Switching to `KE`
is a one-line change in `Su2ConfigWriter`; flag if SST fails to converge.

### Axisymmetric 2D (rocket bell)

`AXISYMMETRIC= YES` with a 2D quad mesh. The gas-side domain is a structured (axialCells × radialCells)
mesh. Y-axis = radial (r), X-axis = axial (x). Symmetry axis is y = 0.

3D runs for aerospike: deferred to Sprint C.3.

### Wall boundary condition: adiabatic

`MARKER_HEATFLUX= (wall, 0.0)` — SU2 computes the wall heat flux q_wall; we
**do not** prescribe the wall temperature. Prescribing T_w (isothermal BC) would make
the validation circular (we'd be comparing q predicted by SU2 with q_Bartz at the same T_w).
The adiabatic-wall run gives an independent prediction.

### Mesh density bands

| Mode | axialCells | radialCells | Use case |
|---|---|---|---|
| Coarse | 50 | 20 | Smoke test, CI (fast) |
| Standard | 200 | 80 | Drift report validation |
| Fine | 400 | 160 | Publication / peer review |

y+ target: ≈ 1 (SST requires y+ < 1 at the wall). Grid spacing at the wall ≈ 5 μm
for a LOX/CH4 chamber at Pc ≈ 10 MPa.

### Boundary markers

| Marker | Location | SU2 BC |
|---|---|---|
| `wall` | Chamber + nozzle inner surface | `MARKER_HEATFLUX= (wall, 0.0)` |
| `inlet` | Injector face (x = x_injector) | `MARKER_INLET= (inlet, Pt, Tt)` |
| `outlet` | Nozzle exit (x = x_exit) | `MARKER_OUTLET= (outlet, Pb)` |
| `symmetry` | Axis r = 0 | `MARKER_SYM= (symmetry)` |

---

## Data flow

```
ChamberContour.Stations[]
        │
        ▼
Su2MeshWriter.Write()  ───→  chamber.su2  ─────────────────────────────────┐
                                                                            │
CfdFieldExport.Write()  ──→  chamber.vti  (optional IC warm-start)         │
                                                                            ▼
Su2ConfigWriter.Write() ──→  chamber.cfg  ──→  Su2Runner.Run()  ──→  history.csv
                                                                            │
                                                                            ▼
Su2HistoryParser.ParseWallHeatFlux()  ──→  Su2WallProfile
        │
        │  q_wall → T_w conversion:
        │  T_w(i) = T_aw(i) − q_wall(i) / h_bartz(i)
        ▼
MeasuredSummary { PeakWallT_K = T_w_peak }
        │
        ▼
CalibrationPosterior.Calibrate(measured, runner)
        │
        ▼
MultiKnobCalibrationResult { BartzScalingFactor.MapValue = posterior }
        │
        ▼
DriftReport (Markdown + JSONL)
```

---

## CalibrationPosterior wiring

`BartzScalingFactor` is already knob #3 in `CalibrationPosterior` (axis 2, `hasThermal`
guard, prior mean 1.0, σ 0.20, bounds [0.60, 1.40]). **No new knob is added.**

The `CfdCalibrationRunner` constructs the `MeasuredSummary`:
- `PeakWallT_K`: set to the SU2 adiabatic wall temperature (peak across all stations)
- All other channels (`TotalMassFlow_kgs`, `CoolantDT_K`, `CoolantDP_Pa`): set to `double.NaN`
  (CalibrationPosterior silently skips NaN channels)

This fires only the `hasThermal` axis, adjusting `BartzScalingFactor` to minimise the
relative error between SU2 and Bartz-predicted `PeakWallT_K`.

---

## CI policy

- `cfd-validation.yml` (Sprint C.4): nightly, top-5 Pareto designs, informational only.
- Test filter for non-SU2 CI: `dotnet test --filter "Category!=RequiresSU2"`
- SU2-gated tests use `[Fact(Skip = "Requires SU2 on PATH — set SU2_RUN env var")]`.
- A missing SU2 binary must never fail a build or non-SU2 test run.

---

## Deferred work

| Item | Sprint | Notes |
|---|---|---|
| 3D axisymmetric aerospike | C.3 | Coordinate with Team A on FlightConditions → SU2 BCs |
| Air-breathing CFD (ramjet combustor) | C.3 | Reacting-flow config; consult Team A |
| Electric propulsion CFD | deferred | Low-Reynolds + plasma physics beyond SU2 RANS scope |
| Marine incompressible RANS | C.3 | Coordinate with Team M on hull BCs |
| Nightly CI workflow | C.4 | `.github/workflows/cfd-validation.yml` |

---

## References

- Menter, F.R. (1994). "Two-equation eddy-viscosity turbulence models for engineering
  applications." *AIAA Journal* 32(8), 1598–1605. https://doi.org/10.2514/3.12149
- Wilcox, D.C. (2006). *Turbulence Modeling for CFD*, 3rd ed. DCW Industries. §4.5.
- SU2 mesh format spec: https://su2code.github.io/docs/Mesh-File/
- SU2 config options: https://su2code.github.io/docs/Configuration-File/
- Bartz, D.R. (1957). "A simple equation for rapid estimation of rocket nozzle convective
  heat transfer coefficients." *Jet Propulsion* 27(1), 49–51.
