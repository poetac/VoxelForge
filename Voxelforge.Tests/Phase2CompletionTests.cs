// Phase2CompletionTests.cs — Contract tests for the filter
// contamination preset library + clean / dirty ΔP model wired into
// PressureStackup, schema v7 → v8.
//
// All changes are additive and backward-compatible: every new
// field on OperatingConditions defaults to a value that reproduces
// v7 behaviour exactly (FilterStandard.Custom + 0 % contamination
// passes the existing FilterDeltaP_Pa scalar straight through).

using Voxelforge.FeedSystem;
using Voxelforge.IO;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class Phase2CompletionTests
{
    // ─────────────────────────────────────────────────────────────────
    //  FilterPresets library
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FilterPresets_CoverAllEnumValues()
    {
        foreach (FilterStandard s in System.Enum.GetValues<FilterStandard>())
            Assert.True(FilterPresets.All.ContainsKey(s), $"Missing preset for {s}.");
    }

    [Fact]
    public void FilterPresets_None_ReturnsZero()
    {
        double dp = FilterPresets.EffectiveDeltaP_Pa(
            FilterStandard.None, customCleanDP_Pa: 50_000, contaminationFraction: 1.0);
        Assert.Equal(0.0, dp, precision: 6);
    }

    [Theory]
    [InlineData(FilterStandard.CoarseMesh_100um)]
    [InlineData(FilterStandard.Standard_40um)]
    [InlineData(FilterStandard.Fine_25um)]
    [InlineData(FilterStandard.UltraFine_10um)]
    public void FilterPresets_CleanFraction_MatchesNominalCleanDP(FilterStandard s)
    {
        var spec = FilterPresets.SpecFor(s);
        double dp = FilterPresets.EffectiveDeltaP_Pa(s, customCleanDP_Pa: 9_999_999,
                                                       contaminationFraction: 0.0);
        Assert.Equal(spec.NominalCleanDP_Pa, dp, precision: 3);
    }

    [Theory]
    [InlineData(FilterStandard.CoarseMesh_100um)]
    [InlineData(FilterStandard.Standard_40um)]
    [InlineData(FilterStandard.Fine_25um)]
    [InlineData(FilterStandard.UltraFine_10um)]
    public void FilterPresets_DirtyFraction_MatchesCleanTimesMultiplier(FilterStandard s)
    {
        var spec = FilterPresets.SpecFor(s);
        double dp = FilterPresets.EffectiveDeltaP_Pa(s, customCleanDP_Pa: 0,
                                                       contaminationFraction: 1.0);
        double expected = spec.NominalCleanDP_Pa * spec.DirtyMultiplier;
        Assert.Equal(expected, dp, precision: 3);
    }

    [Fact]
    public void FilterPresets_LinearLoadingBetweenCleanAndDirty()
    {
        var spec = FilterPresets.SpecFor(FilterStandard.Standard_40um);
        double clean = FilterPresets.EffectiveDeltaP_Pa(
            FilterStandard.Standard_40um, customCleanDP_Pa: 0, contaminationFraction: 0.0);
        double half  = FilterPresets.EffectiveDeltaP_Pa(
            FilterStandard.Standard_40um, customCleanDP_Pa: 0, contaminationFraction: 0.5);
        double dirty = FilterPresets.EffectiveDeltaP_Pa(
            FilterStandard.Standard_40um, customCleanDP_Pa: 0, contaminationFraction: 1.0);
        Assert.Equal(0.5 * (clean + dirty), half, precision: 3);
        Assert.True(dirty > clean,
            $"Dirty ΔP should exceed clean. clean={clean:E2} dirty={dirty:E2}");
    }

    [Fact]
    public void FilterPresets_Custom_PassesThroughUserScalar()
    {
        // Default contamination=0 should reproduce the v7 scalar exactly.
        const double user = 137_500;
        double dp = FilterPresets.EffectiveDeltaP_Pa(
            FilterStandard.Custom, customCleanDP_Pa: user, contaminationFraction: 0.0);
        Assert.Equal(user, dp, precision: 3);
    }

    [Fact]
    public void FilterPresets_Custom_AppliesGenericDirtyMultiplier()
    {
        const double user = 100_000;
        double dp = FilterPresets.EffectiveDeltaP_Pa(
            FilterStandard.Custom, customCleanDP_Pa: user, contaminationFraction: 1.0);
        Assert.Equal(user * FilterPresets.CustomDirtyMultiplier, dp, precision: 3);
    }

    [Fact]
    public void FilterPresets_ContaminationClampedToZeroOne()
    {
        var spec = FilterPresets.SpecFor(FilterStandard.Standard_40um);
        double over   = FilterPresets.EffectiveDeltaP_Pa(
            FilterStandard.Standard_40um, customCleanDP_Pa: 0, contaminationFraction: 99);
        double under  = FilterPresets.EffectiveDeltaP_Pa(
            FilterStandard.Standard_40um, customCleanDP_Pa: 0, contaminationFraction: -1);
        Assert.Equal(spec.NominalCleanDP_Pa * spec.DirtyMultiplier, over, precision: 3);
        Assert.Equal(spec.NominalCleanDP_Pa, under, precision: 3);
    }

    [Fact]
    public void FilterPresets_FinerRating_HasHigherCleanDP()
    {
        // Physically: tighter filtration → smaller pore → higher ΔP.
        double coarseDP = FilterPresets.SpecFor(FilterStandard.CoarseMesh_100um).NominalCleanDP_Pa;
        double fineDP   = FilterPresets.SpecFor(FilterStandard.UltraFine_10um  ).NominalCleanDP_Pa;
        Assert.True(fineDP > coarseDP,
            $"Ultra-fine 10 µm clean ΔP should exceed coarse 100 µm. coarse={coarseDP:E2} ultra={fineDP:E2}");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Wiring into PressureStackup
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Stackup_FilterSegment_LabelChangesWithPreset()
    {
        var (cond, design) = BaselineWithStackup();
        cond = cond with { FilterStandard = FilterStandard.Fine_25um };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var stackup = gen.FeedStackup;
        Assert.NotNull(stackup);
        Assert.Contains(stackup!.Segments,
            s => s.Name.Contains("25 µm", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Stackup_DirtyFilter_DropsPredictedPcMoreThanClean()
    {
        var (cond, design) = BaselineWithStackup();
        cond = cond with { FilterStandard = FilterStandard.Standard_40um };

        var clean = RegenChamberOptimization.GenerateWith(
            cond with { FilterContaminationFraction = 0.0 }, design, skipVoxelGeometry: true);
        var dirty = RegenChamberOptimization.GenerateWith(
            cond with { FilterContaminationFraction = 1.0 }, design, skipVoxelGeometry: true);

        Assert.NotNull(clean.FeedStackup);
        Assert.NotNull(dirty.FeedStackup);
        Assert.True(dirty.FeedStackup!.PredictedChamberPressure_Pa
                    < clean.FeedStackup!.PredictedChamberPressure_Pa,
            $"End-of-life filter should drop Pc more than fresh element. "
            + $"clean={clean.FeedStackup.PredictedChamberPressure_Pa:E2} "
            + $"dirty={dirty.FeedStackup.PredictedChamberPressure_Pa:E2}");
    }

    [Fact]
    public void Stackup_CustomMode_HonorsLegacyScalar()
    {
        // v7-era saved design: FilterStandard defaults to Custom, scalar
        // controls the ΔP. This is the back-compat path.
        var (cond, design) = BaselineWithStackup();
        cond = cond with
        {
            FilterStandard = FilterStandard.Custom,
            FilterDeltaP_Pa = 77_777,
            FilterContaminationFraction = 0,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var stackup = gen.FeedStackup!;
        var filterSeg = System.Array.Find(stackup.Segments,
            s => s.Name.StartsWith("Filter", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(default, filterSeg);
        Assert.Equal(77_777.0, filterSeg.DeltaP_Pa, precision: 0);
    }

    [Fact]
    public void Stackup_PresetMode_OverridesScalar()
    {
        // When a named preset is selected, the user's FilterDeltaP_Pa
        // scalar must be IGNORED — the preset's clean ΔP wins.
        var (cond, design) = BaselineWithStackup();
        cond = cond with
        {
            FilterStandard = FilterStandard.Standard_40um,
            FilterDeltaP_Pa = 9_999_999,           // big bogus scalar
            FilterContaminationFraction = 0,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var stackup = gen.FeedStackup!;
        var filterSeg = System.Array.Find(stackup.Segments,
            s => s.Name.StartsWith("Filter", System.StringComparison.OrdinalIgnoreCase));
        var spec = FilterPresets.SpecFor(FilterStandard.Standard_40um);
        Assert.Equal(spec.NominalCleanDP_Pa, filterSeg.DeltaP_Pa, precision: 0);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Schema v7 → v8 migration
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Schema_V8_IsInChain()
    {
        // v8 (filter presets) preceded v9 (ablative, chilldown, start
        // transient, turbopump). v8 stays in the migration chain so v8
        // saved files migrate forward cleanly.
        Assert.Contains("v8", DesignPersistence.KnownSchemas);
    }

    [Fact]
    public void OperatingConditions_NewFilterFields_PreserveV7Behaviour()
    {
        var c = new OperatingConditions();
        Assert.Equal(FilterStandard.Custom, c.FilterStandard);   // back-compat path
        Assert.Equal(0.0, c.FilterContaminationFraction);
        // Existing scalar default unchanged.
        Assert.Equal(100_000.0, c.FilterDeltaP_Pa);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private static (OperatingConditions cond, RegenChamberDesign design) BaselineWithStackup()
    {
        var cond = new OperatingConditions
        {
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
            TankUllagePressure_Pa = 1.5e7,   // opt the stackup in
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 40,
        };
        return (cond, design);
    }
}
