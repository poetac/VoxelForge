// Program.cs — Voxelforge.Kiosk entry point.
//
// Two modes:
//   • --headless --preset NAME --seq N [--out PATH]
//       Single-shot: builds one STL with the headless `new Library(0.5)`
//       constructor, exits. Used by the Voxelforge.Tests subprocess
//       regression test (CLAUDE.md pitfall #8 — xUnit + PicoGK).
//
//   • (no args)
//       Interactive kiosk. Mirrors the main app's 3-thread architecture:
//         Main thread — Library.Go(VoxelSizeMM, Run) → PicoGK viewer / GLFW
//         Task thread — Run() → kiosk pipeline (voxel ops here only)
//         STA  thread — KioskForm (WinForms UI, button clicks)
//       Cross-thread communication uses KioskShared (queue + events).
//
//       The PicoGK GLFW viewer window shows the current iteration's
//       voxels live; the WinForms form holds the buttons. Visitor
//       presses "Try Another" until they like a preview, then
//       "Save This" to commit STL + fire render.

using System.Globalization;
using System.IO;
using System.Windows.Forms;
using PicoGK;

namespace Voxelforge.Kiosk;

public static class Program
{
    private const float VoxelSizeMM = 0.5f;

    /// <summary>Viewer group ID for the chamber preview.</summary>
    private const int ViewerGroupChamber = 1;

    private static KioskForm? _form;
    private static volatile bool _formReady = false;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--headless")
            return RunHeadless(args);

        Console.WriteLine("Voxelforge Kiosk — booting PicoGK…");
        Library.Go(VoxelSizeMM, Run);
        return 0;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Headless single-shot (regression-test subprocess + CLI sanity)
    // ──────────────────────────────────────────────────────────────────

    private static int RunHeadless(string[] args)
    {
        string  preset       = KioskPipeline.FdmCanonicals[0];
        int     seq          = 1;
        string? outDir       = null;
        bool    perturb      = true;
        double  voxelSize_mm = KioskPipeline.VoxelSize_mm;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--preset" when i + 1 < args.Length:
                    preset = args[++i]; break;
                case "--seq"    when i + 1 < args.Length:
                    seq    = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--out"    when i + 1 < args.Length:
                    outDir = args[++i]; break;
                case "--no-perturb":
                    perturb = false; break;
                case "--voxel"  when i + 1 < args.Length:
                    voxelSize_mm = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
            }
        }
        outDir ??= KioskSettings.DefaultWatchFolder();

        try
        {
            Console.WriteLine($"[kiosk] Booting headless PicoGK Library at {voxelSize_mm} mm voxel…");
            using var lib = new Library((float)voxelSize_mm);
            // PicoGK 2.0: scoped Library is not the global singleton; register
            // it as the ambient so builder methods reach the new Voxels(lib,…) overloads.
            using var _libScope = Voxelforge.Geometry.LibraryScope.Set(lib);
            Console.WriteLine($"[kiosk] Library ready. Building {preset} #{seq:D4} → {outDir}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = KioskPipeline.Generate(
                preset, seq, outDir, perturb: perturb, voxelSize_mm: voxelSize_mm);
            sw.Stop();
            Console.WriteLine(
                $"[kiosk] Exported {result.PresetName} #{result.SequenceNumber} in {sw.Elapsed.TotalSeconds:F1} s: " +
                $"{result.TriangleCount:N0} tri, {result.StlBytes:N0} bytes → {result.StlPath}");
            Console.WriteLine($"[kiosk] {result.Description}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Kiosk headless build failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Task thread — voxel ops live here only
    // ──────────────────────────────────────────────────────────────────

    /// <summary>The currently-displayed preview, kept across "Try
    /// Another" presses so a "Save This" can commit it without
    /// rebuilding. Replaced (and the prior one released for GC) every
    /// preview build.</summary>
    private static KioskPreview? _currentPreview;

    private static void Run()
    {
        Library.Log("Kiosk task thread started.");

        // Warm copper viewer material (matches the main app's default).
        try
        {
            Library.oViewer().SetGroupMaterial(
                ViewerGroupChamber,
                new ColorFloat(0.75f, 0.50f, 0.32f),
                fMetallic: 0.90f,
                fRoughness: 0.30f);
        }
        catch (Exception ex)
        {
            Library.Log($"Viewer material init failed: {ex.Message}");
        }

        var settings = KioskSettings.Load();
        string? preflightError = TestFolderWritable(settings.WatchFolder);
        if (preflightError != null)
        {
            Library.Log($"Pre-flight: watch folder NOT writable — {preflightError}");
            KioskLog.Write(settings.WatchFolder,
                $"STARTUP: watch folder pre-flight failed: {preflightError}");
        }
        else
        {
            KioskLog.Write(settings.WatchFolder,
                $"STARTUP: kiosk task thread ready, voxel={VoxelSizeMM}mm, watchFolder OK.");
        }

        var uiThread = new Thread(UiThreadMain)
        {
            IsBackground = true,
            Name = "KioskUI",
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        while (!_formReady && Library.bContinueTask(true)) Thread.Sleep(30);
        if (!_formReady || _form == null)
        {
            Library.Log("Kiosk form never initialised — exiting.");
            return;
        }

        while (Library.bContinueTask(true))
        {
            if (KioskShared.TryDequeue(out var req) && req != null)
            {
                ProcessRequest(req, settings);
            }
            else
            {
                Thread.Sleep(20);
            }
        }

        KioskLog.Write(settings.WatchFolder, "SHUTDOWN: kiosk task thread exiting.");
    }

    private static void ProcessRequest(KioskRequest req, KioskSettings settings)
    {
        try
        {
            switch (req)
            {
                case KioskTryAnotherRequest tryAnother:
                    HandleTryAnother(tryAnother, settings);
                    break;

                case KioskCommitRequest commit:
                    HandleCommit(commit, settings);
                    break;
            }
        }
        catch (Exception ex)
        {
            string ctx = req is KioskTryAnotherRequest t ? t.PresetName : "commit";
            KioskLog.Write(settings.WatchFolder,
                $"ERROR ({ctx}): {ex.GetType().Name}: {ex.Message}");
            KioskShared.RaiseError(ex, ctx);
        }
        finally
        {
            // Release native VDB handles between iterations.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static void HandleTryAnother(KioskTryAnotherRequest req, KioskSettings settings)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var preview = KioskPipeline.BuildPreview(
            req.PresetName, req.SequenceNumber, perturb: true);
        sw.Stop();

        // Push to viewer — replace any prior preview.
        try
        {
            var viewer = Library.oViewer();
            viewer.RemoveAllObjects();
            viewer.Add(preview.Voxels, ViewerGroupChamber);
        }
        catch (Exception ex)
        {
            Library.Log($"Viewer update failed: {ex.Message}");
        }

        _currentPreview = preview;

        KioskLog.Write(settings.WatchFolder,
            $"PREVIEW {preview.PresetName} #{preview.SequenceNumber:D4} in {sw.Elapsed.TotalSeconds:F1}s — " +
            $"L={preview.BoundingLength_mm:F0}mm OD={preview.BoundingDiameter_mm:F0}mm");

        KioskShared.RaisePreviewReady(new KioskPreviewReady(
            PresetName:          preview.PresetName,
            SequenceNumber:      preview.SequenceNumber,
            Description:         preview.Description,
            BoundingLength_mm:   preview.BoundingLength_mm,
            BoundingDiameter_mm: preview.BoundingDiameter_mm));
    }

    private static void HandleCommit(KioskCommitRequest req, KioskSettings settings)
    {
        if (_currentPreview is null)
        {
            // Visitor pressed "Save This" before any preview was built.
            // Treat as a no-op rather than an error — the form should
            // already be in a state that prevents this, but be safe.
            return;
        }
        var preview = _currentPreview;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var commit = KioskPipeline.Commit(
            preview, req.WatchFolder,
            onRenderComplete: pngPath =>
            {
                KioskLog.Write(req.WatchFolder, $"RENDER OK: {pngPath}");
                KioskShared.RaiseRenderReady(pngPath);
            },
            onRenderFailed: (pngPath, msg) =>
            {
                KioskLog.Write(req.WatchFolder, $"RENDER FAIL ({pngPath}): {msg}");
            });

        sw.Stop();

        // Advance sequence number. Failure is logged but not propagated
        // (visitor got their STL).
        settings.NextSequence = preview.SequenceNumber + 1;
        try { settings.Save(); }
        catch (Exception saveEx)
        {
            KioskLog.Write(req.WatchFolder,
                $"WARN: settings save failed: {saveEx.Message}");
        }

        KioskLog.Write(req.WatchFolder,
            $"COMMIT #{commit.SequenceNumber:D4} {commit.PresetName} OK in {sw.Elapsed.TotalSeconds:F1}s — " +
            $"{commit.TriangleCount:N0} tri, {commit.StlBytes:N0} B" +
            (commit.RenderPending ? " — render pending" : " — no render (Blender absent)"));

        KioskShared.RaiseCommitReady(commit);
    }

    private static string? TestFolderWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".kiosk-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  STA UI thread
    // ──────────────────────────────────────────────────────────────────

    private static void UiThreadMain()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var settings = KioskSettings.Load();
        _form = new KioskForm(settings);
        _form.Shown += (_, _) => _formReady = true;
        Application.Run(_form);

        try { Library.bContinueTask(false); } catch { /* shutting down */ }
    }
}
