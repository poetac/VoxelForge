// Program.ElectricPropulsion.cs — electric-propulsion-pillar dispatch handlers.
//
// Sibling to Program.Airbreathing.cs / Program.Marine.cs. Issue #441 —
// Wave-1 resistojet UI with live in-app voxel preview via
// ResistojetVoxelBuilder. HET / arcjet / ion / MPD slot in when those
// kinds ship.

using PicoGK;
using Voxelforge.ElectricPropulsion;
using Voxelforge.ElectricPropulsion.Geometry;

namespace Voxelforge;

public static partial class Program
{
    private static void RegenerateElectricPropulsionForManualMode(
        ResistojetConditions cond,
        ElectricPropulsionEngineDesign design)
    {
        try
        {
            SetEpFormStatus("Solving…");
            var result = ElectricPropulsionOptimization.GenerateWith(design, cond);

            // Voxel preview — only when the design is structurally feasible.
            ResistojetGeometryResult? geo = null;
            if (result.IsFeasible)
            {
                SetEpFormStatus("Building voxels…");
                // Smoothen radius capped at 25 % of wall (PicoGK pitfall #1).
                var buildOpts = new ResistojetBuildOptions(
                    VoxelSize_mm:      0.10,
                    SmoothenRadius_mm: Math.Min(0.05, 0.25 * design.ChamberWallThickness_mm));
                geo = ResistojetVoxelBuilder.Build(design, buildOpts);
                UpdateViewerElectricPropulsion(geo.Voxels);
            }

            UpdateEpFormResults(result, geo);
            string status = result.IsFeasible
                ? $"Ready.  Thrust {result.Thrust_N:F4} N,  Isp {result.IspVacuum_s:F0} s"
                : $"Infeasible — {result.Violations.Count} gate(s) failed";
            SetEpFormStatus(status);
        }
        catch (Exception ex)
        {
            Library.Log($"Electric-propulsion regen error: {ex}");
            SetEpFormStatus("Error: " + ex.Message);
        }
    }

    private static void UpdateViewerElectricPropulsion(IVoxelHandle? handle)
    {
        if (handle is null) return;
        try
        {
            var viewer = Library.oViewer();
            viewer.RemoveAllObjects();
            // EP pillar owns its own concrete PicoGKVoxelHandle (parallel-pillar policy).
            if (handle is Voxelforge.ElectricPropulsion.PicoGKVoxelHandle epHandle)
                viewer.Add(epHandle.Inner, ViewerGroupChamber);
        }
        catch (Exception ex) { Library.Log($"EP viewer update failed: {ex.Message}"); }
    }

    private static void UpdateEpFormResults(ElectricPropulsionResult result, ResistojetGeometryResult? geo)
    {
        var form = _epForm;
        if (form == null || form.IsDisposed) return;
        try { form.BeginInvoke(() => form.UpdateResults(result, geo)); }
        catch { /* form closing */ }
    }

    private static void SetEpFormStatus(string msg)
    {
        var form = _epForm;
        if (form == null || form.IsDisposed) return;
        try { form.BeginInvoke(() => form.SetStatus(msg)); }
        catch { /* form closing */ }
    }

    // ── Avalonia electric-propulsion path (ADR-027 Phase 1) ──────────────────
    //
    // Parallel to RegenerateElectricPropulsionForManualMode; kept separate so
    // the two UI paths can diverge independently in later phases. Reuses the
    // existing UpdateViewerElectricPropulsion for GLFW side-effects.

    private static void RegenerateForAvaloniaElectricMode(
        ResistojetConditions cond,
        ElectricPropulsionEngineDesign design)
    {
        try
        {
            SetAvaloniaEpStatus("Solving…");
            var result = ElectricPropulsionOptimization.GenerateWith(design, cond);

            ResistojetGeometryResult? geo = null;
            if (result.IsFeasible)
            {
                SetAvaloniaEpStatus("Building voxels…");
                var buildOpts = new ResistojetBuildOptions(
                    VoxelSize_mm:      0.10,
                    SmoothenRadius_mm: Math.Min(0.05, 0.25 * design.ChamberWallThickness_mm));
                geo = ResistojetVoxelBuilder.Build(design, buildOpts);
                UpdateViewerElectricPropulsion(geo.Voxels);   // reuse GLFW update
            }

            UpdateAvaloniaEpResults(result, geo);
            SetAvaloniaEpStatus(result.IsFeasible
                ? $"Ready.  Thrust {result.Thrust_N:F4} N,  Isp {result.IspVacuum_s:F0} s"
                : $"Infeasible — {result.Violations.Count} gate(s) failed");
        }
        catch (Exception ex)
        {
            Library.Log($"Avalonia EP regen error: {ex}");
            SetAvaloniaEpStatus("Error: " + ex.Message);
        }
    }

    private static void UpdateAvaloniaEpResults(
        ElectricPropulsionResult result, ResistojetGeometryResult? geo)
    {
        // ElectricPropulsionWindow.UpdateResults calls Dispatcher.UIThread.Post internally;
        // safe to call from the task thread.
        _avaloniaEpWindow?.UpdateResults(result, geo);
    }

    private static void SetAvaloniaEpStatus(string msg)
    {
        // ElectricPropulsionWindow.SetStatus calls Dispatcher.UIThread.Post internally;
        // safe to call from the task thread.
        _avaloniaEpWindow?.SetStatus(msg);
    }
}
