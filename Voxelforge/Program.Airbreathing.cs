// Program.Airbreathing.cs — air-breathing-pillar dispatch handlers.
//
// Extracted from Program.cs (Sprint 0 / Wave 1, 2026-05-05) as a
// partial-class slice. Behavior is unchanged — the four methods below
// previously lived inline; moving them out keeps Program.cs focused on
// rocket-pillar dispatch + the GLFW / STA viewer scaffolding shared
// between both families.

using System;
using PicoGK;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Geometry;
using Voxelforge.Geometry;

namespace Voxelforge;

public static partial class Program
{
    // ── Air-breathing request handler (task thread) ───────────────────────

    private static void RegenerateAirbreathingForManualMode(
        FlightConditions cond,
        AirbreathingEngineDesign design,
        RamjetBuildOptions buildOpts)
    {
        try
        {
            SetAbFormStatus("Solving cycle…");

            // 1. Cycle physics (PicoGK-free).
            var result = AirbreathingOptimization.GenerateWith(design, cond);

            // 2. Voxel geometry — dispatch on engine kind.
            RamjetGeometryResult? geo = null;
            if (design.Kind == AirbreathingEngineKind.Ramjet)
            {
                SetAbFormStatus("Building voxels…");
                var contour = RamjetGeometry.From(design);
                var adapter = new RamjetBuilderAdapter();
                geo = adapter.Build(contour, buildOpts);
                UpdateViewerAirbreathing(geo.Voxels);
            }
            else if (design.Kind == AirbreathingEngineKind.Pulsejet)
            {
                SetAbFormStatus("Building voxels…");
                var contour = PulsejetGeometry.From(design);
                var adapter = new PulsejetBuilderAdapter();
                var pulsejetOpts = new PulsejetBuildOptions(
                    WallThickness_mm: buildOpts.WallThickness_mm,
                    RunLpbfAnalysis:  false);
                var pulsejetGeo = adapter.Build(contour, pulsejetOpts);
                UpdateViewerAirbreathing(pulsejetGeo.Voxels);
            }

            UpdateAbFormResults(result, geo);
            string status = result.IsFeasible
                ? $"Ready.  Thrust {result.Stations.ThrustNet_N:F1} N,  Isp {result.Stations.SpecificImpulse_s:F0} s"
                : $"Infeasible — {result.Violations.Count} gate(s) failed";
            SetAbFormStatus(status);
        }
        catch (Exception ex)
        {
            Library.Log($"Airbreathing regen error: {ex}");
            SetAbFormStatus("Error: " + ex.Message);
        }
    }

    private static void UpdateViewerAirbreathing(IVoxelHandle? handle)
    {
        if (handle is null) return;
        try
        {
            var viewer = Library.oViewer();
            viewer.RemoveAllObjects();
            // The airbreathing pillar owns its own PicoGKVoxelHandle
            // (parallel-pillar policy — not the rocket-side type).
            // Inner is public so we cast directly without AsPicoGK().
            if (handle is Voxelforge.Airbreathing.PicoGKVoxelHandle abHandle)
                viewer.Add(abHandle.Inner, ViewerGroupChamber);
        }
        catch (Exception ex) { Library.Log($"Airbreathing viewer update failed: {ex.Message}"); }
    }

    private static void UpdateAbFormResults(AirbreathingResult result, RamjetGeometryResult? geo)
    {
        var form = _abForm;
        if (form == null || form.IsDisposed) return;
        try { form.BeginInvoke(() => form.UpdateResults(result, geo)); }
        catch { /* form closing */ }
    }

    private static void SetAbFormStatus(string msg)
    {
        var form = _abForm;
        if (form == null || form.IsDisposed) return;
        try { form.BeginInvoke(() => form.SetStatus(msg)); }
        catch { /* form closing */ }
    }
}
