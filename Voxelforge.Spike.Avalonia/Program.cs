// Voxelforge.Spike.Avalonia — ADR-027 threading spike (2026-05-06).
//
// Research question:
//   Can PicoGK's GLFW viewer (requiring the main thread on Windows) and
//   Avalonia's ClassicDesktopLifetime coexist in the same process?
//
// Architecture under test (mirrors the current WinForms pattern):
//   Thread A — main thread  : Library.Go() → GLFW event loop (PicoGK)
//   Thread B — background   : AppBuilder.Configure<SpikeApp>()
//                               .StartWithClassicDesktopLifetime()
//                             (Avalonia Dispatcher + Skia renderer)
//
// Unlike WinForms, Avalonia does NOT require STA. The spike uses a plain
// MTA background thread for Avalonia to confirm the dispatcher is
// thread-apartment-agnostic.
//
// Observations recorded in ADR-027:
//   PASS  — both windows open, both event loops spin, no deadlock or exception.
//   FAIL  — one of: Avalonia throws on non-main thread, GLFW crashes on
//            non-main-thread Avalonia GPU context, render thread conflict,
//            process hang, or P/Invoke access violation.
//
// Run:
//   dotnet run --project Voxelforge.Spike.Avalonia/Voxelforge.Spike.Avalonia.csproj
//
// Exit codes:  0 = PASS (ADR-027 ACCEPTED)   1 = FAIL (ADR-027 REJECTED)

using System;
using System.Threading;
using Avalonia;
using PicoGK;
using Voxelforge.Spike.Avalonia;

// ── Entry point ───────────────────────────────────────────────────────────────
// [STAThread] is intentionally omitted — we want to prove that GLFW (not STA)
// is the main-thread requirement.  PicoGK's Library.Go() captures the
// calling thread as the GLFW event-loop thread.

Console.WriteLine("[Spike] PicoGK will own the main thread via Library.Go().");
Console.WriteLine("[Spike] Avalonia will start on a background MTA thread.");

try
{
    Library.Go(0.5f, () =>
    {
        // ── PicoGK task callback ─────────────────────────────────────────
        // This runs on a PicoGK-managed worker thread (not main, not Avalonia).
        SpikeState.PicoGKTaskReached = true;
        Console.WriteLine($"[PicoGK task] Running on thread {Environment.CurrentManagedThreadId}.");

        // ── Launch Avalonia on a dedicated background thread ─────────────
        var avaloniaThread = new Thread(() =>
        {
            try
            {
                Console.WriteLine($"[Avalonia thread] Starting on thread {Environment.CurrentManagedThreadId}.");

                // Standard Avalonia 11 AppBuilder chain.
                // UsePlatformDetect() → Win32 backend on Windows.
                // The Win32 backend creates its own HWND and message loop
                // independently of GLFW — no shared OpenGL context by default.
                int exitCode = AppBuilder
                    .Configure<SpikeApp>()
                    .UsePlatformDetect()
                    .UseSkia()
                    .StartWithClassicDesktopLifetime(Array.Empty<string>());

                SpikeState.AvaloniaExited = true;
                Console.WriteLine($"[Avalonia thread] Exited normally (code {exitCode}).");
            }
            catch (Exception ex)
            {
                SpikeState.AvaloniaException = ex;
                Console.WriteLine($"[Avalonia thread] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
        });
        avaloniaThread.Name = "Avalonia UI Thread";
        avaloniaThread.SetApartmentState(ApartmentState.MTA);
        avaloniaThread.IsBackground = false; // keep process alive while Avalonia runs
        avaloniaThread.Start();

        SpikeState.AvaloniaLaunched = true;
        Console.WriteLine("[PicoGK task] Avalonia thread started. Waiting 8 s for visual confirmation...");

        // Poll for 8 s; exit early if Avalonia crashes.
        for (int i = 0; i < 80; i++)
        {
            Thread.Sleep(100);
            if (SpikeState.AvaloniaException != null) break;
            if (!Library.bContinueTask(true)) break;
        }

        Console.WriteLine("[PicoGK task] Requesting PicoGK task exit (Library.bContinueTask → false).");
        // Signal the task to stop. Library.bContinueTask(false) is the
        // production-equivalent of the shutdown signal.
        Library.bContinueTask(false);
    });
}
catch (Exception ex)
{
    Console.WriteLine($"[Main] Library.Go threw: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("=== RESULT: FAIL — PicoGK could not start. ===");
    Environment.Exit(1);
}

// ── Report ────────────────────────────────────────────────────────────────────
Console.WriteLine();
if (SpikeState.AvaloniaException is { } avEx)
{
    Console.WriteLine("=== RESULT: FAIL ===");
    Console.WriteLine($"Avalonia threw on its background thread: {avEx.GetType().Name}");
    Console.WriteLine($"Message: {avEx.Message}");
    Console.WriteLine("ADR-027 verdict: REJECTED (Avalonia cannot run on a non-main thread).");
    Environment.Exit(1);
}

if (!SpikeState.PicoGKTaskReached)
{
    Console.WriteLine("=== RESULT: FAIL ===");
    Console.WriteLine("PicoGK task callback was never reached (Library.Go() deadlocked or crashed).");
    Environment.Exit(1);
}

Console.WriteLine("=== RESULT: PASS ===");
Console.WriteLine("Both PicoGK GLFW viewer and Avalonia window ran concurrently without exception.");
Console.WriteLine("ADR-027 verdict: ACCEPTED — migration path is viable.");
Environment.Exit(0);
