// Program.Marine.cs — marine-pillar dispatch handlers.
//
// Sibling to Program.Airbreathing.cs / Program.ElectricPropulsion.cs.
// Issue #441 — Wave-1 + Wave-2 AUV displacement-hull UI with live in-app
// voxel preview. Reuses the M3 voxel pipeline shipped via PR #436
// (MarineHullVoxelBuilder.Build) — same builder the StlExporter
// subprocess invokes, just called directly on the task thread per the
// PicoGK pitfall #4 contract.

using PicoGK;
using Voxelforge.Marine;
using Voxelforge.Marine.Geometry;

namespace Voxelforge;

public static partial class Program
{
    private static void RegenerateMarineForManualMode(
        MarineConditions cond,
        MarineDesign design)
    {
        try
        {
            SetMarineFormStatus("Solving…");
            var result = MarineOptimization.GenerateWith(design, cond);

            // Voxel preview — only when the design is structurally feasible.
            // Infeasible hulls (e.g. wall too thin for SF≥1.5) still surface
            // physics in the UI but skip the voxel build to avoid wasting
            // ~5-30 s on a design that can't ship.
            MarineHullGeometryResult? geo = null;
            if (result.IsFeasible)
            {
                SetMarineFormStatus("Building voxels…");
                var buildOpts = new MarineHullBuildOptions(
                    WallThickness_mm: design.WallThickness_m * 1000.0);
                geo = MarineHullVoxelBuilder.Build(design, buildOpts);
                UpdateViewerMarine(geo.Shell);
            }

            UpdateMarineFormResults(result, geo);
            string status = result.IsFeasible
                ? $"Ready.  SF {result.BucklingSafetyFactor:F2},  Drag {result.DragForce_N:F1} N"
                : $"Infeasible — {result.Violations.Count} gate(s) failed";
            SetMarineFormStatus(status);
        }
        catch (Exception ex)
        {
            Library.Log($"Marine regen error: {ex}");
            SetMarineFormStatus("Error: " + ex.Message);
        }
    }

    private static void UpdateViewerMarine(IVoxelHandle? handle)
    {
        if (handle is null) return;
        try
        {
            var viewer = Library.oViewer();
            viewer.RemoveAllObjects();
            // Marine pillar owns its own concrete PicoGKVoxelHandle
            // (parallel-pillar policy). Inner is public so we cast directly.
            if (handle is Voxelforge.Marine.PicoGKVoxelHandle marineHandle)
                viewer.Add(marineHandle.Inner, ViewerGroupChamber);
        }
        catch (Exception ex) { Library.Log($"Marine viewer update failed: {ex.Message}"); }
    }

    private static void UpdateMarineFormResults(MarineResult result, MarineHullGeometryResult? geo)
    {
        var form = _marineForm;
        if (form == null || form.IsDisposed) return;
        try { form.BeginInvoke(() => form.UpdateResults(result, geo)); }
        catch { /* form closing */ }
    }

    private static void SetMarineFormStatus(string msg)
    {
        var form = _marineForm;
        if (form == null || form.IsDisposed) return;
        try { form.BeginInvoke(() => form.SetStatus(msg)); }
        catch { /* form closing */ }
    }
}
