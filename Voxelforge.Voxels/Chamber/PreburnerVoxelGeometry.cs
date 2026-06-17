// PreburnerVoxelGeometry.cs — Preburner voxel geometry. The preburner
// joins the chamber/pump/manifold bundle as its own body in the
// monolithic assembly.
//
// What this is
// ────────────
// A preburner is a miniature combustion chamber operating at off-
// nominal MR to produce turbine-drive gas. Geometrically it is a
// short pressure vessel with one or two feed ports and one exhaust
// port. We model it as a capsule (cylinder with hemispherical endcaps)
// because the internal pressure vessel stress distribution is most
// efficient with that shape — typical staged-combustion preburners
// are roughly 1.0 L/D. L* (characteristic length) from
// PreburnerResult.CharacteristicLength_m * ChamberVolume_mm3 set the
// volume; L/D = 1.0 picks the cylinder portion's diameter.
//
// Scope
// ─────
//   • Outer envelope only — injector face detail, feed port bores,
//     exhaust duct branching, and internal cooling passages are
//     deferred to a later follow-on when the staged-combustion
//     engineering story demands them.
//   • Wall thickness baked into the SDF: outer-r = derived-r + 3 mm.
//     Real staged-combustion preburners run 4–8 mm walls at 20–40 MPa;
//     we pick a common-case 3 mm for LPBF printability at 0.4 mm voxel.
//   • Orientation: long axis along +X to match the chamber convention.
//     The monolithic builder places it between the turbine-side of the
//     fuel pump and the injector dome so the exhaust duct routes cleanly.

using PicoGK;

namespace Voxelforge.Chamber;

/// <summary>
/// Derived preburner voxel-geometry record. Carries the physical
/// dimensions the monolithic builder needs to position and voxelise
/// the preburner body. Pure data — no PicoGK dependency.
/// </summary>
public sealed record PreburnerVoxelGeometry(
    double CylinderLength_mm,
    double InnerRadius_mm,
    double OuterRadius_mm,
    double TotalLength_mm,        // cylinder + 2 × hemisphere
    double WallThickness_mm,
    double EstimatedMass_g);

/// <summary>
/// Preburner voxel geometry derivation. Pure-math; no PicoGK
/// dependency in the sizing path. PicoGK only enters when
/// <see cref="BuildImplicit"/> is called.
/// </summary>
public static class PreburnerVoxel
{
    /// <summary>Default wall thickness (mm). 3 mm is the LPBF-printable baseline at 0.4 mm voxel.</summary>
    public const double DefaultWallThickness_mm = 3.0;

    /// <summary>Default length / diameter ratio for the cylindrical portion.</summary>
    public const double DefaultLengthToDiameterRatio = 1.0;

    /// <summary>GRCop-42 density for analytical mass (g/cm³). Matches TurbopumpGeometryGenerator.</summary>
    public const double WallMaterialDensity_gcm3 = 8.9;

    /// <summary>
    /// Size a preburner's voxel envelope from its sizing result.
    /// </summary>
    /// <param name="result">Preburner sizing (provides <c>ChamberVolume_mm3</c>).</param>
    /// <param name="wallThickness_mm">Override wall thickness; 0 = default 3 mm.</param>
    /// <param name="lengthToDiameterRatio">Override L/D; 0 = default 1.0.</param>
    public static PreburnerVoxelGeometry Size(
        PreburnerResult result,
        double wallThickness_mm = 0,
        double lengthToDiameterRatio = 0)
    {
        if (result is null) throw new System.ArgumentNullException(nameof(result));
        if (result.ChamberVolume_mm3 <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(result),
                "preburner chamber volume must be positive");

        double wall = wallThickness_mm > 0 ? wallThickness_mm : DefaultWallThickness_mm;
        double ld   = lengthToDiameterRatio > 0
                    ? lengthToDiameterRatio : DefaultLengthToDiameterRatio;

        // Volume of a capsule = π·r²·L_cyl + (4/3)·π·r³ with L_cyl = 2·r·(ld − 1)
        // is not quite what we want; we parameterise on the cylindrical portion:
        //   V = π·r²·L_cyl + (4/3)·π·r³
        //   L_cyl = ld·(2r)   (L/D ratio applied to cylindrical portion only)
        //   ⇒ V = 2π·r³·ld + (4/3)·π·r³ = π·r³·(2·ld + 4/3)
        double innerR = System.Math.Pow(
            result.ChamberVolume_mm3 / (System.Math.PI * (2.0 * ld + 4.0 / 3.0)),
            1.0 / 3.0);
        double cylLen = ld * 2.0 * innerR;
        double outerR = innerR + wall;
        double totalLen = cylLen + 2.0 * outerR;    // outer endcap extends by outerR each side

        // Analytical mass: outer capsule volume − inner capsule volume, × density.
        double volOuter_mm3 = System.Math.PI * outerR * outerR * cylLen
                            + (4.0 / 3.0) * System.Math.PI * outerR * outerR * outerR;
        double volInner_mm3 = System.Math.PI * innerR * innerR * cylLen
                            + (4.0 / 3.0) * System.Math.PI * innerR * innerR * innerR;
        double shellVol_mm3 = volOuter_mm3 - volInner_mm3;
        double mass_g       = shellVol_mm3 * 1e-3 * WallMaterialDensity_gcm3;

        return new PreburnerVoxelGeometry(
            CylinderLength_mm:    cylLen,
            InnerRadius_mm:       innerR,
            OuterRadius_mm:       outerR,
            TotalLength_mm:       totalLen,
            WallThickness_mm:     wall,
            EstimatedMass_g:      mass_g);
    }

    /// <summary>
    /// Build a PicoGK <see cref="IImplicit"/> representing the
    /// preburner's solid shell. The body is a capsule aligned with
    /// +X; the cylindrical portion runs from <c>x ∈ [0, L_cyl]</c>
    /// with hemispherical endcaps extending by <c>outerR</c> on each
    /// side. Caller positions the body in world space by wrapping in
    /// <c>OffsetImplicit</c>.
    /// </summary>
    public static IImplicit BuildImplicit(PreburnerVoxelGeometry geom)
    {
        if (geom is null) throw new System.ArgumentNullException(nameof(geom));
        return new PreburnerCapsuleImplicit(
            cylLength_mm:   (float)geom.CylinderLength_mm,
            outerR_mm:      (float)geom.OuterRadius_mm,
            wallThickness_mm: (float)geom.WallThickness_mm);
    }
}

/// <summary>
/// Capsule-shell SDF representing the preburner pressure vessel.
/// Cylindrical portion along +X with two hemispherical endcaps. Inside
/// the solid wall the SDF is negative. Interior cavity is hollow
/// (positive SDF).
/// </summary>
public sealed class PreburnerCapsuleImplicit : IImplicit
{
    private readonly float _cylLength;
    private readonly float _outerR;
    private readonly float _innerR;

    public PreburnerCapsuleImplicit(float cylLength_mm, float outerR_mm, float wallThickness_mm)
    {
        if (cylLength_mm <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(cylLength_mm),
                "cylinder length must be positive");
        if (outerR_mm <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(outerR_mm),
                "outer radius must be positive");
        if (wallThickness_mm <= 0 || wallThickness_mm >= outerR_mm)
            throw new System.ArgumentOutOfRangeException(nameof(wallThickness_mm),
                "wall thickness must be positive and less than outer radius");
        _cylLength = cylLength_mm;
        _outerR = outerR_mm;
        _innerR = outerR_mm - wallThickness_mm;
    }

    public float fSignedDistance(in System.Numerics.Vector3 p)
    {
        // Outer capsule: signed distance to capsule with axis [0, cylLength]
        // along +X and radius outerR.
        float dOuter = CapsuleSdf(p, _cylLength, _outerR);
        // Inner capsule (cavity): same axis, radius innerR. Solid shell
        // = outer ∩ !inner ⇒ SDF = max(dOuter, −dInner).
        float dInner = CapsuleSdf(p, _cylLength, _innerR);
        return MathF.Max(dOuter, -dInner);
    }

    // Standard capsule SDF: axis along +X from x=0 to x=L, radius r.
    private static float CapsuleSdf(System.Numerics.Vector3 p, float L, float r)
    {
        float xClamp = MathF.Max(0f, MathF.Min(L, p.X));
        float dx = p.X - xClamp;
        float dr = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        float dist = MathF.Sqrt(dx * dx + dr * dr);
        return dist - r;
    }
}
