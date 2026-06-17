// TurbofanVoxelBuilder.cs — turn a TurbofanContour into a printable PicoGK
// voxel shell with two concentric annular regions:
//
//   Inner (core flow shell):
//     bounded by core-inner SDF (= contour radius) and core-outer SDF
//     (= core-inner + WallThickness_mm).
//   Outer (bypass-duct shell):
//     bounded by bypass-inner SDF (= bypass-duct outer radius from contour)
//     and bypass-outer SDF (= bypass-inner + BypassDuctWallThickness_mm).
//
// Both shells are open at inlet + exit (no end caps) — the inlet face acts
// as a structural ring that fastens the two flow paths together. The voxel
// builder produces a single combined shell mesh; the two structural regions
// share material at the inlet ring after the BoolAdd.
//
// Wave-2 follow-on for issue #441 — the most-built-out airbreathing cycle
// finally gets a voxel pipeline alongside ramjet + pulsejet.

using System;
using System.Linq;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// Build a printable turbofan shell from a <see cref="TurbofanContour"/>.
/// Public entry point for the air-breathing pillar's turbofan voxel
/// pipeline.
/// </summary>
public static class TurbofanVoxelBuilder
{
    /// <summary>
    /// Default voxel-size cap when auto-resolving from wall thickness.
    /// Matches <c>RamjetVoxelBuilder.MaxAutoVoxelSize_mm</c>.
    /// </summary>
    public const double MaxAutoVoxelSize_mm = 0.4;

    /// <summary>
    /// Density used to estimate <see cref="TurbofanGeometryResult.TotalMass_g"/>
    /// [g/cm³]. 7.9 (300-series stainless / Inconel typical), matching
    /// <c>RamjetVoxelBuilder.EstimatedMaterialDensity_g_per_cm3</c>.
    /// </summary>
    public const double EstimatedMaterialDensity_g_per_cm3 = 7.9;

    /// <summary>
    /// Build the turbofan shell. Must run inside a <c>PicoGK.Library</c>
    /// scope on the task thread (CLAUDE.md PicoGK pitfall #4).
    /// </summary>
    public static TurbofanGeometryResult Build(
        TurbofanContour contour,
        TurbofanBuildOptions opts)
    {
        if (contour is null) throw new ArgumentNullException(nameof(contour));
        if (opts    is null) throw new ArgumentNullException(nameof(opts));
        if (opts.WallThickness_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(opts.WallThickness_mm),
                $"Core wall thickness must be positive (got {opts.WallThickness_mm:F3} mm).");
        if (opts.BypassDuctWallThickness_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(opts.BypassDuctWallThickness_mm),
                $"Bypass-duct wall thickness must be positive (got {opts.BypassDuctWallThickness_mm:F3} mm).");
        if (contour.CoreStations.Length < 2)
            throw new ArgumentException(
                "Contour needs ≥ 2 core stations to build a body of revolution.",
                nameof(contour));
        if (contour.BypassOuterRadii_m.Length != contour.CoreStations.Length)
            throw new ArgumentException(
                "Bypass radii array length must match core stations length.",
                nameof(contour));

        // ── 1. Unit bridge: metres → mm at the boundary ──────────────────
        int n = contour.CoreStations.Length;
        var coreInner_mm = new (double x, double r)[n];
        var coreOuter_mm = new (double x, double r)[n];
        var bypassInner_mm = new (double x, double r)[n];
        var bypassOuter_mm = new (double x, double r)[n];
        for (int i = 0; i < n; i++)
        {
            double x = contour.CoreStations[i].X_m * 1000.0;
            double rCore = contour.CoreStations[i].R_m * 1000.0;
            double rBypass = contour.BypassOuterRadii_m[i] * 1000.0;
            coreInner_mm[i]   = (x, rCore);
            coreOuter_mm[i]   = (x, rCore + opts.WallThickness_mm);
            bypassInner_mm[i] = (x, rBypass);
            bypassOuter_mm[i] = (x, rBypass + opts.BypassDuctWallThickness_mm);
        }

        // ── 2. Voxel size + smoothen clamp ───────────────────────────────
        double thinnestWall = Math.Min(opts.WallThickness_mm, opts.BypassDuctWallThickness_mm);
        double voxelSize_mm = opts.VoxelSize_mm > 0
            ? opts.VoxelSize_mm
            : Math.Min(thinnestWall / 4.0, MaxAutoVoxelSize_mm);
        double smoothen_mm = Math.Min(opts.SmoothenRadius_mm, 0.25 * thinnestWall);
        if (smoothen_mm < 0) smoothen_mm = 0;

        // ── 3-4. Build SDFs ──────────────────────────────────────────────
        var coreInnerImpl   = new RevolvedContourImplicit(coreInner_mm.Select(p => (p.x, p.r)));
        var coreOuterImpl   = new RevolvedContourImplicit(coreOuter_mm.Select(p => (p.x, p.r)));
        var bypassInnerImpl = new RevolvedContourImplicit(bypassInner_mm.Select(p => (p.x, p.r)));
        var bypassOuterImpl = new RevolvedContourImplicit(bypassOuter_mm.Select(p => (p.x, p.r)));

        // ── 5. Bounds ────────────────────────────────────────────────────
        double xMin = coreInner_mm.Min(p => p.x);
        double xMax = coreInner_mm.Max(p => p.x);
        double rMaxOuter = bypassOuter_mm.Max(p => p.r);
        const float pad_mm = 2f;
        var bounds = new BBox3(
            new Vector3((float)xMin - pad_mm, -(float)rMaxOuter - pad_mm, -(float)rMaxOuter - pad_mm),
            new Vector3((float)xMax + pad_mm,  (float)rMaxOuter + pad_mm,  (float)rMaxOuter + pad_mm));

        // ── 6. Core shell = coreOuter − coreInner ────────────────────────
        var coreOuterSolid = LibraryScope.MakeVoxels(coreOuterImpl, bounds);
        var coreInnerSolid = LibraryScope.MakeVoxels(coreInnerImpl, bounds);
        coreOuterSolid.BoolSubtract(coreInnerSolid);

        // ── 7. Bypass shell = bypassOuter − bypassInner ──────────────────
        var bypassOuterSolid = LibraryScope.MakeVoxels(bypassOuterImpl, bounds);
        var bypassInnerSolid = LibraryScope.MakeVoxels(bypassInnerImpl, bounds);
        bypassOuterSolid.BoolSubtract(bypassInnerSolid);

        // ── 8. Combined shell = core ∪ bypass ────────────────────────────
        coreOuterSolid.BoolAdd(bypassOuterSolid);

        // ── 9. LPBF-safe smoothing pass ──────────────────────────────────
        if (smoothen_mm > 0)
            coreOuterSolid.Smoothen((float)smoothen_mm);

        // ── 10. Compute scalars ──────────────────────────────────────────
        double bbLength_mm   = xMax - xMin;
        double bbDiameter_mm = 2.0 * rMaxOuter;
        double coreThroatArea_mm2 = Math.PI * Math.Pow(contour.CoreThroatStation.R_m * 1000.0, 2);
        double coreExitArea_mm2   = Math.PI * Math.Pow(contour.CoreExitStation.R_m * 1000.0, 2);
        double bypassExitR_mm     = contour.BypassOuterRadii_m[^1] * 1000.0;
        double bypassInnerExitR_mm = contour.CoreStations[^1].R_m * 1000.0
                                   + opts.WallThickness_mm; // bypass inner = core outer at exit
        double bypassExitArea_mm2 = Math.PI * (bypassExitR_mm * bypassExitR_mm
                                              - bypassInnerExitR_mm * bypassInnerExitR_mm);

        // Bypass ratio recovered from area ratio at the exit (consistency check).
        double bypassRatio = coreExitArea_mm2 > 0 ? bypassExitArea_mm2 / coreExitArea_mm2 : 0;

        // Use largest interior core station as the combustor proxy (matches ramjet).
        double combustorArea_mm2 = coreInner_mm
            .Skip(1).SkipLast(1)
            .Select(p => Math.PI * p.r * p.r)
            .DefaultIfEmpty(coreThroatArea_mm2)
            .Max();
        double contractionRatio = coreThroatArea_mm2 > 0 ? combustorArea_mm2 / coreThroatArea_mm2 : 0;
        double expansionRatio   = coreThroatArea_mm2 > 0 ? coreExitArea_mm2  / coreThroatArea_mm2 : 0;

        // Trapezoidal annular volume + inner-wall surface, summed across both shells.
        double shellVolume_mm3 = 0;
        double innerSurface_mm2 = 0;
        for (int i = 0; i < n - 1; i++)
        {
            double x0 = coreInner_mm[i].x,   x1 = coreInner_mm[i + 1].x;
            double dx = x1 - x0;

            // Core annular cross-section.
            double rCi0 = coreInner_mm[i].r,   rCi1 = coreInner_mm[i + 1].r;
            double rCo0 = coreOuter_mm[i].r,   rCo1 = coreOuter_mm[i + 1].r;
            double aCore0 = Math.PI * (rCo0 * rCo0 - rCi0 * rCi0);
            double aCore1 = Math.PI * (rCo1 * rCo1 - rCi1 * rCi1);
            shellVolume_mm3 += 0.5 * (aCore0 + aCore1) * dx;

            // Bypass annular cross-section.
            double rBi0 = bypassInner_mm[i].r, rBi1 = bypassInner_mm[i + 1].r;
            double rBo0 = bypassOuter_mm[i].r, rBo1 = bypassOuter_mm[i + 1].r;
            double aByp0 = Math.PI * (rBo0 * rBo0 - rBi0 * rBi0);
            double aByp1 = Math.PI * (rBo1 * rBo1 - rBi1 * rBi1);
            shellVolume_mm3 += 0.5 * (aByp0 + aByp1) * dx;

            // Core inner-wall lateral surface (frustum side area).
            double slantCore = Math.Sqrt(dx * dx + (rCi1 - rCi0) * (rCi1 - rCi0));
            innerSurface_mm2 += 2.0 * Math.PI * 0.5 * (rCi0 + rCi1) * slantCore;
        }
        double mass_g = shellVolume_mm3 * 1e-3 * EstimatedMaterialDensity_g_per_cm3;

        string description = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Turbofan shell, L={0:F1} mm, OD={1:F1} mm, A_core_t={2:F1} mm², ε={3:F2}, "
                + "ε_c={4:F2}, BPR={5:F2}, t_core={6:F2} mm, t_bypass={7:F2} mm, voxel={8:F3} mm",
            bbLength_mm, bbDiameter_mm, coreThroatArea_mm2,
            expansionRatio, contractionRatio, bypassRatio,
            opts.WallThickness_mm, opts.BypassDuctWallThickness_mm, voxelSize_mm);

        // LPBF printability analysis (optional). Uses the turbofan-aware
        // surface sampler that walks both flow paths — emits 4 wall samples
        // per (station × azimuthal-slot) for the core inner / core outer /
        // bypass inner / bypass outer surfaces.
        LpbfPrintabilityResult? printability = null;
        if (opts.RunLpbfAnalysis && opts.LpbfMaterial is not null)
        {
            var lpbfSamples = TurbofanSurfaceSampler.SampleAxisymmetric(
                contour,
                opts.WallThickness_mm,
                opts.BypassDuctWallThickness_mm,
                opts.LpbfAzimuthalSamples);

            // Build axis = +X (laid lengthwise on the build plate, same as ramjet).
            // Voxel field omitted (trapped-powder analysis opt-in via separate sprint).
            // Routing graph empty (no internal plumbing in this MVP shell).
            printability = LpbfPrintabilityAnalysis.Run(
                samples:               lpbfSamples,
                buildAxis:             Vector3.UnitX,
                material:              opts.LpbfMaterial,
                voxelField:            null,
                openings:              null,
                routingGraph:          LpbfRoutingGraph.Empty,
                runOrientationAdvisor: true);
        }

        return new TurbofanGeometryResult(
            Voxels:                     new PicoGKVoxelHandle(coreOuterSolid),
            SolidVolume_mm3:            shellVolume_mm3,
            InnerSurfaceArea_mm2:       innerSurface_mm2,
            WallThickness_mm:           opts.WallThickness_mm,
            BypassDuctWallThickness_mm: opts.BypassDuctWallThickness_mm,
            TotalMass_g:                mass_g,
            BoundingLength_mm:          bbLength_mm,
            BoundingDiameter_mm:        bbDiameter_mm,
            CoreThroatArea_mm2:         coreThroatArea_mm2,
            BypassExitArea_mm2:         bypassExitArea_mm2,
            BypassRatio:                bypassRatio,
            ContractionRatio:           contractionRatio,
            ExpansionRatio:             expansionRatio,
            Description:                description,
            Printability:               printability);
    }
}
