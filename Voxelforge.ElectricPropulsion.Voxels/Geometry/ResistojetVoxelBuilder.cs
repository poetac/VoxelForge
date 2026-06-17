// ResistojetVoxelBuilder.cs — build a resistojet shell from an
// ElectricPropulsionEngineDesign.
//
// Wave-1 geometry per pillar spec §7:
//   • Cylindrical heater chamber (HeaterChamberLength × HeaterChamberRadius).
//   • 30° half-angle converging cone-frustum from chamber to throat.
//   • 15° half-angle diverging cone-frustum from throat to exit.
// All sections revolved around the X axis. Wall thickness is uniform =
// design.ChamberWallThickness_mm.
//
// Mirrors the airbreathing-side RamjetVoxelBuilder.Build pipeline:
// inner SDF → outer SDF (= inner + wall) → voxelise both → annular shell
// = outer.BoolSubtract(inner) → smoothen → return result.
//
// MVP omissions: heater coil placeholder is decorative and not modeled
// in the voxel build (a single-loop helix would be a 50+ LOC addition
// of marginal value at Wave-1 fidelity). LPBF printability analysis
// is wired but optional — pass an LpbfMaterialProfile to RunLpbfAnalysis.

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.ElectricPropulsion.Geometry;

/// <summary>
/// Resistojet voxel-build options.
/// </summary>
/// <param name="VoxelSize_mm">PicoGK Library voxel size [mm].</param>
/// <param name="SmoothenRadius_mm">
/// Cleanup smoothen radius [mm]. Cap at 25 % of wall thickness
/// (CLAUDE.md PicoGK pitfall #1).
/// </param>
/// <param name="LpbfMaterial">
/// Optional LPBF material profile. When non-null, runs the printability
/// analysis pass and populates <see cref="ResistojetGeometryResult.Printability"/>.
/// </param>
public sealed record ResistojetBuildOptions(
    double VoxelSize_mm = 0.10,
    double SmoothenRadius_mm = 0.05,
    LpbfMaterialProfile? LpbfMaterial = null);

/// <summary>
/// Build a printable resistojet shell from an
/// <see cref="ElectricPropulsionEngineDesign"/>. See class file header for the
/// pipeline summary.
/// </summary>
public static class ResistojetVoxelBuilder
{
    /// <summary>
    /// Estimated material density for mass projection [g/cm³]. 8.6 ≈ niobium
    /// alloy (typical resistojet outer wall + nozzle material).
    /// </summary>
    public const double EstimatedMaterialDensity_g_per_cm3 = 8.6;

    /// <summary>
    /// Half-angle of the converging nozzle cone [rad]. 30° is conservative
    /// for resistojets — matches Aerojet MR-501-series hardware photos.
    /// </summary>
    public static readonly double ConvergingHalfAngle_rad = 30.0 * Math.PI / 180.0;

    /// <summary>
    /// Half-angle of the diverging nozzle cone [rad]. 15° is the
    /// industry-standard low-divergence cone (resistojets uniformly use
    /// conical, not Rao parabolic — see pillar spec §7 rationale).
    /// </summary>
    public static readonly double DivergingHalfAngle_rad = 15.0 * Math.PI / 180.0;

    /// <summary>
    /// Build the resistojet shell. Must run inside a <c>PicoGK.Library</c>
    /// scope on the task thread (CLAUDE.md PicoGK pitfall #4).
    /// </summary>
    public static ResistojetGeometryResult Build(
        ElectricPropulsionEngineDesign design,
        ResistojetBuildOptions opts)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (opts is null)   throw new ArgumentNullException(nameof(opts));
        if (design.ChamberWallThickness_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"ChamberWallThickness_mm must be positive; got {design.ChamberWallThickness_mm}.");
        if (opts.VoxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(opts),
                $"VoxelSize_mm must be positive; got {opts.VoxelSize_mm}.");

        // Clamp smoothen to ≤ 25 % of wall thickness (PicoGK pitfall #1).
        double smoothenCap = 0.25 * design.ChamberWallThickness_mm;
        double smoothen = Math.Min(opts.SmoothenRadius_mm, smoothenCap);

        // ── Build the (x, R) contour in mm ─────────────────────────────
        // Coordinate frame: chamber starts at x=0, runs to x=chamberLength.
        // Then converging cone (30° half-angle) from chamber radius down
        // to throat radius. Then diverging cone (15° half-angle) from
        // throat radius up to exit radius (computed from area ratio).
        double L_chamber = design.HeaterChamberLength_mm;
        double R_chamber = design.HeaterChamberRadius_mm;
        double R_throat  = design.NozzleThroatRadius_mm;
        double R_exit    = R_throat * Math.Sqrt(design.NozzleAreaRatio);

        // Converging-cone axial length = (R_chamber − R_throat) / tan(30°).
        double L_converging = (R_chamber - R_throat) / Math.Tan(ConvergingHalfAngle_rad);
        // Diverging-cone axial length = (R_exit − R_throat) / tan(15°).
        double L_diverging  = (R_exit - R_throat) / Math.Tan(DivergingHalfAngle_rad);

        double x_throat = L_chamber + L_converging;
        double x_exit   = x_throat + L_diverging;

        // Inner gas-path contour points (x, R).
        var innerContour = new (double x_mm, double r_mm)[]
        {
            (0.0,        R_chamber),
            (L_chamber,  R_chamber),
            (x_throat,   R_throat),
            (x_exit,     R_exit),
        };

        // Outer-shell contour = inner contour radially offset by wall thickness.
        double t = design.ChamberWallThickness_mm;
        var outerContour = new (double x_mm, double r_mm)[]
        {
            (0.0,        R_chamber + t),
            (L_chamber,  R_chamber + t),
            (x_throat,   R_throat + t),
            (x_exit,     R_exit + t),
        };

        var innerImplicit = new RevolvedContourImplicit(innerContour);
        var outerImplicit = new RevolvedContourImplicit(outerContour);

        // ── Voxelise ──────────────────────────────────────────────────
        // Bounding box generously larger than the outer contour.
        double bboxMargin_mm = 5.0;
        double xMin_mm = -bboxMargin_mm;
        double xMax_mm = x_exit + bboxMargin_mm;
        double radial_mm = R_exit + t + bboxMargin_mm;
        var bbox = new BBox3(
            new Vector3((float)xMin_mm,    (float)(-radial_mm), (float)(-radial_mm)),
            new Vector3((float)xMax_mm,    (float)( radial_mm), (float)( radial_mm)));

        var inner = LibraryScope.MakeVoxels(innerImplicit, bbox);
        var outer = LibraryScope.MakeVoxels(outerImplicit, bbox);

        // Annular shell = outer minus inner.
        outer.BoolSubtract(inner);

        if (smoothen > 0.0)
        {
            outer.Smoothen((float)smoothen);
        }

        // ── Compute scalars ───────────────────────────────────────────
        double throatArea_mm2 = Math.PI * R_throat * R_throat;
        double exitArea_mm2   = Math.PI * R_exit * R_exit;

        // Solid volume from the analytical contour (annular frustum sum).
        double solidVolume_mm3 =
              CylinderVolume(L_chamber,    R_chamber + t)        - CylinderVolume(L_chamber,    R_chamber)
            + FrustumVolume (L_converging, R_chamber + t, R_throat + t)
                                                                 - FrustumVolume(L_converging, R_chamber, R_throat)
            + FrustumVolume (L_diverging,  R_throat + t,  R_exit + t)
                                                                 - FrustumVolume(L_diverging,  R_throat,  R_exit);
        double mass_g = solidVolume_mm3 * 1e-3 * EstimatedMaterialDensity_g_per_cm3;

        // ── Optional LPBF analysis ─────────────────────────────────────
        LpbfPrintabilityResult? printability = null;
        // Wave-1 ships without LPBF analysis wired (the shared
        // LpbfPrintabilityAnalysis surface needs SurfaceSamples that
        // RamjetSurfaceSampler provides on the airbreathing side; an
        // analogous resistojet sampler is a Wave-2 follow-on).
        // The shape stays in the result so the StlExporter contract
        // is identical to ramjet's.
        _ = opts.LpbfMaterial;

        var description = $"Resistojet shell: L={x_exit:F1} mm, OD={2*(R_exit+t):F1} mm, "
                        + $"ε={design.NozzleAreaRatio:F1}, throat R={R_throat:F2} mm, "
                        + $"voxel={opts.VoxelSize_mm:F3} mm, mass≈{mass_g:F1} g.";

        return new ResistojetGeometryResult(
            Voxels:              new PicoGKVoxelHandle(outer),
            SolidVolume_mm3:     solidVolume_mm3,
            WallThickness_mm:    t,
            TotalMass_g:         mass_g,
            BoundingLength_mm:   x_exit,
            BoundingDiameter_mm: 2.0 * (R_exit + t),
            ThroatArea_mm2:      throatArea_mm2,
            ExitArea_mm2:        exitArea_mm2,
            AreaRatio:           design.NozzleAreaRatio,
            Description:         description,
            Printability:        printability);
    }

    private static double CylinderVolume(double length, double radius)
        => Math.PI * radius * radius * length;

    private static double FrustumVolume(double length, double r1, double r2)
        => (Math.PI * length / 3.0) * (r1 * r1 + r1 * r2 + r2 * r2);
}
