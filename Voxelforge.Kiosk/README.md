# Voxelforge Kiosk — operator quick-start

Trade-show kiosk app. Visitor iterates through unique rocket-engine designs on screen, locks in their favourite, and the kiosk drops a watertight STL plus a production-quality PNG render into a watch folder for the Bambu X1C FDM printer.

## Two-window UX

The kiosk runs two windows:

- **Voxelforge Kiosk** (WinForms) — fullscreen on the visitor-facing monitor. Holds the buttons + status text.
- **PicoGK preview** (GLFW) — opens automatically when the kiosk launches. Shows the live 3-D engine voxel mesh with a copper material. Visitor can drag with the mouse to orbit / zoom while iterating.

For best presentation, place both windows on the same monitor side-by-side, or put the PicoGK preview on a second monitor for a "press the button, watch the engine appear" effect.

## Visitor flow

1. Idle screen: "Press to generate a unique rocket-engine design."
2. Visitor presses **Try a Design** → kiosk picks the next preset in the rotation, builds a perturbed variant in ~2–5 s, displays it in the PicoGK preview window. The form now shows two buttons:
   - **🔄 Try Another** — fresh perturbation; preview updates in place
   - **✓ Save This One** — commit the current preview
3. Visitor iterates **🔄 Try Another** until they like a design, then **✓ Save This One**.
4. Kiosk:
   - Writes the STL to the watch folder (~0.1 s, synchronous)
   - Asynchronously fires `voxelforge-render` to produce a `.png` companion (~30 s, non-blocking — visitor doesn't wait)
   - Resets to idle for the next visitor

The status line tracks every transition: "Generating bell #0042…", "✓ Saved … (render in progress…)", "✓ Saved STL + render → voxelforge_kiosk_0042_bell.png".

## First-time setup

1. **Build the kiosk in Release config from the repo root:**
   ```bash
   dotnet build voxelforge.sln -c Release
   ```
   Debug builds work but PicoGK in Debug is 10–100× slower — never use Debug at the show.

2. **Locate the kiosk exe at:**
   ```
   Voxelforge.Kiosk\bin\Release\net9.0-windows\Voxelforge.Kiosk.exe
   ```

3. **(Required for production renders) install Blender 4.x.** The kiosk auto-discovers Blender at:
   - `C:\Tools\Blender\` (portable extract — recommended for kiosk machines)
   - `C:\Program Files\Blender Foundation\Blender X.Y\` (MSI installer)
   - `$env:VOXELFORGE_BLENDER_PATH` (override, full path to `blender.exe`)
   - Any `blender.exe` on `$PATH`

   **Recommended:** download a portable Blender 4.5 LTS or 4.2 LTS ZIP from blender.org, extract to `C:\Tools\Blender\`, leave the contents flat (`C:\Tools\Blender\blender.exe`).

   Without Blender the kiosk still runs — it just skips the PNG render and saves only the STL. The status line shows "(Blender absent — STL only)" so the operator knows.

4. **Default watch folder is `%LocalAppData%\Voxelforge.Kiosk\output\`.** To change it, edit `%LocalAppData%\Voxelforge.Kiosk\settings.json` (created on first run):
   ```json
   { "WatchFolder": "D:\\KioskOutput", "NextSequence": 1 }
   ```

5. **Configure your slicer (Bambu Studio recommended):**
   - Watch the same folder for `*.stl`
   - Auto-import + auto-arrange + auto-orient is fine; parts land on their flat (cut) face
   - Use `0.20 mm Standard @ X1C` for PLA. Print times: bell ~45 min, aerospike ~50 min, pintle ~30 min.

## Three presets

| Preset | Thrust | Pc | ε | L × OD | Print time (PLA, 0.2 mm) |
|---|---:|---:|---:|---:|---:|
| **bell** | 1.5 kN | 3 MPa | 6 | ~215 × 65 mm | ~45 min |
| **aerospike** | 1.5 kN | 4 MPa | 8 | ~195 × 60 mm | ~50 min |
| **pintle** | 1 kN | 4 MPa | 6 | ~150 × 45 mm | ~30 min |

All sized to fit Bambu X1C's 256 mm bed with comfortable margin (≤ 230 mm in any axis after perturbation).

Each visitor's print is perturbed deterministically from the sequence number — same seq, same engine. Across the show floor every part looks subtly different even within a preset.

## Operator shortcuts

All require **Ctrl+Shift** as the chord prefix (no single-key shortcuts so a visitor can't accidentally close the kiosk):

| Chord | Action |
|---|---|
| Ctrl+Shift+D | Toggle hidden debug panel (preset picker + last error trace) |
| Ctrl+Shift+R | Reset session counter to 0 (does not change next sequence number) |
| Ctrl+Shift+Q | Close kiosk |

When the debug panel is visible, the preset combo lets the operator force a specific preset (overrides the rotation). Set it back to `(rotate)` to resume.

**Esc, Alt+F4, etc. are deliberately ignored.** Visitors can't close the kiosk by accident.

## Watch folder layout

```
<watchFolder>\
    voxelforge_kiosk_0001_bell.stl
    voxelforge_kiosk_0001_bell.png         # if Blender installed
    voxelforge_kiosk_0002_aerospike.stl
    voxelforge_kiosk_0002_aerospike.png
    ...
    kiosk.log              # append-only diagnostic log
```

The kiosk **never deletes** files — operator manages the folder. STLs are 20–50 MB each; PNGs are 1–3 MB. Budget ~5 GB per 100 prints.

## Diagnostic log

`kiosk.log` in the watch folder records every preview, commit, render, and error:

```
2026-04-29 14:07:31  STARTUP: kiosk task thread ready, voxel=0.5mm, watchFolder OK.
2026-04-29 14:07:55  PREVIEW bell #0042 in 3.4s — L=212mm OD=63mm
2026-04-29 14:08:01  PREVIEW bell #0042 in 3.5s — L=215mm OD=65mm
2026-04-29 14:08:11  COMMIT #0042 bell OK in 0.1s — 743,540 tri, 37,177,084 B — render pending
2026-04-29 14:08:42  RENDER OK: D:\KioskOutput\voxelforge_kiosk_0042_bell.png
```

If the render fails (Blender misbehaving, etc.) you'll see `RENDER FAIL` lines with the captured stderr.

## Show-day pre-flight checklist

- [ ] Blender installed + auto-discovered (run `voxelforge-render --in some.stl --out test.png` to verify)
- [ ] Plug in printer, confirm Bambu Studio sees it
- [ ] Run `Voxelforge.Kiosk.exe` once, confirm both windows appear, press **Try a Design**, confirm preview appears in GLFW window
- [ ] Press **🔄 Try Another** a few times to confirm the preview updates with new variants
- [ ] Press **✓ Save This One**, confirm STL appears in watch folder, confirm status line shows "render in progress…" then "✓ Saved STL + render → …" within ~30 s
- [ ] Open the STL in Bambu Studio — confirm watertight (no errors), confirm half-section is visible (one flat face)
- [ ] Slice with X1C 0.2 mm Standard PLA profile — confirm estimate < 60 min, no orphan-island warnings
- [ ] Print the first STL physically; confirm bed adhesion, layer adhesion, internal channels visible after print
- [ ] Once happy, **delete the test STLs + PNGs** so the demo starts at sequence 1
- [ ] Restart the kiosk so the on-disk scan resets `NextSequence`

## Troubleshooting

**"Build failed for X" on every press**
- Check `kiosk.log` for the actual exception
- Most common cause: watch folder unwritable (drive removed, permissions). The startup pre-flight writes a `STARTUP:` line confirming watch folder is OK.

**Prints take a very long time (> 30 s build)**
- Running a Debug build. Switch to Release.

**Status shows "(Blender absent — STL only)" but you have Blender installed**
- Auto-discovery only checks `C:\Tools\Blender`, `C:\Program Files\Blender Foundation\…`, and `$PATH`. Set `$env:VOXELFORGE_BLENDER_PATH` to the full path of `blender.exe` and restart the kiosk.

**PicoGK preview window doesn't appear**
- Make sure no other PicoGK app is running on the same machine (single-instance constraint via the underlying GLFW context).

**Slicer reports "non-manifold" warnings**
- Should not happen — PicoGK's marching-cubes output is watertight by construction. If it does, run the regression test:
  ```bash
  dotnet test Voxelforge.Tests/Voxelforge.Tests.csproj -c Release --filter KioskPipelineSubprocessTests
  ```

**Memory creeps over a long show**
- The kiosk explicitly `GC.Collect()`s between iterations to release native PicoGK voxel handles. If you still see Working Set climbing past ~3 GB after 50+ prints, check `kiosk.log` for any `WARN`/`ERROR` lines.

## What's deliberately out of scope (v1)

- Direct LAN-mode push to the X1C (operator drops STLs from the watch folder)
- Visitor name embossed on the part
- Touch keyboard / multilingual UI
- TPMS-lattice topology (the half-section cutaway would fragment a TPMS lattice)
- Turntable MP4 renders (still PNG only)
