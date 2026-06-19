# Published-engine validation fixtures — conventions

This directory holds published-engine validation fixtures: a real-flying-hardware spec + a published ground-truth performance number + a voxelforge prediction band. Each fixture is a falsifier — if voxelforge's prediction drifts outside the documented tolerance, the test fails and the next contributor has to either fix the physics or document why the drift is acceptable.

This README pins the convention for **per-fixture tolerance rationale**, surfaced by issue [#638](https://github.com/poetac/voxelforge/issues/638). The convention is forward-looking: existing fixtures get rationale comments incrementally (one pillar per PR) until coverage is complete.

## Rationale convention

Every `EpsilonFraction` field on a fixture's `Tolerances` block carries an inline rationale comment explaining **why that width is appropriate for that fixture**. The rationale references:

- **Which physics is not modelled** that limits the prediction (e.g. shifting-equilibrium combustion, 2-D throat compressibility, real-geometry manufacturing tolerances).
- **The cluster-anchor source** the value came from (datasheet, NASA / NIST table, textbook section).
- **Cross-link to [ADR-036](../../Voxelforge/docs/ADR/ADR-036-validation-tolerance-ladder.md)** — the canonical tolerance ladder. Per-fixture bands must agree with the ladder's pillar × variant × quantity entry; mismatch is a bug.

### Example (canonical form)

```csharp
GroundTruth: new PublishedGroundTruth(
    VacuumIsp_s:             444.4,
    VacuumThrust_N:          73_400.0,
    TotalMassFlow_kgs:       16.85,
    ThroatRadiusEstimate_mm: 68.0,
    Tolerances: new EpsilonFraction(
        // Calibrated regen-bell variant per ADR-036 § Rocket pillar.
        // ±5% Isp tracks the frozen-flow approximation's overshoot
        // vs Pratt & Whitney's published vacuum Isp; shifting-equilibrium
        // (Sutton 9e §3.2) would tighten this further but is unmodelled.
        IspS_Frac:    0.05,
        // ±5% thrust = ±5% Isp at fixed mDot (we set Thrust_N as input).
        ThrustFrac:   0.05,
        // ±5% mDot is Isp-driven; same source.
        MdotFrac:     0.05,
        // ±14% geometry: r_t back-derived from documented exit diameter
        // / √ε. Wider band reflects the inverse-sqrt's leverage on the
        // 535 mm exit measurement and the absence of a directly-published
        // throat radius.
        GeometryFrac: 0.14)),
```

### Anti-patterns

- **No rationale.** "0.15" without a comment means a future contributor doesn't know whether they can tighten or whether the band already absorbs a known modelling gap. CI accepts a widened band silently — VFA / CA analyzers can't catch this.
- **Generic rationale.** "Wide because voxelforge is preliminary-design" is correct but too broad. Pin the SPECIFIC modelling gap (shifting-equilibrium combustion, finite-rate chemistry, regen-side heat-pickup uncertainty, etc.) so the next reader knows which physics layer to look at when tightening.
- **Mismatch with [ADR-036](../../Voxelforge/docs/ADR/ADR-036-validation-tolerance-ladder.md).** If the per-fixture band disagrees with the ladder's pillar × variant × quantity row, ADR-036 wins; fix the fixture.

## Coverage status (2026-05-17)

| Pillar | Fixtures | Rationale comments |
|---|---|---|
| Rocket | 24 regen-bell (RL10A, Merlin-1D ×2, F-1, J-2, J-2X, HM7B, NK-33, BE-4, Raptor 1/2, RD-180, RD-170, RD-191, Vinci, SSME, LE-5B, LE-7A, Vulcain 1/2, BE-3, RL10B-2, NK-15, RS-68A) + XRS-2200 aerospike | Complete (#745) |
| Air-breathing | 14 catalogue fixtures (Mattingly ramjet, J85, J47, J57, J79, Marquardt ramjet, R-25, F404 turbofan, NASA GTX RBCC ×3, LM2500 ×2, V-1 pulsejet) + 7 standalone (F404 two-spool, RB-545 LACE, V-1 pulsejet detail, AFRL RDE, J79 wet, T56-A-15, T700) | Complete (#745) |
| Electric propulsion | 17 (MR-501B resistojet, BPT-4000, SPT-100, HiVHAc, TAL HETs, MR-509 + MR-510 arcjets, NSTAR + NEXT-C + NEXIS + HiPEP GITs, EO-1 + LES-6 PPTs, NASA-Lewis SF-MPD, LiLFA + Princeton X9 + Stuttgart ZT-1 applied-field MPDs) | Complete (#745). 9 fixtures cross-link physics-cascade-status.md #545/#546. |
| Marine | 6 (Bluefin-21, REMUS-100/600/6000 displacement AUVs; CoastalCargo40m displacement-surface; PlaningYacht11m planing) | Complete (#745, #755): all 6 fixtures carry per-quantity rationale. ADR-036 § Marine Displacement-AUV row widened to ±40 % drag (2026-05-17 via #755) to reflect documented Hoerner cluster scatter at Re_L < 10⁷. |
| Nuclear | 3 (NERVA NRX-A6, NERVA NRX-A6 fuel-pin sub-model, SP-100 bimodal NTR) | Complete (#745) |

When a pillar's coverage completes, drop its row from "Pending" + add a line in `CHANGELOG.md` referencing the PR.

## When to override the default

`PublishedEngineFixtures.DefaultTolerances` (declared at the top of `PublishedEngineFixtures.cs`) is the fallback for fixtures that don't need per-fixture tuning. Override per-fixture **only when** the cluster-anchor evidence justifies tighter or looser bands than the default. Document the rationale inline.

## See also

- [ADR-036](../../Voxelforge/docs/ADR/ADR-036-validation-tolerance-ladder.md) — canonical tolerance ladder (pillar × variant × quantity)
- [#638](https://github.com/poetac/voxelforge/issues/638) — original "add per-fixture justification" issue
- [#630](https://github.com/poetac/voxelforge/issues/630) — future conformance test that parses ADR-036 + cross-checks the per-fixture bands
- [`physics-cascade-status.md`](../../Voxelforge/docs/physics-cascade-status.md) — known physics gaps; explains why some bands are wide
