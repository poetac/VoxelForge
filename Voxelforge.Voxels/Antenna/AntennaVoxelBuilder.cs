// AntennaVoxelBuilder.cs — Sprint A.83 (C.2) parabolic-dish + feed voxel
// builder. Framing-B Phase 3 voxel-pipeline backfill on the Antenna
// pillar (Wave-1 internal namespace `Voxelforge.Antenna`, algebraic-only
// since ANT.W1 / ANT.W2; this is the first geometry surface). LAST of
// the 6 voxel builders in Track C.2 — completes the umbrella sweep
// (Flywheel A.67 ✓ → Tankage A.70 ✓ → HeatPipe ✓ → Refrigeration ✓
// → Aerostructures ✓ → Antenna A.83 ✓ closes #647).
//
// Generates a parabolic reflector + feed assembly from an
// AntennaLinkDesign (uses the Tx-side dish parameters):
//
//   ── Reflector topology ──
//   Paraboloid of revolution around +Z. Standard form:
//     z(r) = r² / (4F)         with r² = x² + y²
//   where F is the focal length. The reflector is an OPEN-FRONT shell of
//   wall thickness t: the outer surface follows z = r²/(4F) and the
//   inner surface follows z = r²/(4F) + t (offset along +Z, the dish's
//   illumination direction). The aperture is the circle r = D/2 in the
//   plane z = (D/2)²/(4F).
//
//   ── f/D ratio anchor ──
//   The DESIGN record (AntennaLinkDesign) carries diameter but not
//   focal length. Real prime-focus dishes cluster f/D ∈ [0.3, 0.5]
//   (DSN BWG ~0.4, DBS 0.6m dish ~0.4, amateur radio H-alpha dishes
//   ~0.4-0.45). We anchor f/D = 0.4 as the cluster-mid default. This
//   choice is documented in DefaultFocalToDiameterRatio.
//
//   ── Reflector wall thickness anchor ──
//   Cluster-anchor for spun-aluminium ground-station dishes is 1.5-3 mm
//   (DBS 0.6 m residential is 1.5 mm, commercial 2-3 mm dishes are
//   2.4 mm). We anchor wall = 2 mm with a floor at 4×voxel so the
//   shell stays voxel-resolvable. Documented in DefaultReflectorWallThickness_mm.
//
//   ── Feed topology ──
//   A cylindrical feed envelope positioned at the focal point z = F
//   above the vertex, oriented along +Z (boresight). Models the
//   feed-horn + LNB block as a single cylinder. Diameter scales with
//   wavelength but we use a wavelength-independent geometric anchor
//   (D/15 by default — the 0.6 m DBS dish has ~40 mm feed-block radius
//   = D/15). Length matches a typical 0.5×D extension above the focus
//   to envelope the feed support arm + LNB; the cylinder spans
//   [z = F − L_feed/2, z = F + L_feed/2].
//
//   ── Boresight axis convention ──
//   +Z is the dish boresight axis (look-direction = +Z; the reflector
//   focuses incoming +Z radiation onto its focal point at z = F). The
//   vertex sits at the origin. The full assembly extends from z = 0
//   (vertex) to z = F + L_feed/2 (top of feed envelope).
//
//   ── PicoGK closed-cavity note ──
//   The parabolic dish is OPEN-FRONT (the aperture circle r = D/2 has
//   no covering surface). The reflector shell — outer paraboloid minus
//   inner offset paraboloid — therefore renders correctly as a HOLLOW
//   shell without triggering the PicoGK 2.0.0 closed-cavity flood-fill
//   limitation documented in A.70 Tankage. No special workaround needed
//   for the dish shell itself. The feed cylinder is open-bottom (it
//   merges into the dish-side air gap) and open-top, so it also renders
//   as a hollow cylinder if we wanted that; for geometry-only sprint
//   we voxelise the feed as a SOLID block (it's a stand-in for the
//   feed-horn + LNB + supports — downstream LPBF preparation can shell
//   it via mesh-based operators).
//
//   ── Wall-safe smoothing (PicoGK pitfall #1) ──
//   Smoothen(d) destroys features < 2d. The reflector shell wall is
//   the thinnest feature; we cap d at 25 % of
//   min(reflectorWall, feedRadius, dishDepth) and only smoothen if
//   d ≥ 0.02 mm (consistent with FlywheelVoxelBuilder /
//   TankageVoxelBuilder).
//
//   ── Non-parabolic kinds ──
//   AntennaKind.IdealIsotropic, HalfWaveDipole, YagiUda, Horn are
//   gracefully unsupported (throw NotSupportedException pointing to
//   ANT.W4 framing-C). This sprint is geometry-only for the
//   parabolic-dish topology — the wire / aperture / array topologies
//   need a separate library of feed-element implicits that isn't in
//   scope for the voxel-pipeline backfill umbrella (#647).
//
//   ── Validation surface ──
//   AntennaLinkDesign.ValidateSelf() rejects the AntennaKind.None
//   sentinel + non-positive frequency / power / distance / aperture
//   efficiency + parabolic-dish endpoints with diameter <= 0. The
//   voxel builder propagates these.

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.Antenna;

/// <summary>
/// PicoGK voxel builder for a parabolic-dish + feed antenna assembly
/// (Sprint A.83 / C.2). Companion to <see cref="AntennaSolver"/> — turns
/// an <see cref="AntennaLinkDesign"/> (Tx-side dish parameters) into a
/// voxel body. LAST of the 6 Track C.2 voxel builders — closes umbrella
/// issue #647.
/// </summary>
internal static class AntennaVoxelBuilder
{
    /// <summary>
    /// Default focal-length-to-diameter ratio for the paraboloid.
    /// Prime-focus dishes cluster f/D ∈ [0.3, 0.5] (DSN BWG ~0.4, DBS
    /// 0.6 m residential ~0.4, amateur ham-radio dishes ~0.4-0.45);
    /// 0.4 is the cluster mid-anchor. See file header for the cluster
    /// reference set.
    /// </summary>
    internal const double DefaultFocalToDiameterRatio = 0.4;

    /// <summary>
    /// Default reflector shell-wall thickness [mm]. Cluster-anchor for
    /// spun-aluminium ground-station dishes (DBS 0.6 m residential
    /// 1.5 mm, commercial 2-3 mm dishes 2.4 mm); 2 mm is the cluster
    /// mid-anchor. The builder bumps this up to 4×voxel if necessary so
    /// the shell stays voxel-resolvable (sub-voxel walls quantise poorly).
    /// </summary>
    internal const double DefaultReflectorWallThickness_mm = 2.0;

    /// <summary>
    /// Default feed-envelope radius as a fraction of dish diameter.
    /// Anchored from the DBS 0.6 m dish (~40 mm feed-block radius =
    /// D/15). Slightly oversized for small ground-station dishes;
    /// faithful for the residential-DBS cluster. Geometry-only proxy
    /// for the feed-horn + LNB + support assembly.
    /// </summary>
    internal const double DefaultFeedRadiusFractionOfDiameter = 1.0 / 15.0;

    /// <summary>
    /// Default feed-envelope axial length as a fraction of dish
    /// diameter. Wraps the cylindrical feed block around the focal
    /// point (extends L_feed/2 above + below the focus). 0.5×D matches
    /// the DBS-cluster feed-arm + LNB envelope.
    /// </summary>
    internal const double DefaultFeedLengthFractionOfDiameter = 0.5;

    /// <summary>
    /// Wall-safe smoothing radius cap per PicoGK pitfall #1
    /// (<c>Smoothen(d)</c> destroys features &lt; 2d). 25 % of the
    /// minimum feature thickness keeps the reflector wall + feed
    /// envelope intact.
    /// </summary>
    internal const double SmoothingFeatureFraction = 0.25;

    /// <summary>
    /// Build the parabolic-dish + feed voxel body for
    /// <paramref name="design"/>. Uses the Tx-side dish parameters
    /// (<c>design.TransmitDishDiameter_m</c>); the Rx-side parameters
    /// are ignored. Build the Rx dish by inverting the Tx/Rx assignment
    /// at the call site or by reading <c>ReceiveDishDiameter_m</c> into
    /// a swapped <c>AntennaLinkDesign</c> record.
    /// </summary>
    /// <param name="design">Validated antenna design record. Must satisfy
    ///   <see cref="AntennaLinkDesign.ValidateSelf"/> AND must have
    ///   <see cref="AntennaKind.ParabolicDish"/> as the Tx topology.</param>
    /// <param name="voxelSize_mm">PicoGK voxel grid size in mm. Used only
    ///   for the wall-safe smoothing cap and the bounding-box padding.
    ///   The caller is responsible for constructing the ambient
    ///   <c>Library</c> at the matching voxel size.</param>
    /// <returns>Geometry summary + voxel handle.</returns>
    /// <exception cref="ArgumentNullException">design is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">voxelSize_mm is
    ///   non-positive.</exception>
    /// <exception cref="ArgumentException">design fails ValidateSelf —
    ///   propagates from the design record (AntennaKind.None, non-positive
    ///   frequency / power / distance / aperture efficiency, or non-positive
    ///   ParabolicDish diameter).</exception>
    /// <exception cref="NotSupportedException">design.TransmitAntennaKind
    ///   is not <see cref="AntennaKind.ParabolicDish"/>. Wire-class
    ///   (HalfWaveDipole / YagiUda) + aperture-class (Horn) + ideal
    ///   topologies (IdealIsotropic) are deferred to ANT.W4 framing-C
    ///   work for non-dish topology library.</exception>
    internal static AntennaGeometryResult Build(AntennaLinkDesign design, double voxelSize_mm)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (double.IsNaN(voxelSize_mm) || voxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(voxelSize_mm),
                $"voxelSize_mm={voxelSize_mm:F4} must be > 0.");
        design.ValidateSelf();

        if (design.TransmitAntennaKind != AntennaKind.ParabolicDish)
            throw new NotSupportedException(
                $"AntennaVoxelBuilder.Build() is ParabolicDish-only. "
              + $"For '{design.TransmitAntennaKind}' use: "
              + $"BuildHelical / BuildHorn / BuildYagiUda / BuildPatch, "
              + $"or the general dispatch method BuildAny().");

        // ── 1. Resolve dimensional fields (millimetres) ───────────────
        double D_mm = design.TransmitDishDiameter_m * 1000.0;
        double R_mm = 0.5 * D_mm;                  // aperture radius
        double F_mm = DefaultFocalToDiameterRatio * D_mm;
        double depth_mm = (R_mm * R_mm) / (4.0 * F_mm);  // z(R) = R²/(4F)

        // Wall thickness with a 4×voxel floor for resolvability.
        double wallMin_mm = 4.0 * voxelSize_mm;
        double wall_mm = Math.Max(DefaultReflectorWallThickness_mm, wallMin_mm);

        double feedRadius_mm = DefaultFeedRadiusFractionOfDiameter * D_mm;
        double feedLength_mm = DefaultFeedLengthFractionOfDiameter * D_mm;

        // Total axial extent: vertex at z=0; reflector outer top at
        // z = depth + wall (the offset paraboloid's rim); feed extends to
        // z = F + L_feed/2. Take the max.
        double feedTop_mm = F_mm + 0.5 * feedLength_mm;
        double reflectorTop_mm = depth_mm + wall_mm;
        double zMax_mm = Math.Max(feedTop_mm, reflectorTop_mm);
        double zMin_mm = 0.0;                       // vertex sits at origin
        double overall_mm = zMax_mm - zMin_mm;

        // ── 2. Bounding box ───────────────────────────────────────────
        // Reflector + feed + a feed-radius lateral margin so the feed
        // cylinder fits cleanly.
        float pad_mm = (float)Math.Max(2.0 * voxelSize_mm, 1.0);
        float halfDiameter_mm = (float)R_mm;
        // Feed cylinder may extend below z = F − L_feed/2; clamp the
        // bottom of the bounding box to min(z_vertex, feedBottom).
        double feedBottom_mm = F_mm - 0.5 * feedLength_mm;
        float zMinBox = (float)Math.Min(zMin_mm, feedBottom_mm) - pad_mm;
        float zMaxBox = (float)zMax_mm + pad_mm;
        var bounds = new BBox3(
            new Vector3(-halfDiameter_mm - pad_mm, -halfDiameter_mm - pad_mm, zMinBox),
            new Vector3( halfDiameter_mm + pad_mm,  halfDiameter_mm + pad_mm, zMaxBox));

        // ── 3. Reflector shell ────────────────────────────────────────
        // Open-front paraboloid shell, axially bounded by z ∈ [0, depth + wall].
        var reflectorImpl = new ParaboloidShellImplicit(
            focalLength_mm:    (float)F_mm,
            apertureRadius_mm: (float)R_mm,
            wallThickness_mm:  (float)wall_mm);
        Voxels body = LibraryScope.MakeVoxels(reflectorImpl, bounds);

        // ── 4. Feed cylinder ──────────────────────────────────────────
        // Boresight is +Z. Cylinder centred on (0, 0, F), oriented +Z,
        // length L_feed → spans z ∈ [F − L_feed/2, F + L_feed/2].
        var feedImpl = new CylinderImplicit(
            start:     new Vector3(0f, 0f, (float)(F_mm - 0.5 * feedLength_mm)),
            direction: new Vector3(0f, 0f, 1f),
            radius:    (float)feedRadius_mm,
            length:    (float)feedLength_mm);
        Voxels feedVox = LibraryScope.MakeVoxels(feedImpl, bounds);
        body.BoolAdd(feedVox);

        // ── 5. Wall-safe smoothing (PicoGK pitfall #1) ────────────────
        // Smoothen(d) destroys features < 2d → cap at 25 % of the
        // thinnest dimension. Reflector wall is typically the thinnest
        // (2 mm vs feed radius 40 mm on a 0.6 m dish). Skip below
        // 0.02 mm (sub-voxel noise floor).
        double minFeature_mm = Math.Min(Math.Min(wall_mm, feedRadius_mm), depth_mm);
        double safeSmooth_mm = SmoothingFeatureFraction * minFeature_mm;
        if (safeSmooth_mm >= 0.02)
            body.Smoothen((float)safeSmooth_mm);

        return new AntennaGeometryResult(
            DishDiameter_mm:           D_mm,
            FocalLength_mm:            F_mm,
            DishDepth_mm:              depth_mm,
            ReflectorWallThickness_mm: wall_mm,
            FeedRadius_mm:             feedRadius_mm,
            FeedLength_mm:             feedLength_mm,
            OverallAxialLength_mm:     overall_mm,
            Voxels:                    new PicoGKVoxelHandle(body));
    }

    // ── ANT.W5-voxel: topology-specific builders ──────────────────────

    /// <summary>
    /// Sprint ANT.W5-voxel. Build a helical end-fire antenna voxel body.
    /// Delegates to <see cref="HelicalAntennaVoxelBuilder.Build"/>.
    /// </summary>
    internal static HelicalGeometryResult BuildHelical(
        AntennaLinkDesign design, double voxelSize_mm)
        => HelicalAntennaVoxelBuilder.Build(design, voxelSize_mm);

    /// <summary>
    /// Sprint ANT.W5-voxel. Build a conical horn antenna voxel body.
    /// Delegates to <see cref="HornAntennaVoxelBuilder.Build"/>.
    /// </summary>
    internal static HornGeometryResult BuildHorn(
        AntennaLinkDesign design, double voxelSize_mm)
        => HornAntennaVoxelBuilder.Build(design, voxelSize_mm);

    /// <summary>
    /// Sprint ANT.W5-voxel. Build a Yagi-Uda end-fire array voxel body.
    /// Delegates to <see cref="YagiUdaAntennaVoxelBuilder.Build"/>.
    /// </summary>
    internal static YagiUdaGeometryResult BuildYagiUda(
        AntennaLinkDesign design, double voxelSize_mm)
        => YagiUdaAntennaVoxelBuilder.Build(design, voxelSize_mm);

    /// <summary>
    /// Sprint ANT.W6. Build a microstrip patch antenna voxel body.
    /// Delegates to <see cref="PatchAntennaVoxelBuilder.Build"/>.
    /// </summary>
    internal static PatchGeometryResult BuildPatch(
        AntennaLinkDesign design, double voxelSize_mm)
        => PatchAntennaVoxelBuilder.Build(design, voxelSize_mm);

    /// <summary>
    /// Sprint ANT.W5-voxel. General dispatch: build the voxel body for
    /// any supported <see cref="AntennaKind"/> and return a common
    /// <see cref="IAntennaGeometryResult"/>. Callers that need
    /// topology-specific fields cast to the concrete result type.
    /// </summary>
    /// <exception cref="ArgumentNullException">design is null.</exception>
    /// <exception cref="NotSupportedException">
    ///   TransmitAntennaKind is IdealIsotropic, HalfWaveDipole, or
    ///   CrossedDipole — these have no printable solid geometry.</exception>
    internal static IAntennaGeometryResult BuildAny(
        AntennaLinkDesign design, double voxelSize_mm)
    {
        ArgumentNullException.ThrowIfNull(design);
        return design.TransmitAntennaKind switch
        {
            AntennaKind.ParabolicDish => Build(design, voxelSize_mm),
            AntennaKind.Helical       => BuildHelical(design, voxelSize_mm),
            AntennaKind.Horn          => BuildHorn(design, voxelSize_mm),
            AntennaKind.YagiUda       => BuildYagiUda(design, voxelSize_mm),
            AntennaKind.Patch         => BuildPatch(design, voxelSize_mm),
            _ => throw new NotSupportedException(
                     $"AntennaKind '{design.TransmitAntennaKind}' has no "
                   + $"printable solid voxel geometry.")
        };
    }
}

/// <summary>
/// Open-front paraboloid shell SDF. The reflector is the volume between
/// the outer paraboloid <c>z = r² / (4F)</c> and an inner paraboloid
/// offset along +Z by <c>wall</c>: <c>z = r² / (4F) + wall</c>. The
/// shell is axially bounded by <c>z ∈ [0, depth + wall]</c> where
/// <c>depth = R²/(4F)</c> is the rim depth at <c>r = R = D/2</c>. The
/// shell is open across the aperture circle <c>r = R</c> at the top
/// (incoming radiation enters along −Z and is reflected back to the
/// focal point at <c>z = F</c>; we model the reflector as the
/// structural shell BEHIND the reflective surface, with the outer
/// paraboloid being the reflective face and the offset inner paraboloid
/// being the back of the wall).
///
/// SDF construction (PicoGK convention: sign &lt; 0 = inside):
///   bowlSurfaceSDF = paraboloidZ(r) − z
///     Negative when z &gt; paraboloidZ — i.e. ABOVE the outer
///     (reflective) paraboloid. Inside the shell starts here.
///   backOffsetSDF = z − (paraboloidZ(r) + wall)
///     Negative when z &lt; paraboloidZ + wall — i.e. BELOW the inner
///     (back-of-shell) offset surface.
///   shellSDF = max(bowlSurfaceSDF, backOffsetSDF)
///     Negative when BOTH are negative — point is above the
///     reflective face AND below the back offset. That's the wall
///     volume.
///   axialBoundSDF = max(−z, z − (depth + wall))
///     Negative when z ∈ [0, depth + wall]. Clips the shell so it
///     doesn't extend past the rim plane or below the vertex.
///   radialBoundSDF = r − R
///     Negative when r &lt; R (inside the aperture circle). Clips
///     the shell to the dish footprint.
///   final = max(shellSDF, axialBoundSDF, radialBoundSDF)
/// All four sub-SDFs use the standard max-composition for set
/// intersection (negative everywhere = inside the wall).
///
/// Note: the SDF is an "envelope" rather than a true Euclidean
/// distance — the gradient magnitude is not constant. PicoGK 2.0.0's
/// voxelizer is sign-driven (interior = sign &lt; 0) and tolerates
/// envelope-style SDFs as long as the zero-level set is correct +
/// the gradient is non-zero at the boundary. The paraboloid normal
/// at <c>z = r²/(4F)</c> has the proper non-zero gradient (∂/∂z = 1,
/// ∂/∂r = −r/(2F)), so the surface is well-defined for voxelisation.
/// </summary>
internal sealed class ParaboloidShellImplicit : IImplicit
{
    private readonly float _focalLength_mm;
    private readonly float _apertureRadius_mm;
    private readonly float _wallThickness_mm;
    private readonly float _depth_mm;
    private readonly float _topZ_mm;

    internal ParaboloidShellImplicit(
        float focalLength_mm,
        float apertureRadius_mm,
        float wallThickness_mm)
    {
        if (focalLength_mm <= 0f)
            throw new ArgumentOutOfRangeException(nameof(focalLength_mm),
                $"focalLength_mm={focalLength_mm:F4} must be > 0.");
        if (apertureRadius_mm <= 0f)
            throw new ArgumentOutOfRangeException(nameof(apertureRadius_mm),
                $"apertureRadius_mm={apertureRadius_mm:F4} must be > 0.");
        if (wallThickness_mm <= 0f)
            throw new ArgumentOutOfRangeException(nameof(wallThickness_mm),
                $"wallThickness_mm={wallThickness_mm:F4} must be > 0.");
        _focalLength_mm    = focalLength_mm;
        _apertureRadius_mm = apertureRadius_mm;
        _wallThickness_mm  = wallThickness_mm;
        _depth_mm = (apertureRadius_mm * apertureRadius_mm) / (4f * focalLength_mm);
        _topZ_mm  = _depth_mm + wallThickness_mm;
    }

    public float fSignedDistance(in Vector3 p)
    {
        float r = MathF.Sqrt(p.X * p.X + p.Y * p.Y);
        float z = p.Z;

        // Paraboloid surfaces (z-as-function-of-r).
        float paraboloidZ = (r * r) / (4f * _focalLength_mm);

        // bowlSurfaceSDF: < 0 when point is ABOVE the outer (reflective)
        // paraboloid (z > paraboloidZ).
        float bowlSurfaceSDF = paraboloidZ - z;
        // backOffsetSDF: < 0 when point is BELOW the inner offset
        // (z < paraboloidZ + wall).
        float backOffsetSDF  = z - (paraboloidZ + _wallThickness_mm);

        // Shell volume: above outer AND below inner offset.
        float shellSDF = MathF.Max(bowlSurfaceSDF, backOffsetSDF);

        // Axial clamps: z ∈ [0, depth + wall].
        float axialBoundSDF = MathF.Max(-z, z - _topZ_mm);

        // Radial clamp: r ∈ [0, aperture radius].
        float radialBoundSDF = r - _apertureRadius_mm;

        return MathF.Max(MathF.Max(shellSDF, axialBoundSDF), radialBoundSDF);
    }
}
