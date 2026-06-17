// HeatPipeVoxelBuilder.cs — Sprint A.80 (C.2) heat-pipe voxel builder.
// Framing-B Phase 3 voxel-pipeline backfill on the HeatPipe pillar
// (Wave-1 internal namespace `Voxelforge.HeatPipe`, algebraic-only since
// HP.W1 / HP.W2; this is the first geometry surface).
//
// Generates an axisymmetric heat-pipe body from a HeatPipeDesign:
//
//   ── Topology (three concentric radial bands) ──
//   Vapour core   : open central cavity, diameter D = design.InternalDiameter_m.
//                   Carries the vapour-phase return path.
//   Wick annulus  : thin radial layer outside the vapour core. Thickness
//                   = WickThicknessFraction · D (cluster-anchor 10 % of
//                   vapour-core diameter — typical Cu-water sintered wick
//                   on a 6 mm pipe is 0.5–0.8 mm; SAFE-400 Li-W annular
//                   screen wick on a 14 mm pipe is ~ 1.2–1.5 mm; both
//                   land in the 8–12 % band).
//                   The wick OD coincides with the envelope ID.
//   Envelope wall : outer cylindrical shell. Thickness
//                   = EnvelopeWallThicknessFraction · D (cluster-anchor
//                   7.5 % of vapour-core diameter — SAFE-400 14 mm pipe
//                   carries ~ 1 mm tungsten wall = 7 %; Cu-water 6 mm
//                   pipe carries ~ 0.4 mm Cu wall = 6.6 %).
//
//   ── PicoGK 2.0.0 closed-cavity flood-fill workaround ──
//   A sealed heat pipe (vapour core + wick + envelope, end-capped at
//   both ends) is a fully-enclosed cavity. PicoGK 2.0.0's voxelizer
//   flood-fills any region enclosed by a closed surface (documented in
//   the A.70 Tankage sprint — see `Voxelforge.Voxels/Tankage/
//   TankageVoxelBuilder.cs` header). The first-geometry-surface here
//   renders the heat pipe in its OPEN-ENDED form: the envelope and the
//   wick are both hollow shells via `AnnulusImplicit`, which sidesteps
//   the flood-fill since the axially-open ends let the voxelizer
//   represent the hollow correctly. This mirrors the A.70 Tankage
//   "without end caps" path + the A.67 Flywheel ThinRim path — both
//   ship clean hollow geometry via AnnulusImplicit.
//
//   Sealed (end-capped) heat-pipe rendering is deferred to a later
//   sprint together with the upstream PicoGK closed-cavity fix; for
//   printability evaluation the open-ended form is the meaningful
//   surface anyway (LPBF-printed heat pipes are built without their
//   end caps so the wick + envelope can be fired / vacuum-evacuated
//   downstream — the caps are welded on in a separate post-process).
//   The `HeatPipeGeometryResult` carries the envelope inner / outer
//   diameter + wick inner / outer diameter so downstream mass / volume
//   calculations recover the full sealed-heat-pipe envelope from the
//   open-ended voxel body.
//
//   ── Wall-safe smoothing (PicoGK pitfall #1) ──
//   Smoothen(d) destroys features < 2d. The thinnest feature is
//   min(wickThickness, envelopeWallThickness, envelopeOuterRadius); we
//   cap d at 25 % of that floor and only smoothen if d ≥ 0.02 mm
//   (consistent with FlywheelVoxelBuilder / TankageVoxelBuilder /
//   ChamberVoxelBuilder).
//
//   ── Validation surface ──
//   HeatPipeDesign.ValidateSelf() throws on non-positive
//   InternalDiameter_m / Length_m / HeatThroughput_W /
//   OperatingTemperature_K, and on Fluid == HeatPipeFluid.None. The
//   voxel builder propagates these.
//
// Coordinate convention: +X is the heat-pipe axis (same as Flywheel
// rotor axis, Tankage cylinder axis). The pipe runs from x = -L/2 to
// x = +L/2 (centred on the origin) so STL export always produces a
// symmetric body.

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.HeatPipe;

/// <summary>
/// PicoGK voxel builder for a heat-pipe device (Sprint A.80 / C.2).
/// Companion to <see cref="HeatPipeSolver"/> — turns a
/// <see cref="HeatPipeDesign"/> into a printable axisymmetric voxel body
/// (open-ended envelope + wick annulus, both hollow shells; see file
/// header for the PicoGK 2.0.0 closed-cavity workaround).
/// </summary>
internal static class HeatPipeVoxelBuilder
{
    /// <summary>
    /// Default wick annulus thickness as a fraction of the vapour-core
    /// diameter. Cluster-mid anchor for typical heat-pipe wicks:
    /// Cu-water sintered powder on a 6 mm pipe is 0.5–0.8 mm (8–13 %);
    /// SAFE-400 Li-W annular screen wick on a 14 mm pipe is ~ 1.2–1.5 mm
    /// (8.5–10.7 %); both land in the 8–13 % band. 10 % sits cluster-mid.
    /// </summary>
    internal const double WickThicknessFractionOfVapourCoreDiameter = 0.10;

    /// <summary>
    /// Default envelope wall thickness as a fraction of the vapour-core
    /// diameter. Cluster-mid anchor for printable heat-pipe envelopes:
    /// SAFE-400 14 mm pipe with ~ 1 mm tungsten wall is 7.1 %; Cu-water
    /// 6 mm pipe with ~ 0.4 mm Cu wall is 6.6 %; both land in the 6–8 %
    /// band. 7.5 % sits cluster-mid and gives a positive integer-mm
    /// wall on the SAFE-400 anchor (14 mm × 0.075 = 1.05 mm).
    /// </summary>
    internal const double EnvelopeWallThicknessFractionOfVapourCoreDiameter = 0.075;

    /// <summary>
    /// Wall-safe smoothing radius cap per PicoGK pitfall #1
    /// (<c>Smoothen(d)</c> destroys features &lt; 2d). 25 % of the
    /// minimum feature thickness keeps the wick and envelope walls
    /// intact.
    /// </summary>
    internal const double SmoothingFeatureFraction = 0.25;

    /// <summary>
    /// Build the heat-pipe voxel body for <paramref name="design"/>.
    /// </summary>
    /// <param name="design">Validated heat-pipe design record. Must
    ///   satisfy <see cref="HeatPipeDesign.ValidateSelf"/>.</param>
    /// <param name="voxelSize_mm">PicoGK voxel grid size in mm. Used only
    ///   for the wall-safe smoothing cap and the bounding-box padding.
    ///   The caller is responsible for constructing the ambient
    ///   <c>Library</c> at the matching voxel size.</param>
    /// <returns>Geometry summary + voxel handle.</returns>
    /// <exception cref="ArgumentNullException">design is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">voxelSize_mm is
    ///   non-positive.</exception>
    /// <exception cref="ArgumentException">design fails ValidateSelf —
    ///   propagates from the design record (non-positive dimensions, or
    ///   Fluid == HeatPipeFluid.None).</exception>
    internal static HeatPipeGeometryResult Build(HeatPipeDesign design, double voxelSize_mm)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (double.IsNaN(voxelSize_mm) || voxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(voxelSize_mm),
                $"voxelSize_mm={voxelSize_mm:F4} must be > 0.");
        design.ValidateSelf();

        // ── 1. Resolve dimensional fields (millimetres) ───────────────
        double D_vap_mm   = design.InternalDiameter_m * 1000.0;
        double L_mm       = design.Length_m           * 1000.0;
        double halfL_mm   = 0.5 * L_mm;

        double tWick_mm   = WickThicknessFractionOfVapourCoreDiameter   * D_vap_mm;
        double tWall_mm   = EnvelopeWallThicknessFractionOfVapourCoreDiameter * D_vap_mm;

        double R_vap_mm        = 0.5 * D_vap_mm;                  // wick inner radius
        double R_wickOuter_mm  = R_vap_mm + tWick_mm;             // wick outer = envelope inner
        double R_envOuter_mm   = R_wickOuter_mm + tWall_mm;       // envelope outer

        double D_wickOuter_mm  = 2.0 * R_wickOuter_mm;            // = envelope ID
        double D_envOuter_mm   = 2.0 * R_envOuter_mm;             // overall OD

        // ── 2. Bounding box (axisymmetric, centred on origin in x) ────
        float pad_mm    = (float)Math.Max(2.0 * voxelSize_mm, 1.0);
        float halfL_f   = (float)halfL_mm;
        float R_env_f   = (float)R_envOuter_mm;
        float R_wickO_f = (float)R_wickOuter_mm;
        float R_vap_f   = (float)R_vap_mm;
        var bounds = new BBox3(
            new Vector3(-halfL_f - pad_mm, -R_env_f - pad_mm, -R_env_f - pad_mm),
            new Vector3( halfL_f + pad_mm,  R_env_f + pad_mm,  R_env_f + pad_mm));

        // ── 3. Build envelope shell (open-ended hollow cylinder) ──────
        // AnnulusImplicit with rInner = envelope ID, rOuter = envelope OD.
        // The axially-open ends sidestep the PicoGK 2.0.0 closed-cavity
        // flood-fill limitation (same pattern as Tankage no-caps path +
        // Flywheel ThinRim).
        var envelopeImpl = new AnnulusImplicit(
            xMin:   -halfL_f,
            xMax:    halfL_f,
            rInner:  R_wickO_f,
            rOuter:  R_env_f);
        Voxels body = LibraryScope.MakeVoxels(envelopeImpl, bounds);

        // ── 4. Build wick annulus (open-ended hollow cylinder) ────────
        // AnnulusImplicit with rInner = vapour-core OD, rOuter = envelope ID.
        // Unioned into the body so the heat pipe is a single voxel field
        // for downstream mesh extraction / printability gates.
        var wickImpl = new AnnulusImplicit(
            xMin:   -halfL_f,
            xMax:    halfL_f,
            rInner:  R_vap_f,
            rOuter:  R_wickO_f);
        Voxels wick = LibraryScope.MakeVoxels(wickImpl, bounds);
        body.BoolAdd(wick);

        // ── 5. Wall-safe smoothing (PicoGK pitfall #1) ────────────────
        // Smoothen(d) destroys features < 2d → cap at 25 % of the
        // thinnest dimension. For SAFE-400 / KRUSTY (D = 14 mm):
        // tWick = 1.4 mm, tWall = 1.05 mm, R_envOuter ≈ 8.45 mm →
        // min-feature = tWall = 1.05 mm → safe smoothing ≤ 0.26 mm.
        // Skip below 0.02 mm (sub-voxel noise floor).
        double minFeature_mm = Math.Min(Math.Min(tWick_mm, tWall_mm), R_envOuter_mm);
        double safeSmooth_mm = SmoothingFeatureFraction * minFeature_mm;
        if (safeSmooth_mm >= 0.02)
            body.Smoothen((float)safeSmooth_mm);

        return new HeatPipeGeometryResult(
            EnvelopeOuterDiameter_mm:  D_envOuter_mm,
            EnvelopeInnerDiameter_mm:  D_wickOuter_mm,
            EnvelopeWallThickness_mm:  tWall_mm,
            WickOuterDiameter_mm:      D_wickOuter_mm,
            WickInnerDiameter_mm:      D_vap_mm,
            WickThickness_mm:          tWick_mm,
            VapourCoreDiameter_mm:     D_vap_mm,
            Length_mm:                 L_mm,
            Voxels:                    new PicoGKVoxelHandle(body));
    }
}
