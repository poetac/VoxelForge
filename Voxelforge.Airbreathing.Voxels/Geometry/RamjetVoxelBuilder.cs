// RamjetVoxelBuilder.cs — turn a RamjetContour into a printable
// PicoGK voxel shell + (optional) LPBF printability analysis.
//
// Pipeline (mirrors the simpler subset of the rocket
// ChamberVoxelBuilder.Build at Voxelforge.Voxels:149-161):
//
//   1. Unit bridge: contour metres → mm (single conversion at entry).
//   2. Validate options + auto-resolve voxel size + clamp smoothen radius
//      (CLAUDE.md PicoGK pitfall #1: smoothen ≤ 25 % of wall thickness).
//   3. Build inner gas-path SDF (RevolvedContourImplicit).
//   4. Build outer-shell SDF (= inner radii + WallThickness_mm).
//   5. Voxelise both within a generous bounding box.
//   6. Shell = outer.BoolSubtract(inner) → annular wall solid.
//   7. Smoothen at the clamped radius.
//   8. (Optional) LPBF analysis: synthesise SurfaceSamples via the
//      contour-driven RamjetSurfaceSampler, call the pillar-agnostic
//      LpbfPrintabilityAnalysis.Run from Voxelforge.Core.
//   9. Return RamjetGeometryResult with the voxel handle + scalars +
//      LPBF result.
//
// MVP omissions vs. rocket: no cooling channels, no manifold plenums,
// no radial coolant ports, no flanges. Each follow-on lands additively
// when a concrete consumer surfaces.

using System;
using System.Linq;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Build a printable ramjet shell from a <see cref="RamjetContour"/>.
/// Public entry point for the air-breathing pillar's voxel pipeline.
/// </summary>
public static class RamjetVoxelBuilder
{
    /// <summary>
    /// Default voxel-size cap when auto-resolving from wall thickness.
    /// Ramjets at the airframe-integrated scale (200-1000 mm length,
    /// 50-300 mm OD) print fine at ≤ 0.4 mm voxels; finer resolutions
    /// blow memory budgets without surfacing new feasibility violations.
    /// </summary>
    public const double MaxAutoVoxelSize_mm = 0.4;

    /// <summary>
    /// Density used to estimate <see cref="RamjetGeometryResult.TotalMass_g"/>
    /// [g/cm³]. Hard-coded 7.9 (300-series stainless / Inconel typical)
    /// until the air-breathing material library lands and the
    /// <see cref="LpbfMaterialProfile"/>-derived density is available.
    /// </summary>
    public const double EstimatedMaterialDensity_g_per_cm3 = 7.9;

    /// <summary>
    /// Build the ramjet shell. Must run inside a <c>PicoGK.Library</c>
    /// scope on the task thread (CLAUDE.md PicoGK pitfall #4) — the
    /// caller is responsible for the <c>using var lib = new Library(vox)</c>
    /// or <c>Library.Go(vox, _ => ...)</c> wrapper.
    /// </summary>
    public static RamjetGeometryResult Build(RamjetContour contour, RamjetBuildOptions opts)
    {
        if (contour is null) throw new ArgumentNullException(nameof(contour));
        if (opts    is null) throw new ArgumentNullException(nameof(opts));
        if (opts.WallThickness_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(opts.WallThickness_mm),
                $"Wall thickness must be positive (got {opts.WallThickness_mm:F3} mm).");
        if (contour.Stations.Length < 2)
            throw new ArgumentException(
                "Contour needs ≥ 2 stations to build a body of revolution.",
                nameof(contour));

        // ── 1. Unit bridge: metres → mm at the boundary ───────────────
        var inner_mm = contour.Stations
            .Select(s => (s.X_m * 1000.0, s.R_m * 1000.0))
            .ToArray();
        var outer_mm = inner_mm
            .Select(p => (p.Item1, p.Item2 + opts.WallThickness_mm))
            .ToArray();

        // ── 2. Voxel size + smoothen clamp ────────────────────────────
        // VoxelSize: 0 → auto (≤ wall/4 + ≤ MaxAutoVoxelSize_mm).
        double voxelSize_mm = opts.VoxelSize_mm > 0
            ? opts.VoxelSize_mm
            : Math.Min(opts.WallThickness_mm / 4.0, MaxAutoVoxelSize_mm);
        // Smoothen radius: clamp to 25 % of wall per CLAUDE.md pitfall #1.
        double smoothen_mm = Math.Min(
            opts.SmoothenRadius_mm,
            0.25 * opts.WallThickness_mm);
        if (smoothen_mm < 0) smoothen_mm = 0;

        // ── 3-4. Inner + outer SDFs ───────────────────────────────────
        var innerImpl = new RevolvedContourImplicit(inner_mm);
        var outerImpl = new RevolvedContourImplicit(outer_mm);

        // ── 5. Bounds: include both ends with 2 mm pad on every face ─
        double xMin = inner_mm.Min(p => p.Item1);
        double xMax = inner_mm.Max(p => p.Item1);
        double rMaxOuter = outer_mm.Max(p => p.Item2);
        const float pad_mm = 2f;
        var bounds = new BBox3(
            new Vector3((float)xMin - pad_mm, -(float)rMaxOuter - pad_mm, -(float)rMaxOuter - pad_mm),
            new Vector3((float)xMax + pad_mm,  (float)rMaxOuter + pad_mm,  (float)rMaxOuter + pad_mm));

        // ── 6. Shell = outer − inner ──────────────────────────────────
        var outerSolid = LibraryScope.MakeVoxels(outerImpl, bounds);
        var innerSolid = LibraryScope.MakeVoxels(innerImpl, bounds);
        outerSolid.BoolSubtract(innerSolid);

        // ── 7. LPBF-safe smoothing pass ───────────────────────────────
        if (smoothen_mm > 0)
            outerSolid.Smoothen((float)smoothen_mm);

        // ── 8. LPBF printability analysis (optional) ──────────────────
        LpbfPrintabilityResult? printability = null;
        if (opts.RunLpbfAnalysis && opts.LpbfMaterial is not null)
        {
            var samples = RamjetSurfaceSampler.SampleAxisymmetric(
                contour,
                opts.WallThickness_mm,
                opts.LpbfAzimuthalSamples);

            // Build axis = +X. The ramjet is laid down lengthwise on the
            // build plate; the x-axis grows perpendicular to the layers.
            // Voxel field omitted (trapped-powder analysis is opt-in via
            // a separate sprint when a real-design issue surfaces).
            // Routing graph empty (no internal plumbing in the MVP shell).
            printability = LpbfPrintabilityAnalysis.Run(
                samples:               samples,
                buildAxis:             Vector3.UnitX,
                material:              opts.LpbfMaterial,
                voxelField:            null,
                openings:              null,
                routingGraph:          LpbfRoutingGraph.Empty,
                runOrientationAdvisor: true);
        }

        // ── 9. Compute scalars + return ───────────────────────────────
        double bbLength_mm   = xMax - xMin;
        double bbDiameter_mm = 2.0 * rMaxOuter;
        double throatArea_mm2 = Math.PI * Math.Pow(contour.ThroatStation.R_m * 1000.0, 2);
        double combustorArea_mm2 = inner_mm
            // Pick the largest interior-station radius as the combustor proxy
            // (canonical 5-station ramjet contour: combustor = max-R interior
            // before the throat).
            .Skip(1).SkipLast(1)
            .Select(p => Math.PI * p.Item2 * p.Item2)
            .DefaultIfEmpty(throatArea_mm2)
            .Max();
        double exitArea_mm2 = Math.PI * Math.Pow(contour.ExitStation.R_m * 1000.0, 2);
        double contractionRatio = throatArea_mm2 > 0 ? combustorArea_mm2 / throatArea_mm2 : 0;
        double expansionRatio   = throatArea_mm2 > 0 ? exitArea_mm2     / throatArea_mm2 : 0;

        // Solid volume = annular shell volume from analytical contour
        // (faster + more accurate than mesh-extracting from voxels).
        // Volume of revolution between inner radius r(x) and outer radius
        // R(x) approximated by trapezoidal rule over stations.
        double shellVolume_mm3 = 0;
        double innerSurface_mm2 = 0;
        for (int i = 0; i < inner_mm.Length - 1; i++)
        {
            double x0 = inner_mm[i].Item1, x1 = inner_mm[i + 1].Item1;
            double rI0 = inner_mm[i].Item2, rI1 = inner_mm[i + 1].Item2;
            double rO0 = outer_mm[i].Item2, rO1 = outer_mm[i + 1].Item2;
            double dx = x1 - x0;
            // Annular cross-section area at each station: π·(R² − r²).
            double a0 = Math.PI * (rO0 * rO0 - rI0 * rI0);
            double a1 = Math.PI * (rO1 * rO1 - rI1 * rI1);
            shellVolume_mm3 += 0.5 * (a0 + a1) * dx;
            // Inner-wall lateral surface = 2·π·r̄·slantLength (frustum side area).
            double slantLen = Math.Sqrt(dx * dx + (rI1 - rI0) * (rI1 - rI0));
            innerSurface_mm2 += 2.0 * Math.PI * 0.5 * (rI0 + rI1) * slantLen;
        }
        double mass_g = shellVolume_mm3 * 1e-3 /* mm³ → cm³ */ * EstimatedMaterialDensity_g_per_cm3;

        string description = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Ramjet shell, L={0:F1} mm, OD={1:F1} mm, A_t={2:F1} mm², ε={3:F2}, ε_c={4:F2}, t_wall={5:F2} mm, voxel={6:F3} mm",
            bbLength_mm, bbDiameter_mm, throatArea_mm2,
            expansionRatio, contractionRatio,
            opts.WallThickness_mm, voxelSize_mm);

        return new RamjetGeometryResult(
            Voxels:              new PicoGKVoxelHandle(outerSolid),
            SolidVolume_mm3:     shellVolume_mm3,
            InnerSurfaceArea_mm2: innerSurface_mm2,
            WallThickness_mm:    opts.WallThickness_mm,
            TotalMass_g:         mass_g,
            BoundingLength_mm:   bbLength_mm,
            BoundingDiameter_mm: bbDiameter_mm,
            ThroatArea_mm2:      throatArea_mm2,
            ContractionRatio:    contractionRatio,
            ExpansionRatio:      expansionRatio,
            Description:         description,
            Printability:        printability);
    }
}
