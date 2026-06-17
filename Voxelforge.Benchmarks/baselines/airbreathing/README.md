# Air-breathing pillar baselines

Schema-v1 JSONL baselines for the `--bench-sa-airbreathing` subcommand.
Format is identical to the rocket baselines (ADR-013); the `bench_name`
field will be `bench-sa-airbreathing`.

## Current presets

| File | Preset | Date |
|------|--------|------|
| bench-sa-airbreathing-mattingly-ramjet-2026-05-04.jsonl | mattingly-ramjet | 2026-05-04 |
| bench-sa-airbreathing-j85-turbojet-2026-05-04.jsonl | j85-turbojet | 2026-05-04 |

## Adding a new preset baseline

```bash
dotnet run --project Voxelforge.Benchmarks/Voxelforge.Benchmarks.csproj \
  -- --bench-sa-airbreathing --preset <name> --iterations 2000 --seed 42 \
  --out Voxelforge.Benchmarks/baselines/airbreathing/bench-sa-airbreathing-<name>-$(date +%Y-%m-%d).jsonl
```

Commit the new JSONL alongside the physics PR that introduces the preset.
The bench-regression workflow discovers baselines via lexicographic sort on
the filename (YYYY-MM-DD embedded); newest date wins automatically.
