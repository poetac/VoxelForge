// TankageVoxelBuilder.cs — Sprint A.70 (C.2) cylindrical pressure-vessel
// voxel builder. Framing-B Phase 3 voxel-pipeline backfill on the Tankage
// pillar (Wave-1 internal namespace `Voxelforge.Tankage`, algebraic-only
// since TANK.W1 / TANK.W2; this is the first geometry surface).
//
// Generates an axisymmetric pressure-vessel body from a
// PressureVesselDesign:
//
//   ── Topology ──
//   Cylindrical body from x = -L/2 to x = +L/2, outer radius
//   R_outer = R_internal + t.
//   With HasHemisphericalEndCaps = true, two hemispherical caps of outer
//   radius R_outer extend the axial envelope by R_outer on each side
//   (overall length = L + 2·R_outer).
//   Without end caps the vessel is a bare cylinder of length L.
//
//   ── Hollow vs solid voxel body ──
//   The voxel body is the SOLID OUTER ENVELOPE. PicoGK 2.0.0 does not
//   represent fully-enclosed cavities — its voxelizer flood-fills any
//   region of zero-or-negative-SDF enclosed by a closed surface
//   (verified during A.70 with two coaxial voxSphere primitives:
//   outer.BoolSubtract(inner) is a no-op when inner is strictly nested
//   inside outer; same for Offset(-wall) duplicates of the outer).
//
//   For the cylinder-only case (HasHemisphericalEndCaps == false) the
//   axially-open ends DO let the voxelizer represent the hollow cavity,
//   and we use <see cref="Voxelforge.Geometry.AnnulusImplicit"/>
//   directly. This is the same pattern as
//   <see cref="Voxelforge.Flywheel.FlywheelVoxelBuilder"/>'s ThinRim.
//
//   For the with-caps case we voxelise the SOLID envelope (outer
//   capsule). Downstream LPBF preparation can shell the body via
//   PicoGK's mesh-based shelling operators or via a subsequent voxel
//   pipeline pass once PicoGK gains true closed-cavity support. The
//   <see cref="TankageGeometryResult"/> record reports both the outer
//   and internal radii so the design intent is preserved for downstream
//   mass / volume / printability calculations.
//
//   ── Solid-vs-shell mass note ──
//   Voxel-derived mass via ρ · V_voxel matches the SOLID envelope mass
//   when HasHemisphericalEndCaps is true. To recover the shell mass,
//   use <see cref="PressureVesselSolver.Solve(PressureVesselDesign)"/>
//   and read <c>result.ShellMass_kg</c>.
//
//   ── Wall-safe smoothing (PicoGK pitfall #1) ──
//   Smoothen(d) destroys features < 2d. The shell wall is the thinnest
//   feature; we cap d at 25 % of min(WallThickness, R_outer) and only
//   smoothen if d ≥ 0.02 mm (consistent with FlywheelVoxelBuilder /
//   ChamberVoxelBuilder).
//
//   ── Validation surface ──
//   PressureVesselDesign.ValidateSelf() throws on non-positive radius /
//   length / thickness / pressure, on TankShellType.None, and on R/t < 10
//   (the thin-wall validity envelope — thick-wall Lamé physics deferred
//   to TANK.W2). The voxel builder propagates these.
//
// Coordinate convention: +X is the cylinder axis. The cylindrical portion
// runs from x = -L/2 to x = +L/2; with end caps the body extends from
// x = -(L/2 + R_outer) to x = +(L/2 + R_outer). Always centred on the
// origin so STL export produces a symmetric body.

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.Tankage;

/// <summary>
/// PicoGK voxel builder for a cylindrical pressure vessel
/// (Sprint A.70 / C.2). Companion to <see cref="PressureVesselSolver"/> —
/// turns a <see cref="PressureVesselDesign"/> into a voxel body (hollow
/// cylinder shell when HasHemisphericalEndCaps is false; solid outer
/// envelope when HasHemisphericalEndCaps is true; see file header for
/// the PicoGK 2.0.0 closed-cavity limitation).
/// </summary>
internal static class TankageVoxelBuilder
{
    /// <summary>
    /// Wall-safe smoothing radius cap per PicoGK pitfall #1
    /// (<c>Smoothen(d)</c> destroys features &lt; 2d). 25 % of the
    /// minimum feature thickness keeps the shell wall intact.
    /// </summary>
    internal const double SmoothingFeatureFraction = 0.25;

    /// <summary>
    /// Build the pressure-vessel voxel body for <paramref name="design"/>.
    /// </summary>
    /// <param name="design">Validated pressure-vessel design record. Must
    ///   satisfy <see cref="PressureVesselDesign.ValidateSelf"/>.</param>
    /// <param name="voxelSize_mm">PicoGK voxel grid size in mm. Used only
    ///   for the wall-safe smoothing cap and the bounding-box padding.
    ///   The caller is responsible for constructing the ambient
    ///   <c>Library</c> at the matching voxel size.</param>
    /// <returns>Geometry summary + voxel handle.</returns>
    /// <exception cref="ArgumentNullException">design is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">voxelSize_mm is
    ///   non-positive.</exception>
    /// <exception cref="ArgumentException">design fails ValidateSelf —
    ///   propagates from the design record (R/t &lt; 10, non-positive
    ///   dimensions, TankShellType.None).</exception>
    internal static TankageGeometryResult Build(PressureVesselDesign design, double voxelSize_mm)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (double.IsNaN(voxelSize_mm) || voxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(voxelSize_mm),
                $"voxelSize_mm={voxelSize_mm:F4} must be > 0.");
        design.ValidateSelf();

        // ── 1. Resolve dimensional fields (millimetres) ───────────────
        double Rinner_mm  = design.InternalRadius_m * 1000.0;
        double wall_mm    = design.WallThickness_m  * 1000.0;
        double Router_mm  = Rinner_mm + wall_mm;
        double L_mm       = design.ShellLength_m    * 1000.0;
        double halfL_mm   = 0.5 * L_mm;
        bool   hasCaps    = design.HasHemisphericalEndCaps;

        // Overall axial length: L (cylinder only) or L + 2·R_outer.
        double overall_mm = hasCaps ? L_mm + 2.0 * Router_mm : L_mm;

        // ── 2. Bounding box (axisymmetric, centred on origin in x) ────
        double halfAxial_mm = 0.5 * overall_mm;
        float pad_mm   = (float)Math.Max(2.0 * voxelSize_mm, 1.0);
        float halfA_f  = (float)halfAxial_mm;
        float Router_f = (float)Router_mm;
        float Rinner_f = (float)Rinner_mm;
        float halfL_f  = (float)halfL_mm;
        var bounds = new BBox3(
            new Vector3(-halfA_f - pad_mm, -Router_f - pad_mm, -Router_f - pad_mm),
            new Vector3( halfA_f + pad_mm,  Router_f + pad_mm,  Router_f + pad_mm));

        // ── 3. Build the body ─────────────────────────────────────────
        Voxels body;
        if (hasCaps)
        {
            // Solid outer-envelope capsule: cylinder + two spherical
            // caps. The cylinder is built via AnnulusImplicit (rInner=0
            // → degenerate solid cylinder), and the two end caps via
            // PicoGK's voxSphere primitive. The three pieces are then
            // unioned via BoolAdd. PicoGK does NOT represent the
            // resulting closed cavity as hollow even if an SDF
            // formulation tries to encode one (see file-header note);
            // the result here is the SOLID outer envelope.
            var cylinderImpl = new AnnulusImplicit(
                xMin:   -halfL_f,
                xMax:    halfL_f,
                rInner:  0f,
                rOuter:  Router_f);
            body = LibraryScope.MakeVoxels(cylinderImpl, bounds);

            Voxels leftSphere  = MakeSphereAware(new Vector3(-halfL_f, 0f, 0f), Router_f);
            Voxels rightSphere = MakeSphereAware(new Vector3( halfL_f, 0f, 0f), Router_f);
            body.BoolAdd(leftSphere);
            body.BoolAdd(rightSphere);
        }
        else
        {
            // Cylinder-only: HOLLOW shell via AnnulusImplicit. The
            // axially-open ends let the PicoGK voxelizer represent the
            // cavity correctly (no closed-cavity flood-fill).
            var annulusImpl = new AnnulusImplicit(
                xMin:   -halfL_f,
                xMax:    halfL_f,
                rInner:  Rinner_f,
                rOuter:  Router_f);
            body = LibraryScope.MakeVoxels(annulusImpl, bounds);
        }

        // ── 4. Wall-safe smoothing (PicoGK pitfall #1) ────────────────
        // Smoothen(d) destroys features < 2d → cap at 25 % of the
        // thinnest dimension. The shell wall is the thinnest feature
        // (e.g. Falcon-9-class 4.78 mm wall, R_outer 1835 mm → min-feature
        // = wall → safe smoothing ≤ 1.2 mm). Skip below 0.02 mm (sub-voxel
        // noise floor).
        double minFeature_mm = Math.Min(wall_mm, Router_mm);
        double safeSmooth_mm = SmoothingFeatureFraction * minFeature_mm;
        if (safeSmooth_mm >= 0.02)
            body.Smoothen((float)safeSmooth_mm);

        return new TankageGeometryResult(
            OuterRadius_mm:     Router_mm,
            InternalRadius_mm:  Rinner_mm,
            WallThickness_mm:   wall_mm,
            ShellLength_mm:     L_mm,
            OverallLength_mm:   overall_mm,
            HasEndCaps:         hasCaps,
            Voxels:             new PicoGKVoxelHandle(body));
    }

    /// <summary>
    /// Library-aware wrapper for <c>Voxels.voxSphere</c>: routes through
    /// the explicit-Library overload when an ambient
    /// <see cref="LibraryScope"/> is set (headless subprocess /
    /// xUnit-with-LibraryScope path) and falls back to the global-
    /// singleton overload otherwise (interactive / <c>Library.Go()</c>).
    /// Same dual-path policy as <see cref="LibraryScope.MakeVoxels"/>.
    /// </summary>
    private static Voxels MakeSphereAware(Vector3 center, float radius_mm)
        => LibraryScope.Current is { } lib
            ? Voxels.voxSphere(lib, center, radius_mm)
            : Voxels.voxSphere(center, radius_mm);
}
