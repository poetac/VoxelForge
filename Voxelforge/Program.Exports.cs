// Program.Exports.cs — STL / 3MF / VTI / report / save-design handlers.
//
// Extracted from Program.cs (Sprint 0 / Wave 1, 2026-05-05) as a
// partial-class slice. Behavior is unchanged — the eight methods below
// previously lived inline. Op-control infrastructure (BeginOp/EndOp/
// CancelCurrentOp/etc.) remains in Program.cs because it's shared with
// SA orchestration.

using System;
using System.IO;
using PicoGK;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.IO;
using Voxelforge.Optimization;

namespace Voxelforge;

public static partial class Program
{
    private static void HandleExportStl(string path, float voxelMM, bool monolithic)
    {
        if (_lastResult == null) { SetFormStatus("No geometry to export — generate first."); return; }

        // Session voxel size is the process-global PicoGK setting. If the
        // user requested the same voxel, we reuse the in-memory voxels
        // directly — fast path. If they requested a different voxel size
        // (typically smaller for a high-fidelity print STL), we can't
        // re-voxelise in-process because PicoGK's Library is a singleton
        // locked at the startup voxel. Delegate to the subprocess exporter.
        // Topology-aware routing — aerospike designs can't use the fast path
        // because _lastResult.Geometry.Voxels holds the bell-chamber fallback
        // (the aerospike sidecar is physics-only).
        // Monolithic always needs the subprocess too (fused engine body
        // lives in MonolithicEngineBuilder, not _lastResult).
        bool topologyNeedsSubprocess =
            (_lastDesign is not null
                && ChannelTopologyDispatcher.IsAerospikeAxisymmetric(_lastDesign.ChannelTopology))
            || monolithic;
        bool sameAsSession = MathF.Abs(voxelMM - VoxelSizeMM) < 1e-3f
                          && !topologyNeedsSubprocess;

        if (sameAsSession)
        {
            try
            {
                var msg = ChamberVoxelBuilder.ExportStl(_lastResult.Geometry.Voxels.AsPicoGK(), path);
                // OOB-15 follow-up (#257): stamp provenance into the 80-byte STL header.
                IO.ExportMetadata.StampStlHeader(path, _lastResult.DesignHash);
                // SPRINT 4: warn on-screen if this STL is an intermediate best.
                if (_lastResultBestSoFarIter > 0)
                    msg += $" [BEST-SO-FAR iter {_lastResultBestSoFarIter} — not a converged result]";
                SetFormStatus(msg);
            }
            catch (Exception ex) { SetFormStatus("STL export error: " + ex.Message); }
            return;
        }

        // Different voxel — launch headless subprocess.
        if (_lastDesign == null)
        { SetFormStatus("No design to export — generate first."); return; }

        // Preflight memory projection. Fail (block)
        // the export when the projected working set exceeds the user's
        // budget; warn (allow, but alert) when it's close. Uses the
        // last successful voxel build's bounding box as the shape
        // estimate — voxel count grows cubically with 1/voxelSize, so
        // a 4× finer voxel size projects to ~64× the memory.
        long budgetBytes = (long)Voxelforge.UI.ResourceBudget.MemoryBudget_Bytes;
        if (budgetBytes > 0)
        {
            double bx = _lastResult.Geometry.BoundingLength_mm;
            double bd = _lastResult.Geometry.BoundingDiameter_mm;
            var projection = Voxelforge.Analysis.MemoryProjectionGate.Project(
                boundingLx_mm: bx, boundingLy_mm: bd, boundingLz_mm: bd,
                voxelSize_mm: voxelMM, budgetBytes: budgetBytes);
            if (projection.Level == Voxelforge.Analysis.MemoryProjectionLevel.Fail)
            {
                SetFormStatus("STL export blocked — " + projection.Message);
                Library.Log($"MemoryProjectionGate blocked export: {projection.Message}");
                return;
            }
            if (projection.Level == Voxelforge.Analysis.MemoryProjectionLevel.Warning)
                Library.Log($"MemoryProjectionGate warning: {projection.Message}");
        }

        // The subprocess export historically blocked
        // the task thread on `proc.WaitForExit()`, locking the user out
        // of new SA / regen / load commands for the duration. Move the
        // wait onto the threadpool. The task thread immediately returns
        // to its dispatch loop; the export completes asynchronously and
        // posts its status back via SetFormStatus (which already
        // marshals to the UI thread via BeginInvoke).
        //
        // Concurrency contract:
        //   • At most one subprocess export in flight at a time. A
        //     second request while one is running gets a clean reject
        //     status — keeps the cleanup logic simple, no queue.
        //   • Subprocess + main process don't share PicoGK voxel state,
        //     so concurrent SA / regen on the task thread is safe.
        //   • Main-thread fast-path exports (sameAsSession above) STAY
        //     on the task thread because they read PicoGK voxels
        //     directly and PicoGK voxel ops aren't thread-safe.
        if (System.Threading.Interlocked.CompareExchange(
                ref _subprocessExportInFlight, 1, 0) != 0)
        {
            SetFormStatus("STL export already in progress — wait for the current job to finish.");
            return;
        }

        SetFormStatus($"Exporting STL at {voxelMM:F2} mm (re-voxelising in subprocess, async)…");

        string tempDesign = Path.Combine(Path.GetTempPath(),
                                         $"regen_export_{Guid.NewGuid():N}.rcd.json");
        try
        {
            DesignPersistence.Save(tempDesign, _lastResult.Conditions, _lastDesign, _lastResult);
        }
        catch (Exception ex)
        {
            SetFormStatus("Failed to stage design JSON: " + ex.Message);
            System.Threading.Interlocked.Exchange(ref _subprocessExportInFlight, 0);
            return;
        }

        string exeDir = AppContext.BaseDirectory;
        string exporter = Path.Combine(exeDir, "Voxelforge.StlExporter.exe");
        if (!File.Exists(exporter))
        {
            SetFormStatus($"Exporter not found next to main exe ({exporter}). Build the StlExporter project.");
            try { File.Delete(tempDesign); } catch { }
            System.Threading.Interlocked.Exchange(ref _subprocessExportInFlight, 0);
            return;
        }

        // Hand the subprocess wait + cleanup off to the threadpool. The
        // task thread returns immediately to its dispatch loop. Any
        // exceptions are caught and surfaced via SetFormStatus; the
        // in-flight flag is reset in `finally` so a one-shot fault
        // doesn't permanently disable export.
        System.Threading.Tasks.Task.Run(() =>
            RunSubprocessExportAsync(exporter, tempDesign, path, voxelMM, monolithic));
    }

    /// <summary>
    /// Threadpool-side worker for the async STL export. Owns
    /// the subprocess lifetime + temp-file cleanup + status reporting.
    /// Caller must have acquired <see cref="_subprocessExportInFlight"/>;
    /// this method releases it in `finally`.
    ///
    /// Delegate the subprocess launch + Job
    /// Object memory-cap plumbing + BENCH-line parsing to the reusable
    /// <see cref="Voxelforge.Geometry.BuildSubprocess"/> helper.
    /// Preserves every pre-A1 behaviour — identical argument order,
    /// identical memory-cap-breach status message, identical stdout
    /// logging. Strips ~80 lines of Job Object plumbing from this path.
    /// </summary>
    private static void RunSubprocessExportAsync(
        string exporter, string tempDesign, string path, float voxelMM, bool monolithic)
    {
        ulong memCapBytes = Voxelforge.UI.ResourceBudget.MemoryBudget_Bytes;
        bool  demote      = Voxelforge.UI.ResourceBudget.DemotePriority;
        try
        {
            var request = new Voxelforge.Geometry.BuildSubprocessRequest
            {
                DesignJsonPath     = tempDesign,
                OutStlPath         = path,
                StlExporterExePath = exporter,
                VoxelSize_mm       = voxelMM,
                MemoryCapBytes     = memCapBytes,
                DemotePriority     = demote,
                Monolithic         = monolithic,
            };
            var r = Voxelforge.Geometry.BuildSubprocess.Run(request);

            if (r.MemoryCapExceeded)
            {
                long capMB = (long)(memCapBytes / (1024UL * 1024UL));
                SetFormStatus($"STL export exceeded {capMB} MB memory budget — "
                            + $"coarsen the export voxel or raise the Resource Budget cap.");
                Library.Log($"StlExporter killed by Job Object cap ({capMB} MB). Exit {r.ExitCode}.");
                return;
            }

            if (r.Success)
            {
                // OOB-15 follow-up (#257): stamp provenance into the 80-byte STL header.
                IO.ExportMetadata.StampStlHeader(path, _lastResult?.DesignHash);

                string size = new FileInfo(path).Length > 1024 * 1024
                    ? $"{new FileInfo(path).Length / (1024.0 * 1024.0):F1} MB"
                    : $"{new FileInfo(path).Length / 1024.0:F0} KB";

                string tail = (r.GridBuildMs > 0 || r.MeshingMs > 0)
                    ? $", build {r.GridBuildMs / 1000.0:F1} s + mesh {r.MeshingMs / 1000.0:F1} s + write {r.WriteMs / 1000.0:F1} s"
                      + (r.TriangleCount > 0
                          ? $", {(r.TriangleCount >= 1_000_000
                                   ? $"{r.TriangleCount / 1_000_000.0:F1}M"
                                   : $"{r.TriangleCount:N0}")} tris"
                          : "")
                    : "";
                SetFormStatus($"STL exported at {voxelMM:F2} mm → {Path.GetFileName(path)} ({size}{tail})");
                Library.Log($"StlExporter stdout: {r.Stdout.Trim()}");
            }
            else
            {
                SetFormStatus($"Exporter failed (exit {r.ExitCode}): {r.Stderr.Trim()}");
                Library.Log($"StlExporter exit {r.ExitCode}. stderr: {r.Stderr}");
            }
        }
        catch (Exception ex)
        {
            SetFormStatus("Exporter subprocess error: " + ex.Message);
        }
        finally
        {
            try { File.Delete(tempDesign); } catch { }
            // Always release the in-flight flag so a
            // one-shot fault doesn't permanently disable subprocess export.
            System.Threading.Interlocked.Exchange(ref _subprocessExportInFlight, 0);
        }
    }

    /// <summary>
    /// Orchestrates the renderer pipeline.
    /// Runs on a worker thread (not the UI thread, not the task thread).
    /// Steps: stage current design → temp STL via the StlExporter
    /// subprocess → invoke voxelforge-render.exe → cleanup. Posts status
    /// updates to the form via SetFormStatus, which marshals to UI thread.
    /// </summary>
    private static void OrchestrateRender(
        string renderExe, string outputPath, string material, string mode, string resolution, int frames)
    {
        if (_lastResult is null || _lastDesign is null)
        {
            SetFormStatus("Render: no design generated yet — click Generate first.");
            return;
        }

        // Stage the current design + a temp STL path. The renderer wants STL,
        // so we route via the StlExporter subprocess at default 0.4 mm voxel
        // (fast, plenty for a render preview; future enhancement could re-
        // voxelise at 0.15 mm for the maximum-resolution preset).
        string tempDesign = Path.Combine(Path.GetTempPath(),
                                          $"regen_render_{Guid.NewGuid():N}.rcd.json");
        string tempStl = Path.ChangeExtension(tempDesign, ".stl");
        try
        {
            DesignPersistence.Save(tempDesign, _lastResult.Conditions, _lastDesign, _lastResult);
        }
        catch (Exception ex)
        {
            SetFormStatus("Render: failed to stage design JSON — " + ex.Message);
            return;
        }

        try
        {
            // Step 1: temp STL via StlExporter subprocess.
            string exeDir = AppContext.BaseDirectory;
            string exporter = Path.Combine(exeDir, "Voxelforge.StlExporter.exe");
            if (!File.Exists(exporter))
            {
                SetFormStatus($"Render: StlExporter not found at {exporter}.");
                return;
            }
            SetFormStatus($"Render: building temp STL at {VoxelSizeMM:F2} mm voxel…");
            var exportResult = Voxelforge.Geometry.BuildSubprocess.Run(
                new Voxelforge.Geometry.BuildSubprocessRequest
                {
                    DesignJsonPath     = tempDesign,
                    OutStlPath         = tempStl,
                    StlExporterExePath = exporter,
                    VoxelSize_mm       = VoxelSizeMM,
                    MemoryCapBytes     = Voxelforge.UI.ResourceBudget.MemoryBudget_Bytes,
                    DemotePriority     = Voxelforge.UI.ResourceBudget.DemotePriority,
                    Monolithic         = false,
                });
            if (!exportResult.Success || !File.Exists(tempStl))
            {
                SetFormStatus($"Render: STL export failed (exit {exportResult.ExitCode}). {exportResult.Stderr.Trim()}");
                return;
            }

            // Step 2: invoke voxelforge-render.exe. Build args programmatically.
            SetFormStatus($"Render: invoking Blender for {mode} ({material}, {resolution})…");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = renderExe,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };
            psi.ArgumentList.Add("--in");          psi.ArgumentList.Add(tempStl);
            psi.ArgumentList.Add("--out");         psi.ArgumentList.Add(outputPath);
            psi.ArgumentList.Add("--mode");        psi.ArgumentList.Add(mode);
            psi.ArgumentList.Add("--material");    psi.ArgumentList.Add(material);
            psi.ArgumentList.Add("--resolution");  psi.ArgumentList.Add(resolution);
            if (mode == "turntable")
            {
                psi.ArgumentList.Add("--frames");
                psi.ArgumentList.Add(frames.ToString());
            }

            using var renderProc = System.Diagnostics.Process.Start(psi);
            renderProc?.WaitForExit();

            if (renderProc is null || renderProc.ExitCode != 0)
            {
                SetFormStatus($"Render: voxelforge-render exited with code {renderProc?.ExitCode}");
            }
            else
            {
                string fileLabel = mode == "turntable"
                    ? $"{frames}-frame turntable → {Path.GetFileName(outputPath)}_NNNN.png"
                    : Path.GetFileName(outputPath);
                SetFormStatus($"Render: wrote {fileLabel}");
            }
        }
        catch (Exception ex)
        {
            SetFormStatus("Render: orchestration error — " + ex.Message);
        }
        finally
        {
            try { File.Delete(tempDesign); } catch { }
            try { File.Delete(tempStl);    } catch { }
        }
    }

    /// <summary>
    /// Pull a single "BENCH key=value" line out of the
    /// subprocess stdout. Returns 0 when the key is absent or non-numeric
    /// so a missing field degrades the status message gracefully instead
    /// of throwing. Scans line-by-line because the stdout stream mixes
    /// BENCH lines with plain "Exported STL …" confirmation lines.
    /// </summary>
    private static double ParseBenchMs(string stdout, string key)
    {
        string needle = $"BENCH {key}=";
        foreach (var line in stdout.Split('\n'))
        {
            int i = line.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) continue;
            int start = i + needle.Length;
            int end = start;
            while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.' || line[end] == '-'))
                end++;
            if (end > start && double.TryParse(line.AsSpan(start, end - start),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double v))
                return v;
        }
        return 0;
    }

    private static long ParseBenchLong(string stdout, string key)
    {
        string needle = $"BENCH {key}=";
        foreach (var line in stdout.Split('\n'))
        {
            int i = line.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) continue;
            int start = i + needle.Length;
            int end = start;
            while (end < line.Length && char.IsDigit(line[end])) end++;
            if (end > start && long.TryParse(line.AsSpan(start, end - start), out long v))
                return v;
        }
        return 0;
    }

    /// <summary>
    /// PHASE 7: export 3MF at session voxel. Writes a temp STL, wraps it
    /// with LPBF metadata into a .3mf ZIP, deletes the temp. Different voxel
    /// sizes would require the subprocess exporter path (future).
    /// </summary>
    private static void HandleExport3MF(string path)
    {
        if (_lastResult == null) { SetFormStatus("No geometry to export — generate first."); return; }
        string tempStl = Path.Combine(Path.GetTempPath(), $"regen_3mf_{Guid.NewGuid():N}.stl");
        try
        {
            ChamberVoxelBuilder.ExportStl(_lastResult.Geometry.Voxels.AsPicoGK(), tempStl);
            IO.ThreeMFExport.SaveFromStl(tempStl, path, _lastResult);
            var info = new FileInfo(path);
            SetFormStatus($"3MF saved → {path}  ({info.Length / 1024.0:F0} kB)");
        }
        catch (Exception ex) { SetFormStatus("3MF export error: " + ex.Message); }
        finally { try { File.Delete(tempStl); } catch { } }
    }

    private static void HandleExportReport(string path)
    {
        if (_lastResult == null) { SetFormStatus("No results to report — generate first."); return; }
        try
        {
            // SPRINT 4: stamp BEST-SO-FAR banner when exporting during an active run.
            // PHASE 6: attach the Pareto front from the most recent SA run (if any).
            ReportExport.SaveToFile(
                _lastResult, path,
                _lastResultBestSoFarIter,
                _lastParetoFrontSnapshot);
            string suffix = _lastResultBestSoFarIter > 0
                ? $" (BEST-SO-FAR iter {_lastResultBestSoFarIter})" : "";
            if (_lastParetoFrontSnapshot is { Count: > 0 } snap)
                suffix += $" [Pareto: {snap.Count}]";
            SetFormStatus("Report saved → " + path + suffix);
        }
        catch (Exception ex) { SetFormStatus("Report export error: " + ex.Message); }
    }

    /// <summary>
    /// Export CFD field (.vti). Dispatches
    /// <see cref="IO.CfdFieldExport.WriteAerospike"/> when the last
    /// result carries an aerospike sidecar, else the bell-chamber
    /// <see cref="IO.CfdFieldExport.Write"/>. Both paths are
    /// PicoGK-free so this runs safely on the task thread alongside
    /// any in-flight voxel op. Non-fatal on failure — surfaces the
    /// error to the status bar but doesn't tear down the app.
    /// </summary>
    private static void HandleExportVti(string path)
    {
        if (_lastResult == null) { SetFormStatus("No results to export — generate first."); return; }
        try
        {
            IO.CfdFieldStats stats;
            if (_lastResult.Aerospike is { } aero)
            {
                // Aerospike path — reads sidecar contour + optional thermal.
                stats = IO.CfdFieldExport.WriteAerospike(
                    outPath: path,
                    contour: aero.Contour,
                    thermal: aero.Thermal);
                SetFormStatus(
                    $"VTI saved (aerospike) → {path}  "
                  + $"({stats.Nx}×{stats.Ny}×{stats.Nz}, "
                  + $"{stats.SolidVoxelCount} solid / {stats.FluidVoxelCount} fluid, "
                  + $"{stats.FileBytes / 1024.0:F0} kB, {stats.WriteWallMs:F0} ms)");
            }
            else
            {
                // Bell-chamber path — needs the design record for the
                // channel-schedule fields (channel count, rib thickness,
                // height taper). Those live on RegenChamberDesign, not
                // the RegenGenerationResult, so bail with a friendly
                // message if the in-memory design is missing (shouldn't
                // happen after a normal Generate — defensive guard).
                if (_lastDesign == null)
                {
                    SetFormStatus("VTI export needs the last design — re-run Generate first.");
                    return;
                }
                var channels = new HeatTransfer.ChannelSchedule(
                    ChannelCount:              _lastDesign.ChannelCount,
                    RibThickness_mm:           _lastDesign.RibThickness_mm,
                    GasSideWallThickness_mm:   _lastDesign.GasSideWallThickness_mm,
                    ChannelHeightAtChamber_mm: _lastDesign.ChannelHeightChamber_mm,
                    ChannelHeightAtThroat_mm:  _lastDesign.ChannelHeightThroat_mm,
                    ChannelHeightAtExit_mm:    _lastDesign.ChannelHeightExit_mm);
                stats = IO.CfdFieldExport.Write(
                    outPath:                 path,
                    contour:                 _lastResult.Contour,
                    channels:                channels,
                    solver:                  _lastResult.Thermal,
                    outerJacketThickness_mm: _lastDesign.OuterJacketThickness_mm);
                SetFormStatus(
                    $"VTI saved → {path}  "
                  + $"({stats.Nx}×{stats.Ny}×{stats.Nz}, "
                  + $"{stats.SolidVoxelCount} solid / {stats.FluidVoxelCount} fluid, "
                  + $"{stats.FileBytes / 1024.0:F0} kB, {stats.WriteWallMs:F0} ms)");
            }
        }
        catch (Exception ex) { SetFormStatus("VTI export error: " + ex.Message); }
    }

    private static void HandleSaveDesign(string path, OperatingConditions c, RegenChamberDesign d)
    {
        try
        {
            DesignPersistence.Save(path, c, d, _lastResult);
            string suffix = _lastResultBestSoFarIter > 0
                ? $" (BEST-SO-FAR iter {_lastResultBestSoFarIter})" : "";
            SetFormStatus("Saved → " + path + suffix);
        }
        catch (Exception ex) { SetFormStatus("Save error: " + ex.Message); }
    }
}
