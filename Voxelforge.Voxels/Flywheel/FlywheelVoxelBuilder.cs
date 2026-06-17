// FlywheelVoxelBuilder.cs — Sprint A.67 (C.2) flywheel-rotor voxel
// builder. Framing-B Phase 3 voxel-pipeline backfill on the Flywheel
// pillar (Wave-1 internal namespace `Voxelforge.Flywheel`, algebraic-only
// since FW.W1 / FW.W2; this is the first geometry surface).
//
// Generates an axisymmetric body of revolution from a FlywheelDesign:
//
//   ── Rotor topology (driven by design.Shape) ──
//   ThinRim     annulus from R_i = (1 - rimFraction) · R_o to R_o.
//               rimFraction = 0.1 (cluster-mid anchor for grid-scale
//               composite rotors — Beacon Power Smart Energy 25,
//               Active Power CleanSource — typically have rim wall
//               between 8 % and 15 % of R_o; 0.1 sits dead-centre).
//   SolidDisk   full disc, R_i = 0.
//
//   ── Axial thickness (mass-consistent) ──
//   t = m / (ρ · A) where A is the in-plane cross-section area of the
//   rotor (π · R_o² for SolidDisk; π · (R_o² − R_i²) for ThinRim) and ρ
//   is the material density from FlywheelMaterialRegistry. The bore
//   subtraction at R_shaft = 0.05 · R_o leaves a small residual mass
//   mismatch — for ThinRim this is negligible because R_shaft (= 25 mm
//   at R_o = 500 mm) sits well inside R_i (= 450 mm). For SolidDisk the
//   bore takes 0.25 % of cross-section area, comfortably under the
//   ±10 % mass-consistency band tested by the voxel test suite.
//
//   ── Central hub bore ──
//   A cylindrical void at R_shaft = 0.05 · R_o. Conservative cluster
//   anchor — real shaft diameters for utility-scale flywheels span
//   ~ 30 - 80 mm on a 1 m rotor; 50 mm @ R_o = 500 mm is mid-band.
//
//   ── Smoothing cap (PicoGK pitfall #1) ──
//   Smoothen(d) destroys features < 2d. The thinnest feature is
//   min(rimWallThickness, axialThickness, shaftBoreRadius); we cap d at
//   25 % of that floor and only smoothen if d ≥ 0.02 mm (consistent
//   with ChamberVoxelBuilder).
//
// Coordinate convention: +X is the rotor spin axis. The rotor face
// sits at x = -t/2 .. +t/2 (centred on the origin) so STL export
// always produces a symmetric body.

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.Flywheel;

/// <summary>
/// PicoGK voxel builder for a flywheel rotor disc / thin rim
/// (Sprint A.67 / C.2). Companion to <see cref="FlywheelSolver"/> —
/// turns a <see cref="FlywheelDesign"/> into a printable axisymmetric
/// voxel body.
/// </summary>
internal static class FlywheelVoxelBuilder
{
    /// <summary>
    /// Default rim-wall fraction for <see cref="FlywheelShape.ThinRim"/>:
    /// R_o − R_i = rimFraction · R_o. Cluster-mid anchor for grid-scale
    /// composite rotors (Beacon Power Smart Energy 25, Active Power
    /// CleanSource). Outside the 0.05 - 0.20 cluster band, the rim
    /// stops being "thin" or becomes a foil.
    /// </summary>
    internal const double DefaultRimFraction = 0.10;

    /// <summary>
    /// Shaft-bore radius as a fraction of <c>OuterRadius_m</c>. Sized
    /// for a utility-scale rotor (50 mm shaft on a 1 m rotor); small
    /// enough that the bore subtraction is mass-negligible for ThinRim
    /// and only ~ 0.25 % of cross-section for SolidDisk.
    /// </summary>
    internal const double ShaftBoreFractionOfOuterRadius = 0.05;

    /// <summary>
    /// Wall-safe smoothing radius cap per PicoGK pitfall #1
    /// (<c>Smoothen(d)</c> destroys features &lt; 2d). 25 % of the
    /// minimum feature thickness keeps the rim wall, axial thickness
    /// and shaft bore intact.
    /// </summary>
    internal const double SmoothingFeatureFraction = 0.25;

    /// <summary>
    /// Build the rotor voxel body for <paramref name="design"/>.
    /// </summary>
    /// <param name="design">Validated flywheel design record. Must satisfy
    ///   <see cref="FlywheelDesign.ValidateSelf"/>.</param>
    /// <param name="voxelSize_mm">PicoGK voxel grid size in mm. Used only
    ///   for the wall-safe smoothing cap and the bounding-box padding.
    ///   The caller is responsible for constructing the ambient
    ///   <c>Library</c> at the matching voxel size.</param>
    /// <returns>Mass-consistent rotor geometry summary + voxel handle.</returns>
    /// <exception cref="ArgumentNullException">design is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">voxelSize_mm is
    ///   non-positive, or design.OuterRadius_m / Mass_kg are non-positive
    ///   (propagated from ValidateSelf).</exception>
    internal static FlywheelGeometryResult Build(FlywheelDesign design, double voxelSize_mm)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (double.IsNaN(voxelSize_mm) || voxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(voxelSize_mm),
                $"voxelSize_mm={voxelSize_mm:F4} must be > 0.");
        design.ValidateSelf();

        // ── 1. Resolve dimensional fields ─────────────────────────────
        double Ro_mm = design.OuterRadius_m * 1000.0;
        double Ri_mm = design.Shape switch
        {
            FlywheelShape.ThinRim   => Ro_mm * (1.0 - DefaultRimFraction),
            FlywheelShape.SolidDisk => 0.0,
            _ => throw new ArgumentOutOfRangeException(nameof(design), design.Shape,
                    $"FlywheelVoxelBuilder does not yet support shape '{design.Shape}'."),
        };
        double rimWall_mm = Ro_mm - Ri_mm;

        // Mass-consistent axial thickness: t = m / (ρ · A_in_plane).
        // FlywheelMaterialRegistry.For(...) is exposed via the Core
        // InternalsVisibleTo grant to Voxelforge.Voxels.
        double rho_kg_m3 = FlywheelMaterialRegistry.For(design.Material).Density_kgm3;
        double Ro_m = design.OuterRadius_m;
        double Ri_m = Ri_mm / 1000.0;
        double area_m2 = Math.PI * (Ro_m * Ro_m - Ri_m * Ri_m);
        double t_m  = design.Mass_kg / (rho_kg_m3 * area_m2);
        double t_mm = t_m * 1000.0;

        // ── 2. Hub-bore radius ────────────────────────────────────────
        double Rshaft_mm = Ro_mm * ShaftBoreFractionOfOuterRadius;

        // ── 3. Bounding box (axisymmetric, centred on origin in x) ────
        float halfT_mm = (float)(0.5 * t_mm);
        float pad_mm   = (float)Math.Max(2.0 * voxelSize_mm, 1.0);
        var bounds = new BBox3(
            new Vector3(-halfT_mm - pad_mm, -(float)Ro_mm - pad_mm, -(float)Ro_mm - pad_mm),
            new Vector3( halfT_mm + pad_mm,  (float)Ro_mm + pad_mm,  (float)Ro_mm + pad_mm));

        // ── 4. Rotor body: DiscImplicit for solid, AnnulusImplicit for thin rim ──
        // Both implicits use the +X = rotor spin axis convention.
        // DiscImplicit ctor: (xStart, thickness, radius) — built around x = -t/2.
        // AnnulusImplicit ctor: (xMin, xMax, rInner, rOuter).
        Voxels rotor;
        if (Ri_mm <= 0.0)
        {
            // Solid disc.
            var discImpl = new DiscImplicit(
                xStart_mm:    -halfT_mm,
                thickness_mm: (float)t_mm,
                radius_mm:    (float)Ro_mm);
            rotor = LibraryScope.MakeVoxels(discImpl, bounds);
        }
        else
        {
            // Thin annular rim.
            var annulusImpl = new AnnulusImplicit(
                xMin:   -halfT_mm,
                xMax:    halfT_mm,
                rInner: (float)Ri_mm,
                rOuter: (float)Ro_mm);
            rotor = LibraryScope.MakeVoxels(annulusImpl, bounds);
        }

        // ── 5. Subtract central shaft bore ────────────────────────────
        // Bore length extends past the rotor faces so the through-hole
        // is clean. Length = t + 2 mm clearance.
        float boreLength_mm = (float)t_mm + 2.0f;
        var boreImpl = new CylinderImplicit(
            start:     new Vector3(-halfT_mm - 1.0f, 0f, 0f),
            direction: new Vector3(1f, 0f, 0f),
            radius:    (float)Rshaft_mm,
            length:    boreLength_mm);
        var boreVox = LibraryScope.MakeVoxels(boreImpl, bounds);
        rotor.BoolSubtract(boreVox);

        // ── 6. Wall-safe smoothing (PicoGK pitfall #1) ────────────────
        // Smoothen(d) destroys features < 2d → cap at 25 % of the
        // thinnest feature dimension. For a 1025 kg / R_o = 500 mm
        // ThinRim carbon-fibre rotor: rimWall = 50 mm, t ≈ 14 mm,
        // R_shaft = 25 mm; min-feature ≈ 14 mm → safe smoothing
        // ≤ 3.5 mm. Skip below 0.02 mm (sub-voxel noise floor).
        double minFeature_mm = Math.Min(Math.Min(rimWall_mm, t_mm), Rshaft_mm);
        double safeSmooth_mm = SmoothingFeatureFraction * minFeature_mm;
        if (safeSmooth_mm >= 0.02)
            rotor.Smoothen((float)safeSmooth_mm);

        return new FlywheelGeometryResult(
            OuterRadius_mm:      Ro_mm,
            InnerRadius_mm:      Ri_mm,
            AxialThickness_mm:   t_mm,
            ShaftBoreRadius_mm:  Rshaft_mm,
            RimWallThickness_mm: rimWall_mm,
            Voxels:              new PicoGKVoxelHandle(rotor));
    }
}
