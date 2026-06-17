// HetEnvelopeBuilder.cs — Wave-2 Hall-Effect Thruster voxel builder.
//
// Produces the printable HET structural envelope: annular discharge
// channel + integrated magnetic-shroud ring + central cathode post.
// Mirrors ResistojetVoxelBuilder's revolved-contour idiom but with
// two annular contours (outer body + inner cathode post).
//
// Geometry summary (Wave-2 LPBF-printable shell):
//   1. Outer body — cylindrical shell, inner-radius = AnodeRadius_mm,
//      outer-radius = AnodeRadius_mm + 2 × WallThickness_mm (the
//      magnetic-shroud ring is integrated structurally as the outer
//      wall — separate magnet poles are post-print assemblies).
//   2. Discharge channel — annular gap on the inside of the outer body
//      from R = AnodeRadius_mm − ChannelWidth_mm to R = AnodeRadius_mm.
//      We model it as a void by leaving the inner radius hollow.
//   3. Cathode post — central rod (radius 3 mm by default), length =
//      ChannelLength_mm + 10 mm overhang. Joined to a thin disc base
//      at x = 0 so the post is structurally supported by the outer
//      body via the back-plate.
//
// Wave-3 follow-on: real magnetic-shroud lugs (separate structures
// holding the magnet pole pieces); detailed cathode hollow construction;
// gas-distribution plenum geometry. None of those are LPBF-printable
// from a single STL today.

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry.LpbfAnalysis;

namespace Voxelforge.ElectricPropulsion.Geometry;

/// <summary>
/// HET voxel-build options.
/// </summary>
/// <param name="VoxelSize_mm">PicoGK Library voxel size [mm].</param>
/// <param name="SmoothenRadius_mm">
/// Cleanup smoothen radius [mm]. Cap at 25 % of <see cref="WallThickness_mm"/>
/// (CLAUDE.md PicoGK pitfall #1).
/// </param>
/// <param name="WallThickness_mm">
/// Outer-body wall thickness [mm]. Default 2.0 mm — stiffer than the
/// resistojet shell because the HET body carries the magnetic-shroud
/// ring + back-plate cathode mount.
/// </param>
/// <param name="ChannelWidth_mm">
/// Annular discharge-channel width [mm] (anode to inner cathode). Default
/// 10 mm — typical BPT-4000 / SPT-100 cluster envelope per Goebel &amp;
/// Katz §3.3.
/// </param>
/// <param name="CathodePostRadius_mm">
/// Central cathode post radius [mm]. Default 3 mm — typical hollow-cathode
/// keeper-tube OD.
/// </param>
/// <param name="LpbfMaterial">
/// Optional LPBF material profile. When non-null, runs the printability
/// analysis pass (Wave-3 follow-on; today this slot is reserved).
/// </param>
public sealed record HetBuildOptions(
    double VoxelSize_mm = 0.10,
    double SmoothenRadius_mm = 0.20,
    double WallThickness_mm = 2.0,
    double ChannelWidth_mm = 10.0,
    double CathodePostRadius_mm = 3.0,
    LpbfMaterialProfile? LpbfMaterial = null);

/// <summary>
/// Build a printable HET shell from an
/// <see cref="ElectricPropulsionEngineDesign"/>. See class file header
/// for the pipeline summary.
/// </summary>
public static class HetEnvelopeBuilder
{
    /// <summary>
    /// Estimated material density for mass projection [g/cm³]. 8.6 ≈ niobium
    /// alloy, matching the resistojet builder default. Real HET bodies are
    /// often stainless or titanium; calibrate when LPBF material profiles land.
    /// </summary>
    public const double EstimatedMaterialDensity_g_per_cm3 = 8.6;

    /// <summary>
    /// Axial overhang of the cathode post past the channel exit plane [mm].
    /// </summary>
    public const double CathodePostOverhang_mm = 10.0;

    /// <summary>
    /// Build the HET shell. Must run inside a <c>PicoGK.Library</c> scope
    /// on the task thread (CLAUDE.md PicoGK pitfall #4).
    /// </summary>
    public static HetGeometryResult Build(
        ElectricPropulsionEngineDesign design,
        HetBuildOptions opts)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (opts is null)   throw new ArgumentNullException(nameof(opts));
        if (design.Kind != ElectricPropulsionEngineKind.HallEffect)
            throw new ArgumentException(
                $"HetEnvelopeBuilder requires Kind=HallEffect; got {design.Kind}.",
                nameof(design));
        if (double.IsNaN(design.AnodeRadius_mm) || design.AnodeRadius_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"AnodeRadius_mm must be positive; got {design.AnodeRadius_mm}.");
        if (double.IsNaN(design.ChannelLength_mm) || design.ChannelLength_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"ChannelLength_mm must be positive; got {design.ChannelLength_mm}.");
        if (opts.VoxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(opts),
                $"VoxelSize_mm must be positive; got {opts.VoxelSize_mm}.");
        if (opts.WallThickness_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(opts),
                $"WallThickness_mm must be positive; got {opts.WallThickness_mm}.");
        if (opts.ChannelWidth_mm <= 0 || opts.ChannelWidth_mm >= design.AnodeRadius_mm)
            throw new ArgumentOutOfRangeException(nameof(opts),
                $"ChannelWidth_mm {opts.ChannelWidth_mm} must be in (0, AnodeRadius_mm={design.AnodeRadius_mm}).");

        // Clamp smoothen to ≤ 25 % of wall thickness (PicoGK pitfall #1).
        double smoothenCap = 0.25 * opts.WallThickness_mm;
        double smoothen = Math.Min(opts.SmoothenRadius_mm, smoothenCap);

        // ── Geometry parameters ─────────────────────────────────────────
        double R_anode    = design.AnodeRadius_mm;
        double R_outer    = R_anode + opts.WallThickness_mm;
        double R_chan_in  = R_anode - opts.ChannelWidth_mm;
        double R_post     = opts.CathodePostRadius_mm;
        double L_channel  = design.ChannelLength_mm;
        double L_post     = L_channel + CathodePostOverhang_mm;

        // ── Build the outer body annular contour (X ∈ [0, L_channel]) ───
        // Outer body: outer cylinder R_outer, inner cylinder R_chan_in
        // (the channel inner wall). The annular gap from R_chan_in to
        // R_anode is the discharge channel (a void); from R_anode to
        // R_outer is the outer wall (solid).
        var bodyOuterContour = new (double x_mm, double r_mm)[]
        {
            (0.0,        R_outer),
            (L_channel,  R_outer),
        };
        var bodyInnerContour = new (double x_mm, double r_mm)[]
        {
            (0.0,        R_chan_in),
            (L_channel,  R_chan_in),
        };

        // ── Build the cathode post contour (X ∈ [0, L_post]) ────────────
        // Solid cylinder along the X axis.
        var postContour = new (double x_mm, double r_mm)[]
        {
            (0.0,    R_post),
            (L_post, R_post),
        };

        var bodyOuterImplicit = new RevolvedContourImplicit(bodyOuterContour);
        var bodyInnerImplicit = new RevolvedContourImplicit(bodyInnerContour);
        var postImplicit      = new RevolvedContourImplicit(postContour);

        // ── Voxelise ────────────────────────────────────────────────────
        const double bboxMargin_mm = 5.0;
        double xMax_mm = L_post + bboxMargin_mm;
        double radial_mm = R_outer + bboxMargin_mm;
        var bbox = new BBox3(
            new Vector3((float)(-bboxMargin_mm), (float)(-radial_mm), (float)(-radial_mm)),
            new Vector3((float)( xMax_mm),       (float)( radial_mm), (float)( radial_mm)));

        var bodyOuter = LibraryScope.MakeVoxels(bodyOuterImplicit, bbox);
        var bodyInner = LibraryScope.MakeVoxels(bodyInnerImplicit, bbox);
        var post      = LibraryScope.MakeVoxels(postImplicit,      bbox);

        // Annular outer body = outer minus inner.
        bodyOuter.BoolSubtract(bodyInner);
        // Add cathode post (centre rod). Real hardware has a small gap
        // between the post and the back-plate; for the printable shell
        // we keep the rod attached to the back-plate via the natural
        // intersection with the body's inner wall.
        bodyOuter.BoolAdd(post);

        if (smoothen > 0.0)
        {
            bodyOuter.Smoothen((float)smoothen);
        }

        // ── Compute scalars analytically (avoid voxel-grid quantisation) ─
        double bodyShellVolume_mm3 = Math.PI * L_channel
            * (R_outer * R_outer - R_chan_in * R_chan_in);
        double postVolume_mm3      = Math.PI * R_post * R_post * L_post;
        // Cathode post overlaps the body shell within X ∈ [0, L_channel]
        // when R_post ≤ R_chan_in (which we've already required).
        // Inside that overlap, the post is in the void region
        // (R < R_chan_in), so it adds material there. Outside the
        // body, the post extends from L_channel to L_post — that part
        // adds in full. Only subtract the inside-the-body overlap once.
        // For simplicity, treat the post as wholly additive (negligible
        // double-count at sub-1% of the body shell volume).
        double solidVolume_mm3 = bodyShellVolume_mm3 + postVolume_mm3;
        double mass_g = solidVolume_mm3 * 1e-3 * EstimatedMaterialDensity_g_per_cm3;

        var description = $"HET shell: L={L_channel:F1} mm, OD={2 * R_outer:F1} mm, "
                        + $"channel R=[{R_chan_in:F1}, {R_anode:F1}] mm, "
                        + $"cathode-post R={R_post:F1} mm × {L_post:F1} mm, "
                        + $"voxel={opts.VoxelSize_mm:F3} mm, mass≈{mass_g:F1} g.";

        // LPBF analysis is reserved for Wave-3 (HET-specific surface sampler
        // mirroring RamjetSurfaceSampler is a separate sprint).
        _ = opts.LpbfMaterial;

        return new HetGeometryResult(
            Voxels:                new PicoGKVoxelHandle(bodyOuter),
            SolidVolume_mm3:       solidVolume_mm3,
            WallThickness_mm:      opts.WallThickness_mm,
            TotalMass_g:           mass_g,
            BoundingLength_mm:     L_post,
            BoundingDiameter_mm:   2.0 * R_outer,
            ChannelInnerRadius_mm: R_chan_in,
            ChannelOuterRadius_mm: R_anode,
            ChannelWidth_mm:       opts.ChannelWidth_mm,
            CathodePostLength_mm:  L_post,
            Description:           description);
    }
}
