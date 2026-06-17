// MegaScaleBudgetInvariantTests.cs — pin ADR-006's empirical 1.5×
// safety factor for the MegaScaleEnvelope preset tables across both
// the historical 64 GB anchor (PresetsReferenceWorkstation) and the
// current 96 GB tier (PresetsCurrent).
//
// This test pins ADR-006's empirical 1.5× safety factor — failure means
// the active preset table needs recalibration, typically after a PicoGK
// sparsity-model upgrade per ADR-011 deliberate-sprint upgrades.
//
// Sister coverage: NoyronTierB3Tests.cs holds the structural invariants
// (preset ordering, voxel monotonicity, range). This file holds the
// quantitative invariant — projected peak vs. configured budget across
// the documented thrust × ResourceMode × tier grid.

using Voxelforge.Analysis;
using Voxelforge.UI;

namespace Voxelforge.Tests;

public class MegaScaleBudgetInvariantTests
{
    /// <summary>
    /// 64 GB historical reference workstation — ADR-006's original
    /// calibration anchor. Used to derive the Quiet / Balanced budgets
    /// via <see cref="ResourcePresets.Resolve"/>'s fractional split.
    /// </summary>
    private const long Total_64GB_Bytes = 64L * 1024 * 1024 * 1024;

    /// <summary>
    /// 96 GB current workstation tier — canonical target as of
    /// ADR-006 amendment 2026-05-17.
    /// </summary>
    private const long Total_96GB_Bytes = 96L * 1024 * 1024 * 1024;

    /// <summary>
    /// Empirical 1.5× safety factor baked into the preset calibration
    /// on top of the 0.50 sparsity default (see the preset doc
    /// comments). Projected peak crossing this ceiling means the active
    /// preset table has drifted out of envelope and needs recalibration.
    /// </summary>
    private const double SafetyFactor = 1.5;

    public static IEnumerable<object[]> ThrustModeTierGrid()
    {
        double[] thrusts = { 1_000.0, 10_000.0, 100_000.0, 1_000_000.0, 2_000_000.0 };
        ResourceMode[] modes = { ResourceMode.Quiet, ResourceMode.Balanced, ResourceMode.Maximum };
        (string Name, long TotalBytes)[] tiers = {
            ("64GB-ReferenceWorkstation", Total_64GB_Bytes),
            ("96GB-Current",              Total_96GB_Bytes),
        };
        foreach (var t in thrusts)
            foreach (var m in modes)
                foreach (var tier in tiers)
                    yield return new object[] { t, m, tier.Name, tier.TotalBytes };
    }

    [Theory]
    [MemberData(nameof(ThrustModeTierGrid))]
    public void Recommend_ProjectedPeakStaysInsideSafetyFactor(
        double thrust_N, ResourceMode mode, string tierName, long totalBytes)
    {
        long budget = BudgetForModeAndTier(mode, totalBytes);
        var rec = MegaScaleEnvelope.Recommend(thrust_N, budget);

        long ceiling = (long)(budget * SafetyFactor);
        Assert.True(rec.ProjectedPeakBytes <= ceiling,
            $"[{tierName}] Thrust {thrust_N / 1000.0:F0} kN @ {mode} (budget {budget / (1024.0 * 1024 * 1024):F1} GB): "
          + $"projected peak {rec.ProjectedPeakBytes / (1024.0 * 1024 * 1024):F2} GB "
          + $"exceeds {SafetyFactor}× ceiling = {ceiling / (1024.0 * 1024 * 1024):F2} GB. "
          + "Preset table needs recalibration (PicoGK sparsity model may have shifted).");

        Assert.True(rec.ProjectedPeakBytes > 0,
            "Projection failed to compute — MemoryProjectionGate returned zero bytes.");
    }

    [Theory]
    [MemberData(nameof(ThrustModeTierGrid))]
    public void Recommend_FeasibleImpliesProjectedFitsBudget(
        double thrust_N, ResourceMode mode, string tierName, long totalBytes)
    {
        long budget = BudgetForModeAndTier(mode, totalBytes);
        var rec = MegaScaleEnvelope.Recommend(thrust_N, budget);

        if (!rec.Feasible) return;

        Assert.True(rec.ProjectedPeakBytes <= rec.ProjectedBudgetBytes,
            $"[{tierName}] Thrust {thrust_N / 1000.0:F0} kN @ {mode}: Recommend reported Feasible but projected "
          + $"{rec.ProjectedPeakBytes / (1024.0 * 1024 * 1024):F2} GB > "
          + $"budget {rec.ProjectedBudgetBytes / (1024.0 * 1024 * 1024):F2} GB.");
    }

    [Fact]
    public void PresetsCurrent_CoversDocumentedThrustGrid()
    {
        // Meta-test: the property test grid above sweeps 1 kN — 2 MN.
        // PickPresetBracket needs at least one preset ≤ 1 kN (lower
        // bracket fallback) and at least one preset ≥ 2 MN (upper
        // bracket coverage). If a future edit trims either end the
        // grid silently clamps and the property test would still pass
        // against a degraded table.
        var presets = MegaScaleEnvelope.PresetsCurrent;

        Assert.Contains(presets, p => p.Thrust_N <= 1_000.0);
        Assert.Contains(presets, p => p.Thrust_N >= 2_000_000.0);
    }

    [Fact]
    public void PresetsReferenceWorkstation_CoversDocumentedThrustGrid()
    {
        var presets = MegaScaleEnvelope.PresetsReferenceWorkstation;

        Assert.Contains(presets, p => p.Thrust_N <= 1_000.0);
        Assert.Contains(presets, p => p.Thrust_N >= 2_000_000.0);
    }

    private static long BudgetForModeAndTier(ResourceMode mode, long totalBytes)
    {
        if (totalBytes == Total_64GB_Bytes)
            return mode switch
            {
                ResourceMode.Quiet    => totalBytes / 4,                                          // 25 % → 16 GB
                ResourceMode.Balanced => MegaScaleEnvelope.Budget_ReferenceWorkstation_Balanced,  // 50 % → 32 GB
                ResourceMode.Maximum  => MegaScaleEnvelope.Budget_ReferenceWorkstation_Maximum,   // ~58 GB after OS/app
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unexpected mode."),
            };
        if (totalBytes == Total_96GB_Bytes)
            return mode switch
            {
                ResourceMode.Quiet    => totalBytes / 4,                              // 25 % → 24 GB
                ResourceMode.Balanced => MegaScaleEnvelope.Budget_Current_Balanced,   // 50 % → 48 GB
                ResourceMode.Maximum  => MegaScaleEnvelope.Budget_Current_Maximum,    // ~87 GB after OS/app
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unexpected mode."),
            };
        throw new ArgumentOutOfRangeException(nameof(totalBytes), totalBytes, "Unknown reference tier.");
    }
}
