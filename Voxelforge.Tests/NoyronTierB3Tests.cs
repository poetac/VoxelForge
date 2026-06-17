// NoyronTierB3Tests.cs — Tier B3 forcing-function suite. Covers:
//   • MegaScaleEnvelope.EstimateBoundingBox — monotonic scaling with
//     thrust; positive outputs on the valid range.
//   • PickPresetBracket — returns the SMALLEST preset ≥ requested
//     thrust so the recommendation errs toward safety.
//   • Recommend — voxel scales with cube root of budget ratio; tile
//     count matches preset; feasibility flag respects memory projection.
//   • BuildSweep — filters infeasible points; ascending thrust order.
//   • Envelope rejects out-of-range thrust + non-positive budget.
//   • Projected peak memory matches MemoryProjectionGate at the same
//     bbox + voxel (forcing function: the two must agree on math).
//
// These are pure-math tests; no PicoGK Library required.

using Voxelforge.Analysis;

namespace Voxelforge.Tests;

public class NoyronTierB3Tests
{
    // ══════════════════════ EstimateBoundingBox ══════════════════════

    [Fact]
    public void EstimateBoundingBox_GrowsMonotonicallyWithThrust()
    {
        var small  = MegaScaleEnvelope.EstimateBoundingBox(1_000);
        var medium = MegaScaleEnvelope.EstimateBoundingBox(20_000);
        var large  = MegaScaleEnvelope.EstimateBoundingBox(200_000);

        Assert.True(small.Lx_mm < medium.Lx_mm);
        Assert.True(medium.Lx_mm < large.Lx_mm);
        Assert.True(small.Ly_mm < medium.Ly_mm);
        Assert.True(medium.Lz_mm < large.Lz_mm);
    }

    [Fact]
    public void EstimateBoundingBox_IsSquareInCrossSection()
    {
        // Axisymmetric chamber ⇒ Ly and Lz identical.
        foreach (double thrust in new[] { 500.0, 2_224.0, 50_000.0, 200_000.0 })
        {
            var bbox = MegaScaleEnvelope.EstimateBoundingBox(thrust);
            Assert.Equal(bbox.Ly_mm, bbox.Lz_mm, 6);
        }
    }

    // ══════════════════════ PickPresetBracket ══════════════════════

    [Theory]
    [InlineData(250.0,       500.0)]    // below smallest preset → return 500 N
    [InlineData(500.0,       500.0)]    // exact match
    [InlineData(501.0,     2_224.0)]    // just above → next bracket
    [InlineData(10_000.0, 10_000.0)]
    [InlineData(150_000.0, 200_000.0)]
    public void PickPresetBracket_ReturnsSmallestPresetAboveThrust(double thrust, double expectedThrust)
    {
        var bracket = MegaScaleEnvelope.PickPresetBracket(thrust);
        Assert.Equal(expectedThrust, bracket.Thrust_N, 0);
    }

    [Fact]
    public void PickPresetBracket_ClampsAboveMaxThrust()
    {
        // Requests above the largest preset clamp to the last entry.
        var bracket = MegaScaleEnvelope.PickPresetBracket(10_000_000.0);
        Assert.Equal(2_000_000.0, bracket.Thrust_N, 0);
    }

    // ══════════════════════ Recommend ══════════════════════

    [Fact]
    public void Recommend_MatchesPresetAtCurrentBalanced()
    {
        // PresetsCurrent[10 kN] = (0.35 mm, 1 tile, Balanced); calling
        // Recommend with the matching Budget_Current_Balanced anchor
        // returns the preset verbatim (scaleFactor = 1).
        var rec = MegaScaleEnvelope.Recommend(
            thrust_N:     10_000,
            budgetBytes:  MegaScaleEnvelope.Budget_Current_Balanced);
        Assert.Equal(10_000.0, rec.Thrust_N, 0);
        Assert.Equal(0.35,  rec.VoxelSize_mm, 2);
        Assert.Equal(1,     rec.TileCount);
        Assert.Equal(RecommendedResourceMode.Balanced, rec.ResourceMode);
    }

    [Fact]
    public void Recommend_RescalesPresetAtReferenceWorkstationBalanced()
    {
        // Calling Recommend with the historical 32 GB anchor rescales
        // the 96 GB-calibrated preset back up to a coarser voxel:
        // scaleFactor = (48/32)^(1/3) ≈ 1.1447 → 0.35 × 1.1447 ≈ 0.4007,
        // ceiled to 0.41 mm (Recommend rounds up to 0.01 mm).
        var rec = MegaScaleEnvelope.Recommend(
            thrust_N:     10_000,
            budgetBytes:  MegaScaleEnvelope.Budget_ReferenceWorkstation_Balanced);
        Assert.Equal(10_000.0, rec.Thrust_N, 0);
        Assert.Equal(0.41,  rec.VoxelSize_mm, 2);
        Assert.Equal(1,     rec.TileCount);
        Assert.Equal(RecommendedResourceMode.Balanced, rec.ResourceMode);
    }

    [Fact]
    public void Recommend_CoarsensVoxelOnSmallerBudget()
    {
        // 16 GB budget (Balanced on 32 GB machine) → voxel coarsens by
        // (48/16)^(1/3) ≈ 1.44× for the same thrust vs the 96 GB anchor.
        long budget16GB = 16L * 1024 * 1024 * 1024;
        var medium = MegaScaleEnvelope.Recommend(20_000, MegaScaleEnvelope.Budget_Current_Balanced);
        var tight  = MegaScaleEnvelope.Recommend(20_000, budget16GB);
        Assert.True(tight.VoxelSize_mm > medium.VoxelSize_mm,
            $"Smaller budget should recommend coarser voxel: tight {tight.VoxelSize_mm} vs medium {medium.VoxelSize_mm}");
    }

    [Fact]
    public void Recommend_EnableAutoCoarsenAboveFiftyKN()
    {
        Assert.False(MegaScaleEnvelope.Recommend(10_000).EnableAutoCoarsen);
        Assert.False(MegaScaleEnvelope.Recommend(50_000).EnableAutoCoarsen);
        Assert.True(MegaScaleEnvelope.Recommend(100_000).EnableAutoCoarsen);
        Assert.True(MegaScaleEnvelope.Recommend(500_000).EnableAutoCoarsen);
    }

    [Fact]
    public void Recommend_MeganewtonIsFeasibleOnLargeBudget()
    {
        // 500 kN at 128 GB (Maximum) should be feasible.
        long budget128GBMax = 120L * 1024 * 1024 * 1024;
        var rec = MegaScaleEnvelope.Recommend(500_000, budget128GBMax);
        Assert.True(rec.Feasible, rec.Rationale);
    }

    [Fact]
    public void Recommend_RationaleMentionsPeakAndBudget()
    {
        var rec = MegaScaleEnvelope.Recommend(100_000, MegaScaleEnvelope.Budget_Current_Balanced);
        Assert.Contains("GB", rec.Rationale);
        Assert.Contains("voxel", rec.Rationale);
        Assert.Contains("tiles", rec.Rationale);
    }

    [Theory]
    [InlineData(50.0)]        // below floor
    [InlineData(6_000_000.0)] // above ceiling
    public void Recommend_RejectsOutOfRangeThrust(double thrust)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MegaScaleEnvelope.Recommend(thrust));
    }

    [Fact]
    public void Recommend_RejectsNonPositiveBudget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MegaScaleEnvelope.Recommend(10_000, budgetBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MegaScaleEnvelope.Recommend(10_000, budgetBytes: -1));
    }

    // ══════════════════════ BuildSweep ══════════════════════

    [Fact]
    public void BuildSweep_ReturnsAtLeastOnePointAtCurrentBalanced()
    {
        var sweep = MegaScaleEnvelope.BuildSweep(MegaScaleEnvelope.Budget_Current_Balanced);
        Assert.NotEmpty(sweep);
        // Small thrust classes always fit.
        Assert.Contains(sweep, p => p.Thrust_N <= 20_000);
    }

    [Fact]
    public void BuildSweep_FiltersInfeasiblePointsOnTightBudget()
    {
        // 4 GB (Quiet mode on 8 GB machine) can't fit meganewton builds.
        long tinyBudget = 4L * 1024 * 1024 * 1024;
        var sweepTiny = MegaScaleEnvelope.BuildSweep(tinyBudget);
        var sweepFull = MegaScaleEnvelope.BuildSweep(MegaScaleEnvelope.Budget_Current_Maximum);
        Assert.True(sweepTiny.Length < sweepFull.Length,
            $"Tiny sweep ({sweepTiny.Length}) should trim meganewton points vs full ({sweepFull.Length}).");
    }

    [Fact]
    public void BuildSweep_IsAscendingInThrust()
    {
        var sweep = MegaScaleEnvelope.BuildSweep(MegaScaleEnvelope.Budget_Current_Maximum);
        for (int i = 1; i < sweep.Length; i++)
            Assert.True(sweep[i].Thrust_N >= sweep[i - 1].Thrust_N,
                $"Sweep not ascending at index {i}: {sweep[i - 1].Thrust_N} → {sweep[i].Thrust_N}");
    }

    // ══════════════════════ Consistency with MemoryProjectionGate ══════════════════════

    [Fact]
    public void Recommend_PeakBytesMatchesGateAtSameBbox()
    {
        // Forcing function: Recommend's peak-memory field must equal
        // MemoryProjectionGate.Project at the bbox EstimateBoundingBox
        // returns. If the two drift, pre-flight UI and the envelope
        // advisor will give conflicting numbers.
        double thrust = 100_000;
        long budget = MegaScaleEnvelope.Budget_Current_Balanced;
        var rec = MegaScaleEnvelope.Recommend(thrust, budget);
        var bbox = MegaScaleEnvelope.EstimateBoundingBox(thrust);
        var gate = MemoryProjectionGate.Project(
            bbox.Lx_mm, bbox.Ly_mm, bbox.Lz_mm, rec.VoxelSize_mm, budget);
        Assert.Equal(gate.ProjectedBytes, rec.ProjectedPeakBytes);
    }

    // ══════════════════════ Preset table invariants ══════════════════════
    // Both tiers (canonical PresetsCurrent + historical PresetsReferenceWorkstation)
    // share the same shape invariants. The Theory exercises both.

    public static IEnumerable<object[]> AllPresetTables()
    {
        yield return new object[] { "Current",              MegaScaleEnvelope.PresetsCurrent };
        yield return new object[] { "ReferenceWorkstation", MegaScaleEnvelope.PresetsReferenceWorkstation };
    }

    [Theory]
    [MemberData(nameof(AllPresetTables))]
    public void Presets_AreSortedAscendingByThrust(
        string tier,
        (double Thrust_N, double Voxel_mm, int Tiles, RecommendedResourceMode Mode)[] presets)
    {
        for (int i = 1; i < presets.Length; i++)
            Assert.True(presets[i].Thrust_N > presets[i - 1].Thrust_N,
                $"[{tier}] Presets not ascending at {i}: {presets[i - 1].Thrust_N} → {presets[i].Thrust_N}");
    }

    [Theory]
    [MemberData(nameof(AllPresetTables))]
    public void Presets_VoxelSizeMonotonicallyGrows(
        string tier,
        (double Thrust_N, double Voxel_mm, int Tiles, RecommendedResourceMode Mode)[] presets)
    {
        // As thrust grows, voxel must coarsen (otherwise the projection
        // math breaks).
        for (int i = 1; i < presets.Length; i++)
            Assert.True(presets[i].Voxel_mm >= presets[i - 1].Voxel_mm,
                $"[{tier}] Voxel not monotonically coarsening at {i}: "
              + $"{presets[i - 1].Voxel_mm} → {presets[i].Voxel_mm}");
    }

    [Theory]
    [MemberData(nameof(AllPresetTables))]
    public void Presets_ThrustsWithinValidRange(
        string tier,
        (double Thrust_N, double Voxel_mm, int Tiles, RecommendedResourceMode Mode)[] presets)
    {
        foreach (var p in presets)
            Assert.True(
                p.Thrust_N >= MegaScaleEnvelope.MinThrust_N
             && p.Thrust_N <= MegaScaleEnvelope.MaxThrust_N,
                $"[{tier}] preset thrust {p.Thrust_N:F0} N outside envelope "
              + $"[{MegaScaleEnvelope.MinThrust_N}, {MegaScaleEnvelope.MaxThrust_N}] N.");
    }
}
