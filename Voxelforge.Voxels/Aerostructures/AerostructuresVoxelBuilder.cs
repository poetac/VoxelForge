// AerostructuresVoxelBuilder.cs — Sprint A.82 (C.2) wing-spar voxel
// builder. Framing-B Phase 3 voxel-pipeline backfill on the Aerostructures
// pillar (Wave-1 internal namespace `Voxelforge.Aerostructures`,
// algebraic-only since AS.W1 / AS.W2; this is the first geometry surface).
//
// Generates a prismatic wing-spar body from a WingSparDesign. The spar is
// a long beam extruded along +X (the spanwise direction); the structural
// cross-section sits in the YZ plane:
//
//   +X  = span (root → tip; the spar is centred about x = 0,
//                running from x = -L/2 to x = +L/2)
//   +Y  = chord direction (OuterWidth — "b")
//   +Z  = chord-normal     (OuterHeight — "h")
//
//   ── Topology by section type (per SparSectionType) ────────────────────
//
//   SolidRectangular     Solid b × h cuboid extruded along X. Built via
//                        a single BoxImplicit.
//
//   HollowRectangularBox 4-plate box section: two horizontal flanges (top
//                        + bottom, b × t × L) and two vertical webs
//                        (t × (h - 2t) × L) unioned via BoolAdd. Both
//                        SPANWISE ends are OPEN (no end caps in the
//                        design surface), so the cavity renders correctly
//                        — the closed-cavity flood-fill limitation that
//                        bit A.70 Tankage does NOT apply here (PicoGK
//                        2.0.0 only flood-fills regions enclosed by a
//                        fully-closed surface; the open ±X ends sidestep
//                        it). Same fundamental fix as A.70's
//                        cylinder-only branch (`AnnulusImplicit` with
//                        `rInner > 0`). The voxel body is therefore the
//                        HOLLOW box shell — IsHollowVoxelBody = true.
//                        Mass-recovery via ρ · V_voxel matches the
//                        closed-form shell mass to ~ ±20 % at 1 mm voxel
//                        (wall-quantisation band per the Tankage
//                        cylinder-only A.70 pattern).
//
//   SolidCircular        Solid cylindrical boom, axis along X, radius
//                        h/2 (the WingSparDesign convention: h is
//                        reinterpreted as 2·R for circular sections;
//                        b is ignored). Built via CylinderImplicit.
//
//   ── Wall-safe smoothing (PicoGK pitfall #1) ──────────────────────────
//
//   Smoothen(d) destroys features < 2d → cap d at 25 % of the thinnest
//   feature dimension. The thinnest feature is the wall thickness for
//   HollowRectangularBox (e.g. Cessna 172 6 mm wall → safe ≤ 1.5 mm), or
//   min(b, h) for solid sections. Skip below 0.02 mm (sub-voxel noise
//   floor), consistent with TankageVoxelBuilder / FlywheelVoxelBuilder.
//
//   ── SIMP / lightening-cut note ───────────────────────────────────────
//
//   The umbrella issue #647 sketches a SIMP density-driven internal
//   lightening cut for SIMP-aware spars. The Wave-1 WingSparDesign
//   record does NOT expose a SIMP density field or hint; adding one
//   would expand the Aerostructures public-surface beyond the Wave-1
//   header policy. This sprint ships envelope-only geometry (matching
//   the design record literally); SIMP-driven internal pockets are
//   deferred to a follow-on issue. The hollow-box section already
//   delivers the dominant lightweighting win (the cavity); a future
//   sprint can add SIMP-driven pocket / lattice infill via a separate
//   design-surface extension.
//
//   ── Validation surface ───────────────────────────────────────────────
//
//   WingSparDesign.ValidateSelf() throws on:
//     - SparSectionType.None / SparMaterial.None sentinels
//     - non-positive HalfSpan_m / OuterHeight_m
//     - non-positive OuterWidth_m for non-circular sections
//     - non-positive WallThickness_m for hollow sections
//     - WallThickness_m ≥ half of the smaller outer dimension
//     - non-positive DistributedLift_Nm / LoadFactor
//   The voxel builder propagates these so a malformed design can't
//   sneak through.

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.Aerostructures;

/// <summary>
/// PicoGK voxel builder for a cantilevered wing spar (Sprint A.82 / C.2).
/// Companion to <see cref="WingSparSolver"/> — turns a
/// <see cref="WingSparDesign"/> into a printable prismatic voxel body
/// (solid cuboid / cylinder for solid sections, 4-plate hollow shell
/// for HollowRectangularBox).
/// </summary>
internal static class AerostructuresVoxelBuilder
{
    /// <summary>
    /// Wall-safe smoothing radius cap per PicoGK pitfall #1
    /// (<c>Smoothen(d)</c> destroys features &lt; 2d). 25 % of the
    /// minimum feature thickness keeps the shell wall intact.
    /// </summary>
    internal const double SmoothingFeatureFraction = 0.25;

    /// <summary>
    /// Build the wing-spar voxel body for <paramref name="design"/>.
    /// </summary>
    /// <param name="design">Validated wing-spar design record. Must satisfy
    ///   <see cref="WingSparDesign.ValidateSelf"/>.</param>
    /// <param name="voxelSize_mm">PicoGK voxel grid size in mm. Used only
    ///   for the wall-safe smoothing cap and the bounding-box padding.
    ///   The caller is responsible for constructing the ambient
    ///   <c>Library</c> at the matching voxel size.</param>
    /// <returns>Wing-spar geometry summary + voxel handle.</returns>
    /// <exception cref="ArgumentNullException">design is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">voxelSize_mm is
    ///   non-positive.</exception>
    /// <exception cref="ArgumentException">design fails ValidateSelf —
    ///   propagates from the design record (None sentinels, non-positive
    ///   dimensions, oversize wall).</exception>
    internal static AerostructuresGeometryResult Build(
        WingSparDesign design,
        double         voxelSize_mm)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (double.IsNaN(voxelSize_mm) || voxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(voxelSize_mm),
                $"voxelSize_mm={voxelSize_mm:F4} must be > 0.");
        design.ValidateSelf();

        // ── 1. Resolve dimensional fields (millimetres) ───────────────
        double L_mm     = design.HalfSpan_m     * 1000.0;
        double h_mm     = design.OuterHeight_m  * 1000.0;
        double b_mm     = design.OuterWidth_m   * 1000.0;
        double t_mm     = design.WallThickness_m * 1000.0;
        double halfL_mm = 0.5 * L_mm;

        // For SolidCircular the design reinterprets h as 2·R; the cross-
        // section is a circle of radius h/2 and "width" b is ignored.
        // Use h as both the effective width and the diameter so the
        // bounding-box / result-record fields stay consistent.
        bool isCircular = design.SectionType == SparSectionType.SolidCircular;
        if (isCircular) b_mm = h_mm;  // diameter == height for the result record

        // ── 2. Bounding box (prism, centred on origin) ────────────────
        float halfL_f = (float)halfL_mm;
        float halfH_f = (float)(0.5 * h_mm);
        float halfB_f = (float)(0.5 * b_mm);
        float pad_mm  = (float)Math.Max(2.0 * voxelSize_mm, 1.0);
        var bounds = new BBox3(
            new Vector3(-halfL_f - pad_mm, -halfB_f - pad_mm, -halfH_f - pad_mm),
            new Vector3( halfL_f + pad_mm,  halfB_f + pad_mm,  halfH_f + pad_mm));

        // ── 3. Build the body by section type ─────────────────────────
        Voxels body;
        string sectionDescription;
        bool   isHollow;
        switch (design.SectionType)
        {
            case SparSectionType.SolidRectangular:
            {
                // Single solid cuboid b × h × L (extruded along X).
                var solidImpl = new BoxImplicit(
                    new Vector3(-halfL_f, -halfB_f, -halfH_f),
                    new Vector3( halfL_f,  halfB_f,  halfH_f));
                body = LibraryScope.MakeVoxels(solidImpl, bounds);
                sectionDescription =
                    $"SolidRectangular {h_mm:F0} mm × {b_mm:F0} mm × {L_mm:F0} mm span";
                isHollow = false;
                break;
            }

            case SparSectionType.HollowRectangularBox:
            {
                // Four-plate box section, OPEN at both spanwise ends.
                // The open ±X ends let PicoGK's voxelizer render the
                // hollow cavity correctly (no closed-cavity flood-fill).
                // Plate layout (X-extruded):
                //   topFlange    = box [b × t] at +Z (top of section)
                //   bottomFlange = box [b × t] at -Z (bottom)
                //   leftWeb      = box [t × (h - 2t)] at -Y
                //   rightWeb     = box [t × (h - 2t)] at +Y
                // The web height (h - 2t) excludes the flange thickness
                // so the four plates don't double-count at the corners.
                float t_f       = (float)t_mm;
                float topZmin   = halfH_f - t_f;
                float bottomZmax = -halfH_f + t_f;
                float leftYmax  = -halfB_f + t_f;
                float rightYmin =  halfB_f - t_f;
                float webZmin   = bottomZmax;   // = -halfH + t
                float webZmax   = topZmin;      // = +halfH - t

                var topFlange = new BoxImplicit(
                    new Vector3(-halfL_f, -halfB_f,  topZmin),
                    new Vector3( halfL_f,  halfB_f,  halfH_f));
                var bottomFlange = new BoxImplicit(
                    new Vector3(-halfL_f, -halfB_f, -halfH_f),
                    new Vector3( halfL_f,  halfB_f,  bottomZmax));
                var leftWeb = new BoxImplicit(
                    new Vector3(-halfL_f, -halfB_f,  webZmin),
                    new Vector3( halfL_f,  leftYmax, webZmax));
                var rightWeb = new BoxImplicit(
                    new Vector3(-halfL_f,  rightYmin, webZmin),
                    new Vector3( halfL_f,  halfB_f,   webZmax));

                var unionImpl = new UnionImplicit(
                    topFlange, bottomFlange, leftWeb, rightWeb);
                body = LibraryScope.MakeVoxels(unionImpl, bounds);
                sectionDescription =
                    $"HollowRectangularBox {h_mm:F0} mm × {b_mm:F0} mm × {t_mm:F2} mm wall";
                isHollow = true;
                break;
            }

            case SparSectionType.SolidCircular:
            {
                // Solid cylinder, axis along +X, radius h/2, length L.
                // CylinderImplicit takes (start, direction, radius, length);
                // start at x = -L/2 + the +X axis sweeps to x = +L/2.
                var cylImpl = new CylinderImplicit(
                    start:     new Vector3(-halfL_f, 0f, 0f),
                    direction: new Vector3(1f, 0f, 0f),
                    radius:    halfH_f,            // R = h/2
                    length:    (float)L_mm);
                body = LibraryScope.MakeVoxels(cylImpl, bounds);
                sectionDescription =
                    $"SolidCircular Ø{h_mm:F0} mm × {L_mm:F0} mm span";
                isHollow = false;
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(design), design.SectionType,
                    $"AerostructuresVoxelBuilder does not yet support section type "
                  + $"'{design.SectionType}'.");
        }

        // ── 4. Wall-safe smoothing (PicoGK pitfall #1) ────────────────
        // Smoothen(d) destroys features < 2d → cap at 25 % of the
        // thinnest feature dimension. For HollowRectangularBox the
        // thinnest feature is the wall thickness (e.g. Cessna 172 6 mm
        // wall → safe smoothing ≤ 1.5 mm). For solid sections it's
        // min(b, h). Skip below 0.02 mm (sub-voxel noise floor).
        double minFeature_mm = design.SectionType == SparSectionType.HollowRectangularBox
            ? t_mm
            : Math.Min(b_mm, h_mm);
        double safeSmooth_mm = SmoothingFeatureFraction * minFeature_mm;
        if (safeSmooth_mm >= 0.02)
            body.Smoothen((float)safeSmooth_mm);

        return new AerostructuresGeometryResult(
            SectionType:        design.SectionType,
            HalfSpan_mm:        L_mm,
            OuterHeight_mm:     h_mm,
            OuterWidth_mm:      b_mm,
            WallThickness_mm:   design.SectionType == SparSectionType.HollowRectangularBox
                                    ? t_mm
                                    : 0.0,
            SectionDescription: sectionDescription,
            IsHollowVoxelBody:  isHollow,
            Voxels:             new PicoGKVoxelHandle(body));
    }
}
