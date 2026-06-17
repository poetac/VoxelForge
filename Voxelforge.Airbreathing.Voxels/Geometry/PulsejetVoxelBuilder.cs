// PulsejetVoxelBuilder.cs — turn a PulsejetContour into a printable
// PicoGK voxel shell + (optional) LPBF printability analysis
// (Wave 1 PR-5, sub-step 1a.5).
//
// Mirrors RamjetVoxelBuilder structure. Pipeline:
//
//   1. Unit bridge: contour metres → mm.
//   2. Validate options + auto-resolve voxel size + clamp smoothen radius
//      (CLAUDE.md PicoGK pitfall #1: smoothen ≤ 25% of wall thickness).
//   3. Build inner gas-path SDF (RevolvedContourImplicit).
//   4. Build outer-shell SDF (= inner radii + WallThickness_mm).
//   5. Voxelise both within a generous bounding box.
//   6. Shell = outer.BoolSubtract(inner) → annular wall solid.
//   7. Smoothen at the clamped radius.
//   8. (Optional) LPBF analysis via the pillar-agnostic
//      LpbfPrintabilityAnalysis.Run from Voxelforge.Core.
//   9. Return PulsejetGeometryResult with the voxel handle + scalars.
//
// MVP omissions: no instrumentation bosses, no flanges. The valveless
// geometry has no moving parts so the build is mechanically simpler
// than the ramjet's CD nozzle.

using System;
using System.Linq;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Build a printable valveless pulsejet shell from a
/// <see cref="PulsejetContour"/>. Public entry point for the pulsejet
/// voxel pipeline.
/// </summary>
public static class PulsejetVoxelBuilder
{
    /// <summary>
    /// Default voxel-size cap when auto-resolving from wall thickness.
    /// Same value as the ramjet builder — pulsejets at the V-1-class
    /// scale (3+ m length, 200-400 mm OD) print fine at ≤ 0.4 mm voxels.
    /// </summary>
    public const double MaxAutoVoxelSize_mm = 0.4;

    /// <summary>
    /// Density used to estimate <see cref="PulsejetGeometryResult.TotalMass_g"/>
    /// [g/cm³]. Hard-coded 7.9 (300-series stainless / Inconel typical).
    /// </summary>
    public const double EstimatedMaterialDensity_g_per_cm3 = 7.9;

    /// <summary>
    /// Build the pulsejet shell. Must run inside a <c>PicoGK.Library</c>
    /// scope on the task thread (CLAUDE.md PicoGK pitfall #4).
    /// </summary>
    public static PulsejetGeometryResult Build(PulsejetContour contour, PulsejetBuildOptions opts)
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

        // 1. Unit bridge: metres → mm.
        var inner_mm = contour.Stations
            .Select(s => (s.X_m * 1000.0, s.R_m * 1000.0))
            .ToArray();
        var outer_mm = inner_mm
            .Select(p => (p.Item1, p.Item2 + opts.WallThickness_mm))
            .ToArray();

        // 2. Voxel size + smoothen clamp.
        double voxelSize_mm = opts.VoxelSize_mm > 0
            ? opts.VoxelSize_mm
            : Math.Min(opts.WallThickness_mm / 4.0, MaxAutoVoxelSize_mm);
        double smoothen_mm = Math.Min(
            opts.SmoothenRadius_mm,
            0.25 * opts.WallThickness_mm);
        if (smoothen_mm < 0) smoothen_mm = 0;

        // 3-4. Inner + outer SDFs.
        var innerImpl = new RevolvedContourImplicit(inner_mm);
        var outerImpl = new RevolvedContourImplicit(outer_mm);

        // 5. Bounds.
        double xMin = inner_mm.Min(p => p.Item1);
        double xMax = inner_mm.Max(p => p.Item1);
        double rMaxOuter = outer_mm.Max(p => p.Item2);
        const float pad_mm = 2f;
        var bounds = new BBox3(
            new Vector3((float)xMin - pad_mm, -(float)rMaxOuter - pad_mm, -(float)rMaxOuter - pad_mm),
            new Vector3((float)xMax + pad_mm,  (float)rMaxOuter + pad_mm,  (float)rMaxOuter + pad_mm));

        // 6. Shell = outer − inner.
        var outerSolid = LibraryScope.MakeVoxels(outerImpl, bounds);
        var innerSolid = LibraryScope.MakeVoxels(innerImpl, bounds);
        outerSolid.BoolSubtract(innerSolid);

        // 7. LPBF-safe smoothing pass.
        if (smoothen_mm > 0)
            outerSolid.Smoothen((float)smoothen_mm);

        // 8. LPBF printability analysis (optional). Pulsejet has zero
        // moving parts and a long-tube + thin-wall geometry — overhang
        // analysis on the diffuser convergent is the most likely
        // advisory. Drain path uses tailpipe exit as the natural
        // evacuation point in horizontal-bed orientation.
        LpbfPrintabilityResult? printability = null;
        if (opts.RunLpbfAnalysis && opts.LpbfMaterial is not null)
        {
            // Adapt the contour to RamjetSurfaceSampler shape (it samples
            // an axisymmetric station array — PulsejetStation has the
            // same essential X / R fields). Map by translating
            // PulsejetStation to RamjetStation with a NozzleThroat-marker
            // section (sampler is section-agnostic in MVP).
            var ramjetStations = contour.Stations
                .Select(s => new RamjetStation(s.X_m, s.R_m, RamjetSection.Combustor))
                .ToArray();
            int throatIdx = 0;
            double minR = ramjetStations[0].R_m;
            for (int i = 1; i < ramjetStations.Length; i++)
                if (ramjetStations[i].R_m < minR)
                {
                    minR = ramjetStations[i].R_m;
                    throatIdx = i;
                }
            var ramjetContour = new RamjetContour(
                Stations:      ramjetStations,
                TotalLength_m: contour.TotalLength_m,
                ThroatIndex:   throatIdx);

            var samples = RamjetSurfaceSampler.SampleAxisymmetric(
                ramjetContour,
                opts.WallThickness_mm,
                opts.LpbfAzimuthalSamples);

            printability = LpbfPrintabilityAnalysis.Run(
                samples:               samples,
                buildAxis:             Vector3.UnitX,
                material:              opts.LpbfMaterial,
                voxelField:            null,
                openings:              null,
                routingGraph:          LpbfRoutingGraph.Empty,
                runOrientationAdvisor: true);
        }

        // 9. Compute scalars + return.
        double bbLength_mm   = xMax - xMin;
        double bbDiameter_mm = 2.0 * rMaxOuter;
        double intakeR_mm    = inner_mm[0].Item2;
        double intakeArea_mm2 = Math.PI * intakeR_mm * intakeR_mm;
        double tailpipeR_mm  = inner_mm[inner_mm.Length - 1].Item2;
        double tailpipeArea_mm2 = Math.PI * tailpipeR_mm * tailpipeR_mm;
        double tubeLength_mm = bbLength_mm;

        // Solid volume = annular shell volume from analytical contour
        // (faster + more accurate than mesh-extracting from voxels).
        double shellVolume_mm3 = 0;
        double innerSurface_mm2 = 0;
        for (int i = 0; i < inner_mm.Length - 1; i++)
        {
            double x0 = inner_mm[i].Item1, x1 = inner_mm[i + 1].Item1;
            double rI0 = inner_mm[i].Item2, rI1 = inner_mm[i + 1].Item2;
            double rO0 = outer_mm[i].Item2, rO1 = outer_mm[i + 1].Item2;
            double dx = x1 - x0;
            double a0 = Math.PI * (rO0 * rO0 - rI0 * rI0);
            double a1 = Math.PI * (rO1 * rO1 - rI1 * rI1);
            shellVolume_mm3 += 0.5 * (a0 + a1) * dx;
            double slantLen = Math.Sqrt(dx * dx + (rI1 - rI0) * (rI1 - rI0));
            innerSurface_mm2 += 2.0 * Math.PI * 0.5 * (rI0 + rI1) * slantLen;
        }
        double mass_g = shellVolume_mm3 * 1e-3 /* mm³ → cm³ */ * EstimatedMaterialDensity_g_per_cm3;

        string description = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Pulsejet shell, L={0:F1} mm, OD={1:F1} mm, A_intake={2:F1} mm², A_exit={3:F1} mm², t_wall={4:F2} mm, voxel={5:F3} mm",
            bbLength_mm, bbDiameter_mm, intakeArea_mm2, tailpipeArea_mm2,
            opts.WallThickness_mm, voxelSize_mm);

        return new PulsejetGeometryResult(
            Voxels:              new PicoGKVoxelHandle(outerSolid),
            SolidVolume_mm3:     shellVolume_mm3,
            InnerSurfaceArea_mm2: innerSurface_mm2,
            WallThickness_mm:    opts.WallThickness_mm,
            TotalMass_g:         mass_g,
            BoundingLength_mm:   bbLength_mm,
            BoundingDiameter_mm: bbDiameter_mm,
            IntakeArea_mm2:      intakeArea_mm2,
            TailpipeArea_mm2:    tailpipeArea_mm2,
            TubeLength_mm:       tubeLength_mm,
            Description:         description,
            Printability:        printability);
    }
}
