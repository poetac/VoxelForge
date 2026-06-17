// NtrChamberVoxelBuilder.cs — NTR nozzle + stub reactor core voxel pipeline.
//
// Wave-1 geometry:
//   1. Generate axisymmetric nozzle contour via ChamberContourGenerator (pure math).
//   2. Build inner-wall SDF from contour station (x, R) pairs.
//   3. Outer-wall SDF = inner + NozzleWallThickness_mm (uniform radial offset).
//   4. Voxelise both with Nuclear's LibraryScope.
//   5. Annular shell = outer.BoolSubtract(inner).
//   6. If EnableStubCore: BoolAdd a cylindrical stub reactor core behind the
//      injector face (no fuel-pin geometry — Wave-1; document deferred to Wave-2).
//   7. Smoothen at capped radius (≤ 25 % of wall — CLAUDE.md pitfall #1).
//
// NOTE: this builder does NOT call Voxelforge.Geometry.ChamberVoxelBuilder.Build()
// because Voxelforge.Geometry.LibraryScope is internal. The nozzle geometry is
// reproduced here directly using the public ChamberContourGenerator + the
// nuclear-pillar's own LibraryScope. This mirrors the pattern used by
// Voxelforge.Marine.Voxels.Geometry.MarineHullVoxelBuilder.
//
// Caller must marshal to the task thread (CLAUDE.md PicoGK pitfall #4).

using System;
using System.Collections.Generic;
using System.Numerics;
using PicoGK;
using Voxelforge.Chamber;
using Voxelforge.Geometry;   // RevolvedContourImplicit (public, from Voxelforge.Voxels)
using Voxelforge.Nuclear;

namespace Voxelforge.Nuclear.Geometry;

/// <summary>
/// Builds an NTR nozzle + stub core printable assembly from a
/// <see cref="NuclearThermalDesign"/>.
/// </summary>
public static class NtrChamberVoxelBuilder
{
    /// <summary>Inconel 718 density [g/mm³] used for mass estimation.</summary>
    private const double Inconel718Density_g_mm3 = 8.22e-3;

    /// <summary>
    /// Build the NTR nozzle shell + optional stub core. Must run on the
    /// task thread inside a PicoGK Library scope.
    /// </summary>
    public static NtrGeometryResult Build(
        NuclearThermalDesign design,
        NtrBuildOptions      opts)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (opts   is null) throw new ArgumentNullException(nameof(opts));
        if (opts.VoxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(opts),
                $"VoxelSize_mm must be positive; got {opts.VoxelSize_mm}.");

        double t_wall = design.NozzleWallThickness_mm;
        if (t_wall <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"NozzleWallThickness_mm must be positive; got {t_wall}.");

        // Smoothen capped at 25 % of wall (CLAUDE.md PicoGK pitfall #1).
        double smoothen = Math.Min(opts.SmoothenRadius_mm, 0.25 * t_wall);

        // ── 1. Generate nozzle contour (pure math, no PicoGK) ─────────────────
        // Use a contraction ratio of 3.0 (reactor face is ~3× throat area).
        // characteristicLength_m = 0.5 m is representative of NTR convergent volume.
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm:       design.ThroatRadius_mm,
            contractionRatio:      3.0,
            expansionRatio:        design.ExpansionRatio,
            characteristicLength_m: 0.5);

        double R_chamber = contour.ChamberRadius_mm;
        double R_throat  = contour.ThroatRadius_mm;
        double R_exit    = contour.ExitRadius_mm;
        double L_nozzle  = contour.TotalLength_mm;

        // ── 2-3. Build inner + outer contour arrays ───────────────────────────
        var innerPts = new List<(double x_mm, double r_mm)>(contour.Stations.Length);
        var outerPts = new List<(double x_mm, double r_mm)>(contour.Stations.Length);
        foreach (var s in contour.Stations)
        {
            innerPts.Add((s.X_mm, s.R_mm));
            outerPts.Add((s.X_mm, s.R_mm + t_wall));
        }

        var innerImpl = new RevolvedContourImplicit(innerPts);
        var outerImpl = new RevolvedContourImplicit(outerPts);

        // ── 4. Voxelise ───────────────────────────────────────────────────────
        double margin  = 5.0;
        double radial  = R_exit + t_wall + margin;
        var bbox = new BBox3(
            new Vector3((float)(-margin),    (float)(-radial), (float)(-radial)),
            new Vector3((float)(L_nozzle + margin), (float)(radial),  (float)(radial)));

        var inner = LibraryScope.MakeVoxels(innerImpl, bbox);
        var outer = LibraryScope.MakeVoxels(outerImpl, bbox);

        // ── 5. Annular shell ──────────────────────────────────────────────────
        outer.BoolSubtract(inner);

        // ── 6. Stub reactor core ──────────────────────────────────────────────
        // Wave-1: cylindrical placeholder only; no fuel-pin geometry.
        if (opts.EnableStubCore)
        {
            double coreLen_mm = design.ReactorCoreLength_mm;
            double coreR_mm   = R_chamber + t_wall;  // same OD as nozzle inlet end

            // Core cylinder extends from x = -coreLen_mm to x = 0 (injector face).
            var corePts = new (double x_mm, double r_mm)[]
            {
                (-coreLen_mm, coreR_mm),
                (0.0,         coreR_mm),
            };
            var coreImpl = new RevolvedContourImplicit(corePts);

            double coreRadial  = coreR_mm + margin;
            var coreBbox = new BBox3(
                new Vector3((float)(-coreLen_mm - margin), (float)(-coreRadial), (float)(-coreRadial)),
                new Vector3((float)(margin),               (float)( coreRadial), (float)( coreRadial)));

            var coreVox = LibraryScope.MakeVoxels(coreImpl, coreBbox);
            outer.BoolAdd(coreVox);
        }

        // ── 7. Smoothen ───────────────────────────────────────────────────────
        if (smoothen > 0.0)
            outer.Smoothen((float)smoothen);

        // ── 8. Scalars ────────────────────────────────────────────────────────
        double solidVolume_mm3 = EstimateSolidVolume(contour, t_wall, design, opts);
        double mass_g = solidVolume_mm3 * Inconel718Density_g_mm3;

        string description =
            $"NTR nozzle+core: L_nozzle={L_nozzle:F0} mm, R_t={R_throat:F1} mm, "
          + $"R_e={R_exit:F1} mm, ε={design.ExpansionRatio:F0}, "
          + $"t_wall={t_wall:F2} mm, mass≈{mass_g:F0} g.";

        return new NtrGeometryResult(
            Voxels:              new PicoGKVoxelHandle(outer),
            SolidVolume_mm3:     solidVolume_mm3,
            TotalMass_g:         mass_g,
            NozzleLength_mm:     L_nozzle,
            BoundingDiameter_mm: 2.0 * (R_exit + t_wall),
            ThroatRadius_mm:     R_throat,
            ExitRadius_mm:       R_exit,
            ExpansionRatio:      design.ExpansionRatio,
            Description:         description);
    }

    // Analytical solid-volume estimate from trapezoidal integration over
    // the contour stations (sum of annular frustum shells).
    private static double EstimateSolidVolume(
        ChamberContour       contour,
        double               t_wall,
        NuclearThermalDesign design,
        NtrBuildOptions      opts)
    {
        double vol = 0.0;
        for (int i = 0; i < contour.Stations.Length - 1; i++)
        {
            double x0 = contour.Stations[i].X_mm;
            double x1 = contour.Stations[i + 1].X_mm;
            double r0_i = contour.Stations[i].R_mm;
            double r1_i = contour.Stations[i + 1].R_mm;
            double r0_o = r0_i + t_wall;
            double r1_o = r1_i + t_wall;
            double dx   = x1 - x0;
            // Frustum annulus volume: π·dx/3·((r_o²+r_o·r_o'+r_o'²) − (r_i²+r_i·r_i'+r_i'²))
            vol += Math.PI * dx / 3.0 *
                (r0_o * r0_o + r0_o * r1_o + r1_o * r1_o
               - r0_i * r0_i - r0_i * r1_i - r1_i * r1_i);
        }
        // Add stub core shell volume if enabled.
        if (opts.EnableStubCore)
        {
            double coreLen = design.ReactorCoreLength_mm;
            double coreR   = contour.ChamberRadius_mm + t_wall;
            vol += Math.PI * coreLen * coreR * coreR;  // solid cylinder (stub)
        }
        return vol;
    }
}
