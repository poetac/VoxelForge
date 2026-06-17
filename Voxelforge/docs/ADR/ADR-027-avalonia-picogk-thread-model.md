# ADR-027 — Avalonia + PicoGK Threading Spike

**Status:** ACCEPTED  
**Date:** 2026-05-06  
**Issue:** [#416](https://github.com/poetac/voxelforge/issues/416)  
**Prerequisite for:** [#289](https://github.com/poetac/voxelforge/issues/289) (WinForms → Avalonia UI rewrite)

---

## Context

The main application (`Voxelforge/`) uses a three-thread model today:

| Thread | Owner | Notes |
|---|---|---|
| Main (thread 1) | PicoGK GLFW event loop | `Library.Go()` captures the calling thread |
| Task thread | PicoGK worker | Physics / voxel callbacks |
| STA background | WinForms | `Application.Run()` on an STA background thread |

Issue [#289](https://github.com/poetac/voxelforge/issues/289) proposes migrating the WinForms UI to Avalonia 11.x. Before committing to that migration, we need to confirm that Avalonia's Dispatcher and renderer can coexist with GLFW in the same process on Windows without deadlock, render-thread conflict, or P/Invoke access violation.

**Research question:** Can Avalonia's `ClassicDesktopLifetime` run on a background (MTA) thread while PicoGK's `Library.Go()` owns the main thread on Windows?

---

## Decision

Run a throwaway spike: `Voxelforge.Spike.Avalonia/` (not in `voxelforge.sln`).

Architecture under test — mirrors the current WinForms pattern:

```
Thread A — main thread  : Library.Go(0.5f, taskCallback) → GLFW event loop
Thread B — PicoGK worker: taskCallback body → spawns Avalonia thread
Thread C — background   : AppBuilder.Configure<SpikeApp>()
                            .UsePlatformDetect()        // Win32 backend
                            .UseSkia()
                            .StartWithClassicDesktopLifetime(...)
```

Key differences from the WinForms path:

- Avalonia does **not** require STA. Thread C is explicitly `ApartmentState.MTA`.
- Avalonia's Win32 backend creates its own `HWND` and Win32 message loop, independent of GLFW's event loop. No shared OpenGL context by default.

---

## Spike results

**Verdict: PASS**

Observed output on 2026-05-06 (Windows 11 Pro 10.0.26200, .NET 9.0.15):

```
[Spike] PicoGK will own the main thread via Library.Go().
[Spike] Avalonia will start on a background MTA thread.
      1s    0.0+ Starting tasks.
[PicoGK task] Running on thread 5.
[PicoGK task] Avalonia thread started. Waiting 8 s for visual confirmation...
[Avalonia thread] Starting on thread 6.
[Avalonia window] Opened on thread 6.
[PicoGK task] Requesting PicoGK task exit (Library.bContinueTask → false).
```

Observations:

| # | Observation | Outcome |
|---|---|---|
| 1 | `Library.Go()` started and dispatched the task callback | PASS |
| 2 | PicoGK task ran on thread 5 (PicoGK-managed worker, not main) | PASS |
| 3 | Avalonia `AppBuilder` chain completed on thread 6 (MTA background) | PASS |
| 4 | Avalonia window opened and displayed without exception | PASS |
| 5 | Both event loops spun concurrently for 8 s with no deadlock | PASS |
| 6 | No `AvaloniaException` captured; no P/Invoke AV or Win32 fault | PASS |

**No failure modes observed.** GLFW and Avalonia's Win32 backend coexist cleanly because they are fully independent at the Win32 layer — each owns its own HWND, message pump, and GPU context. The Avalonia Dispatcher is thread-apartment-agnostic; STA is not required.

One expected behaviour noted: `Library.Go()` keeps the GLFW window open after the task callback returns, requiring a user action (close button) before `Library.Go()` returns. This matches the production WinForms behaviour and is not a spike failure — the spike's `Environment.Exit()` path handles forced teardown.

---

## Consequences

**ADR-027 ACCEPTED.** The Avalonia migration path is architecturally viable on Windows.

### Migration model (issues #289, #416)

The same three-thread model continues post-migration:

```
Thread A — main thread   : Library.Go() → GLFW event loop         (unchanged)
Thread B — PicoGK worker : physics / voxel callbacks               (unchanged)
Thread C — MTA background: Avalonia Dispatcher + Skia renderer      (replaces WinForms STA)
```

- No `[STAThread]` attribute required (Avalonia does not need it).
- Cross-thread marshalling from Thread B → Thread C uses `Dispatcher.UIThread.InvokeAsync(...)` in place of `Control.Invoke(...)`.
- WinForms `SharedState` pattern migrates to an `IViewerState` interface backed by `Dispatcher.UIThread` post-callbacks.
- No changes to the PicoGK or physics layers.

### Work remaining before #289 can start

1. Evaluate `Avalonia.ReactiveUI` vs plain MVVM vs code-behind — the spike uses code-behind; production should pick one pattern.
2. Decide whether to add `Avalonia.Themes.Fluent` or `Avalonia.Themes.Simple` (or a custom theme matching Noyron aesthetics).
3. Plan form-by-form migration of `RegenChamberForm` — the existing 2 827-LOC constructor body will split naturally into MVVM ViewModels per the partial-class seams already in place.

### Phase 2 trigger (added 2026-05-17 via #623)

The migration's Phase 2 (per-form ports, one form per non-pillar sprint)
begins when **Framing-B Phase 1 closes** — i.e. the runner restoration,
CFD Sutherland-S landing (#480), and GitHub Pages enable (#349) all
complete. This replaces the earlier "demand-gated" framing: the trigger
is now an internally-observable milestone, not a vague external pull.

Pacing: one form per non-pillar sprint after the trigger fires. Pillar
work stays the priority; Avalonia ports interleave on sprints that
don't ship pillar physics.

### What this ADR does NOT cover

- GPU render conflicts under heavy voxel load (the spike uses no voxels; Thread C's Skia compositor and PicoGK's OpenGL context are separate GPU contexts but share the adapter — stress-test during migration).
- Blended HWND (hosting a GLFW surface inside an Avalonia window or vice-versa). Not needed for #289 scope; two independent windows is the migration target.

---

## Spike reproduction steps

```bash
# From repo root
git switch spike/avalonia-picogk-threading
dotnet run --project Voxelforge.Spike.Avalonia/Voxelforge.Spike.Avalonia.csproj
# Two windows open: PicoGK 3D viewer + Avalonia window showing thread IDs.
# Process exits automatically after ~10 s; exit code 0 = PASS.
```

Requirements: Windows workstation with display, .NET 9 SDK, PicoGK 2.2.0 native DLLs on PATH (resolved via NuGet on `net9.0-windows` target).

The spike project is intentionally **not** in `voxelforge.sln` — it is a throwaway ADR artefact, not a shipping deliverable.
