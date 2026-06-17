---
name: Print / hot-fire failure
about: A voxelforge-generated design failed during LPBF print, cold-flow, or hot-fire
title: "[failure] "
labels: "real-world"
---

> Report the failure even if you can't publish detailed data. "A
> voxelforge design failed at X, I can't say more" is still useful —
> it tells us the tool is being used on real hardware and where the
> limits are.

## What failed

<!-- Print flaw? Cold-flow ΔP discrepancy? Hot-fire anomaly? Structural
damage? Describe the failure mode as narrowly as you can. -->

## When in the process

- [ ] Print did not complete (thermal event, recoater crash, support failure)
- [ ] Print completed but with visible defects (porosity, warping, surface finish)
- [ ] Cold-flow — pressure or flow did not match voxelforge prediction
- [ ] Hot-fire — ignition failure
- [ ] Hot-fire — combustion instability
- [ ] Hot-fire — wall burn-through / coolant loss
- [ ] Hot-fire — structural failure
- [ ] Other:

## Design snapshot

<!-- If you can share the input JSON, attach it. If not, at least
share: thrust, Pc, propellant pair, channel topology, wall material,
coolant, and voxelforge commit SHA. -->

- Thrust:
- Pc:
- Propellant pair:
- Channel topology:
- Wall material:
- voxelforge commit SHA:
- Hardware / print shop:
- Who printed:

## What voxelforge predicted vs what you measured

<!-- Numbers are gold even if rough. Wall T? Coolant ΔP? Feed pressure?
Ignition time? -->

| Quantity | voxelforge predicted | Measured |
|---|---|---|
|  |  |  |

## Your hypothesis

<!-- What do you think went wrong? Uncertainty-band exceedance? Physics
voxelforge doesn't model? Manufacturing tolerance stack-up outside
voxelforge's tolerance analysis envelope? -->

## Anything you'd like voxelforge to do differently

<!-- New gate that would have caught this? Better uncertainty band?
New feasibility check? Different default? -->
