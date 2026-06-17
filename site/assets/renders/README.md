# Render assets

PNG renders referenced by `examples.html`. Until they exist the `<img>` tags degrade gracefully to their `alt` text.

## Files

- `chamber-side.png` — side view of a generated thrust chamber STL
- `chamber-cutaway.png` — cutaway showing regen channels and inner wall
- `chamber-throat.png` — close-up at the throat station

## Generating PBR renders (canonical method)

Build `Voxelforge.Renderer` (requires Blender 4.x on PATH or at `$env:VOXELFORGE_BLENDER_PATH`), then run:

```bash
dotnet run --project Voxelforge.Benchmarks -c Release -- \
    --render-preset merlin --out site/assets/renders/chamber-side.png \
    --material copper --resolution high
```

Repeat with `--render-preset aerospike` for the aerospike view. The `--render-preset` subcommand calls `voxelforge-render` (built from `Voxelforge.Renderer/`) which invokes Blender headless with a Principled BSDF + HDRi pipeline.

## Fallback (PicoGK viewer)

If Blender is unavailable, export screenshots from the PicoGK viewer via `File → Save Screenshot` after generating a design.
