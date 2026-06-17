# 5 kN LOX/CH4 pressure-fed reference case

> **Status:** scaffold only. Replace this file with the real design
> notes once the first run lands.

## What this example demonstrates

- The bread-and-butter pressure-fed reference case. No turbopump; no preburner; axial regen channels.
- Fast to run (seconds of SA + a few minutes of voxelization).
- Stress-tests the **`FEED_PRESSURE_INSUFFICIENT`** gate: the user-supplied tank ullage must clear the line + valve + filter + injector stackup to hit the target Pc.

## What to look for in the output

- Peak gas-side wall T (goal: comfortably below the CuCrZr service limit).
- Coolant outlet T (goal: well under the methane coking band).
- Feed-stack margin (goal: > 10 % headroom on the predicted Pc).
- Voxel adequacy on the narrowest feature (rib thickness in this baseline).

## How to reproduce

See the [gallery README](../README.md). Run the benchmarks CLI
against `input.json`, hash the output, confirm against
[`RECEIPTS.md`](../RECEIPTS.md).

## What this example does *not* demonstrate

- Turbopump + preburner + staged combustion → see `staged-combustion-ffsc` once that folder lands.
- TPMS coolant topology → see `midthrust-tpms-gyroid`.
- Aerospike plug nozzle → see `aerospike-100kn`.
