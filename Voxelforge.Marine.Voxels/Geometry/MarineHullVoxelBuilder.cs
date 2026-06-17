// MarineHullVoxelBuilder.cs — Myring AUV hull voxel pipeline.
//
// Three-part SDF pipeline using the Myring (1976) nose/tail profile and
// a cylindrical mid-body. SDFs are NOT imported from the airbreathing or
// rocket pillars (VFA001 — ADR-026).
//
// Build pipeline:
//   1. Sample Myring profile at N=200 stations (metres → mm unit bridge).
//   2. Outer hull profile = inner profile + WallThickness_mm.
//   3. Voxelise outer and inner as bodies of revolution (MarineProfileImplicit).
//   4. Shell = outer.BoolSubtract(inner) → annular shell.
//   5. Smoothen at clamped radius (≤ 25% of wall per CLAUDE.md pitfall #1).
//   6. Wrap PicoGK.Voxels in PicoGKVoxelHandle and return MarineHullGeometryResult.
//
// Caller is responsible for the PicoGK.Library scope (CLAUDE.md pitfall #4).
//
// References:
//   Myring, D. F. (1976). Aeronautical Quarterly 27(3), 186-194.
//   CLAUDE.md PicoGK pitfalls #1 (smoothen cap) and #4 (task-thread only).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using PicoGK;
using Voxelforge.Marine;
using Voxelforge.Marine.Hydrodynamics;

namespace Voxelforge.Marine.Geometry;

/// <summary>
/// Builds a printable marine AUV hull shell from a <see cref="MarineDesign"/>.
/// Implements <see cref="IMarineVoxelGenerator"/> as a static helper class;
/// the concrete singleton <see cref="MarineVoxelGenerator"/> implements the
/// interface for dependency injection.
/// </summary>
public static class MarineHullVoxelBuilder
{
    private const int ProfileStations = 200;

    // Material densities [g/cm³] — Ti-6Al-4V, Al-6061, AISI-316L LPBF
    private static readonly double[] MaterialDensity_g_cm3 = { 4.43, 2.70, 7.95 };

    /// <summary>
    /// Build the hull shell. Must run on the task thread inside a PicoGK Library scope.
    /// </summary>
    public static MarineHullGeometryResult Build(MarineDesign design, MarineHullBuildOptions opts)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (opts   is null) throw new ArgumentNullException(nameof(opts));
        if (opts.WallThickness_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(opts),
                $"WallThickness_mm must be positive (got {opts.WallThickness_mm:F3}).");

        design.ValidateSelf();

        // ── 1. Sample Myring profile (metres → mm) ────────────────────────────
        double l  = design.Length_m   * 1000.0;  // mm
        double r  = design.Diameter_m * 500.0;   // mm (radius)

        double t = opts.WallThickness_mm;

        var innerProfile = new List<(double x, double ri)>(ProfileStations + 1);
        for (int i = 0; i <= ProfileStations; i++)
        {
            double x = i * l / ProfileStations;
            // RadiusAt helpers are internal in Marine.Core; Marine.Voxels has InternalsVisibleTo
            double ri = design.HullFamily switch
            {
                HullFamily.Myring =>
                    MyringFairingGeometry.RadiusAt(
                        x / 1000.0,
                        design.Length_m,
                        design.Diameter_m / 2.0,
                        design.NoseLength_m,
                        design.TailLength_m) * 1000.0,
                HullFamily.CylindricalHemi =>
                    CylHemiFairingGeometry.RadiusAt(
                        x / 1000.0,
                        design.Length_m,
                        design.Diameter_m / 2.0) * 1000.0,
                _ => throw new ArgumentOutOfRangeException(nameof(design.HullFamily)),
            };
            innerProfile.Add((x, ri));
        }

        var outerProfile = new List<(double x, double ro)>(innerProfile.Count);
        foreach (var (x, ri) in innerProfile)
            outerProfile.Add((x, ri + t));

        // ── 2. Voxel size + smoothen clamp ────────────────────────────────────
        double voxelSize_mm = opts.VoxelSize_mm > 0
            ? opts.VoxelSize_mm
            : Math.Min(t / 4.0, MarineHullBuildOptions.MaxAutoVoxelSize_mm);
        double smoothen_mm = Math.Max(0, Math.Min(opts.SmoothenRadius_mm, 0.25 * t));

        // ── 3. SDFs ───────────────────────────────────────────────────────────
        var innerImpl = new MarineProfileImplicit(innerProfile.ConvertAll(p => (p.x, p.ri)));
        var outerImpl = new MarineProfileImplicit(outerProfile.ConvertAll(p => (p.x, p.ro)));

        // ── 4. Bounding box ───────────────────────────────────────────────────
        double rMaxOuter = outerImpl.RMax;
        const float pad_mm = 2f;
        var bounds = new BBox3(
            new Vector3(-pad_mm,          -(float)rMaxOuter - pad_mm, -(float)rMaxOuter - pad_mm),
            new Vector3((float)l + pad_mm,  (float)rMaxOuter + pad_mm,  (float)rMaxOuter + pad_mm));

        // ── 5. Shell = outer − inner ──────────────────────────────────────────
        var outerSolid = LibraryScope.MakeVoxels(outerImpl, bounds);
        var innerSolid = LibraryScope.MakeVoxels(innerImpl, bounds);
        outerSolid.BoolSubtract(innerSolid);

        // ── 6. Smoothen (LPBF-safe, pitfall #1) ──────────────────────────────
        if (smoothen_mm > 0)
            outerSolid.Smoothen((float)smoothen_mm);

        // ── 7. Scalar estimates ───────────────────────────────────────────────
        double shellVolume_mm3 = 0;
        for (int i = 0; i < innerProfile.Count - 1; i++)
        {
            double x0 = innerProfile[i].x,  x1 = innerProfile[i + 1].x;
            double ri0 = innerProfile[i].ri, ri1 = innerProfile[i + 1].ri;
            double ro0 = outerProfile[i].ro, ro1 = outerProfile[i + 1].ro;
            double dx = x1 - x0;
            double a0 = Math.PI * (ro0 * ro0 - ri0 * ri0);
            double a1 = Math.PI * (ro1 * ro1 - ri1 * ri1);
            shellVolume_mm3 += 0.5 * (a0 + a1) * dx;
        }

        int matIdx = Math.Clamp(design.MaterialIndex, 0, MaterialDensity_g_cm3.Length - 1);
        double mass_g = shellVolume_mm3 * 1e-3 * MaterialDensity_g_cm3[matIdx];

        string desc = string.Format(CultureInfo.InvariantCulture,
            "Marine AUV hull ({4}), L={0:F1} mm, D={1:F1} mm, t={2:F2} mm, voxel={3:F3} mm",
            l, design.Diameter_m * 1000.0, t, voxelSize_mm, design.HullFamily);

        return new MarineHullGeometryResult(
            Shell:           new PicoGKVoxelHandle(outerSolid),
            HullLength_mm:   l,
            HullDiameter_mm: design.Diameter_m * 1000.0,
            ShellVolume_mm3: shellVolume_mm3,
            EstimatedMass_g: mass_g,
            VoxelSize_mm:    voxelSize_mm,
            Description:     desc);
    }
}

/// <summary>
/// Concrete <see cref="IMarineVoxelGenerator"/> singleton.
/// Delegates to <see cref="MarineHullVoxelBuilder.Build"/>.
/// </summary>
public sealed class MarineVoxelGenerator : IMarineVoxelGenerator
{
    public static readonly MarineVoxelGenerator Instance = new();
    private MarineVoxelGenerator() { }

    public MarineHullGeometryResult Build(MarineDesign design, MarineHullBuildOptions options)
        => MarineHullVoxelBuilder.Build(design, options);
}
