// VoxelAdequacyGate.cs — Geometry-resolution adequacy check for LPBF voxel fidelity.
//
// Before declaring a design printable, verify that every critical
// geometric feature is resolved by at least 2 voxels (hard floor) and
// ideally by 3+ voxels (marginal-safety band). Features below 2× the voxel
// pitch are literally invisible to the solver and cannot be built reliably.
//
// The gate is SEPARATE from the physics FeasibilityGate: it depends only on
// geometry + a known voxel size and contains no PicoGK runtime calls, so it
// is safe to call from the tests without Library.Go().
//
// Critical features checked:
//   1. GasSideWall       — inner wall thickness (highest heat flux, most safety-critical)
//   2. RibThickness       — channel wall; determines flow-area accuracy
//   3. ChannelHtThroat   — narrowest channel height (half-value used in ManufacturingAnalysis)
//   4. MinChannelWidth   — thinnest point in channel circumferential pitch minus rib
//
// Rating scale:
//   Pass     — ratio ≥ 3.0 (≥ 3 voxels: geometry is well-resolved)
//   Marginal — ratio ≥ 2.0 but < 3.0 (2–3 voxels: boundary; geometry may lack surface detail)
//   Fail     — ratio < 2.0 (< 2 voxels: feature cannot be reliably printed)

using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Analysis;

/// <summary>Three-level resolution adequacy rating.</summary>
public enum VoxelAdequacyLevel
{
    Pass     = 0,
    Marginal = 1,
    Fail     = 2,
}

/// <summary>
/// Adequacy assessment for one named geometric feature.
/// </summary>
public sealed record FeatureAdequacy(
    string             FeatureName,
    double             FeatureSize_mm,
    double             VoxelRatio,        // FeatureSize_mm / voxelSize_mm
    VoxelAdequacyLevel Level);

/// <summary>
/// Full voxel-resolution adequacy report for one design.
/// Attached to <see cref="Optimization.RegenGenerationResult.VoxelAdequacy"/>
/// when the generation was called with a non-zero voxelSize_mm.
/// </summary>
public sealed record VoxelAdequacyResult(
    VoxelAdequacyLevel   Overall,
    FeatureAdequacy[]    Features,
    double               VoxelSize_mm);

/// <summary>
/// Pure geometric gate — no PicoGK calls, safe in unit tests without Library.Go().
/// </summary>
public static class VoxelAdequacyGate
{
    /// <summary>Minimum feature/voxel ratio below which the feature CANNOT be printed (2 voxels).</summary>
    public const double FailRatioThreshold     = 2.0;

    /// <summary>Minimum feature/voxel ratio for confident geometry (3 voxels).</summary>
    public const double MarginalRatioThreshold = 3.0;

    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate all four critical features against the given voxel size.
    /// Returns the worst-case overall level with per-feature breakdowns.
    /// </summary>
    /// <param name="channels">Channel schedule from the design.</param>
    /// <param name="contour">Chamber contour (used to compute min channel width).</param>
    /// <param name="voxelSize_mm">LPBF / voxel resolution in mm. Must be &gt; 0.</param>
    public static VoxelAdequacyResult Evaluate(
        ChannelSchedule channels,
        ChamberContour  contour,
        double          voxelSize_mm)
    {
        if (voxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(voxelSize_mm),
                "Voxel size must be positive (mm).");

        var features = new System.Collections.Generic.List<FeatureAdequacy>();

        void Check(string name, double featureMM)
        {
            double ratio = featureMM / voxelSize_mm;
            var level = ratio >= MarginalRatioThreshold ? VoxelAdequacyLevel.Pass
                      : ratio >= FailRatioThreshold     ? VoxelAdequacyLevel.Marginal
                      :                                   VoxelAdequacyLevel.Fail;
            features.Add(new FeatureAdequacy(name, featureMM, ratio, level));
        }

        Check("GasSideWall",    channels.GasSideWallThickness_mm);
        Check("RibThickness",   channels.RibThickness_mm);
        Check("ChannelHtThroat", channels.ChannelHeightAtThroat_mm);
        Check("MinChannelWidth", ComputeMinChannelWidth(contour, channels));

        // Overall = worst-case level across all features
        var overall = VoxelAdequacyLevel.Pass;
        foreach (var f in features)
            if (f.Level > overall) overall = f.Level;

        return new VoxelAdequacyResult(
            Overall:     overall,
            Features:    features.ToArray(),
            VoxelSize_mm: voxelSize_mm);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Internal helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimum channel width across all contour stations.
    /// Mirrors ManufacturingAnalysis.FindMinChannelWidth so the two
    /// modules agree on the definition.
    /// </summary>
    private static double ComputeMinChannelWidth(ChamberContour c, ChannelSchedule ch)
    {
        double minW = double.MaxValue;
        foreach (var s in c.Stations)
        {
            double rOuter = s.R_mm + ch.GasSideWallThickness_mm;
            double pitch  = 2.0 * Math.PI * rOuter / ch.ChannelCount;
            double w      = pitch - ch.RibThickness_mm;
            if (w < minW) minW = w;
        }
        return Math.Max(minW, 0.0);  // clamp to 0 — negative means channels don't fit at all
    }
}
