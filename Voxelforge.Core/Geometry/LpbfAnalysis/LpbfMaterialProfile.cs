// LpbfMaterialProfile.cs — Sprint 27 (2026-04-23): per-material printability
// thresholds for the Geometry/LpbfAnalysis subtree.
//
// The existing contour-based `Manufacturing/OverhangAnalysis` uses a hardcoded
// 45° rule that covers stainless-adjacent alloys well enough. Sprint 27's
// voxel-surface-normal analysis needs a per-material threshold because the
// usable unsupported-overhang angle varies meaningfully across the LPBF
// alloy menu — Inconel 718 tolerates ~35° with well-tuned parameters,
// GRCop-42 falls in the middle at ~40°, and stainless / CuCrZr sit around
// 45°. Pull the thresholds from one table so tests + UI + gate surface the
// same numbers.
//
// Deliberately small + independent of PicoGK so the whole analysis layer is
// xUnit-safe (ADR-005).

namespace Voxelforge.Geometry.LpbfAnalysis;

/// <summary>
/// Canonical LPBF alloy identifier used by <see cref="OverhangAnalysis"/>
/// and friends. Distinct from <see cref="HeatTransfer.WallMaterial"/>
/// because the two have different consumer surfaces — the wall material
/// drives thermal + stress, this drives printability. Mapping from
/// <c>WallMaterial</c> to <c>LpbfMaterial</c> lives in
/// <see cref="LpbfMaterialProfiles.FromWallMaterialIndex(int)"/>.
/// </summary>
public enum LpbfMaterial
{
    /// <summary>GRCop-42 (NASA Cu-Cr-Nb, LPBF). 40° unsupported overhang floor.</summary>
    GRCop42,
    /// <summary>CuCrZr (C18150, LPBF). 45° unsupported overhang floor.</summary>
    CuCrZr,
    /// <summary>Inconel 625 (LPBF). 40° unsupported overhang floor.</summary>
    Inconel625,
    /// <summary>Inconel 718 (LPBF). 35° unsupported overhang floor — the
    /// tightest in the menu because IN718's heat-affected zone is more
    /// sensitive to sag than the copper alloys or IN625.</summary>
    Inconel718,
    /// <summary>316L stainless (LPBF). 45° floor — the industry-wide default.</summary>
    Stainless316L,
}

/// <summary>
/// Sprint 27 (2026-04-23): per-material printability thresholds.
/// <para><see cref="MinUnsupportedOverhangAngle_deg"/> is β (from horizontal)
/// below which an unsupported surface patch is deemed unprintable on this
/// alloy. The angle between the surface outward normal and the build axis
/// is (90° − β); the <see cref="OverhangAnalysis"/> evaluator flags a
/// patch when <c>(90° − β) &gt; (90° − floor)</c>, i.e. when the surface
/// is below the floor.</para>
/// </summary>
public sealed record LpbfMaterialProfile(
    LpbfMaterial Material,
    string       DisplayName,
    double       MinUnsupportedOverhangAngle_deg,
    string       Rationale,
    // Sprint 30 (2026-04-24, PH-3) — minimum trapped-powder pocket
    // volume to flag. Single-voxel jitter at the part's drain-path
    // boundary surfaces as ~0.1-0.5 mm³ pockets that aren't real
    // printability hazards. Filter pockets below this threshold
    // before they reach `TRAPPED_POWDER_REGION` violations. Tunable
    // per-alloy; 5 mm³ is a vendor-data-derived starting point.
    double       MinFlaggedPocketVolume_mm3 = 5.0,
    // PH-34 (2026-04-29) — minimum overhang patch area to flag. Single-
    // voxel surface jitter (e.g. step-function discretisation along a
    // gentle taper) produces 0.05-0.5 mm² noise patches that flag
    // identically to real 100+ mm² overhang offenders. Sub-threshold
    // patches self-support thermally during LPBF (the laser melts
    // through them and the surrounding solidified material acts as
    // heat sink). Sibling pattern to MinFlaggedPocketVolume_mm3 (PH-3).
    // 2 mm² is a vendor-data-derived starting point — covers ~3 voxels
    // at 0.8 mm voxel resolution. Tunable per-alloy.
    double       MinFlaggedOverhangPatchArea_mm2 = 2.0);

/// <summary>Per-material threshold lookup table.</summary>
public static class LpbfMaterialProfiles
{
    public static readonly LpbfMaterialProfile GRCop42 = new(
        Material: LpbfMaterial.GRCop42,
        DisplayName: "GRCop-42 (NASA Cu-Cr-Nb)",
        MinUnsupportedOverhangAngle_deg: 40.0,
        Rationale: "Cu-Cr-Nb LPBF tolerates shallower overhangs than Inconel but "
                 + "not as aggressive as 316L. NASA MSFC process-map calibration.");

    public static readonly LpbfMaterialProfile CuCrZr = new(
        Material: LpbfMaterial.CuCrZr,
        DisplayName: "CuCrZr (C18150)",
        MinUnsupportedOverhangAngle_deg: 45.0,
        Rationale: "Industry-standard 45° rule applies to copper alloys at "
                 + "nominal green-laser or IR-plus-absorber parameters.");

    public static readonly LpbfMaterialProfile Inconel625 = new(
        Material: LpbfMaterial.Inconel625,
        DisplayName: "Inconel 625",
        MinUnsupportedOverhangAngle_deg: 40.0,
        Rationale: "IN625 LPBF is the widely-qualified workhorse; 40° is "
                 + "typical on machines with heritage IN625 process maps.");

    public static readonly LpbfMaterialProfile Inconel718 = new(
        Material: LpbfMaterial.Inconel718,
        DisplayName: "Inconel 718",
        MinUnsupportedOverhangAngle_deg: 35.0,
        Rationale: "IN718 LPBF is more sag-prone than IN625 — heat-affected "
                 + "zone extends further on down-facing surfaces. Tuned-parameter "
                 + "vendors achieve 35° on heritage builds.");

    public static readonly LpbfMaterialProfile Stainless316L = new(
        Material: LpbfMaterial.Stainless316L,
        DisplayName: "316L stainless",
        MinUnsupportedOverhangAngle_deg: 45.0,
        Rationale: "Classic 45° rule. 316L LPBF is the most forgiving alloy in "
                 + "the menu — almost every commercial machine ships with a 45° "
                 + "process map for it.");

    public static readonly LpbfMaterialProfile[] All =
    {
        GRCop42, CuCrZr, Inconel625, Inconel718, Stainless316L,
    };

    public static LpbfMaterialProfile For(LpbfMaterial m) => m switch
    {
        LpbfMaterial.GRCop42       => GRCop42,
        LpbfMaterial.CuCrZr        => CuCrZr,
        LpbfMaterial.Inconel625    => Inconel625,
        LpbfMaterial.Inconel718    => Inconel718,
        LpbfMaterial.Stainless316L => Stainless316L,
        _ => throw new System.ArgumentOutOfRangeException(nameof(m), m, "Unknown LpbfMaterial"),
    };

    /// <summary>
    /// Map <see cref="HeatTransfer.WallMaterials"/> indices onto the
    /// printability-threshold menu. Index order is GRCop42 / CuCrZr /
    /// Inconel625 / Inconel718 (see <c>WallMaterials.All</c>); the final
    /// <see cref="LpbfMaterial.Stainless316L"/> entry is available by name
    /// only since the regen pipeline doesn't carry a stainless wall
    /// material today.
    /// </summary>
    public static LpbfMaterialProfile FromWallMaterialIndex(int idx) => idx switch
    {
        0 => GRCop42,
        1 => CuCrZr,
        2 => Inconel625,
        3 => Inconel718,
        _ => CuCrZr, // Safe fallback matching the WallMaterials default index.
    };
}
