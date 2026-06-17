// VoxelAdequacyGateTests.cs — Contract tests for UPGRADE 4: voxel-resolution
// adequacy gate. The gate is a pure geometric check (no PicoGK calls) that
// verifies critical features are resolved by ≥ 2 voxels (Fail), ≥ 3 voxels
// (Marginal), or ≥ 3 voxels comfortably (Pass).
//
// Test strategy: construct ChannelSchedule + ChamberContour values from first
// principles rather than running GenerateWith, so each test is deterministic
// and sub-millisecond fast.

using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class VoxelAdequacyGateTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A minimal ChamberContour with a single station at radius R_mm.
    /// Enough for VoxelAdequacyGate to compute the min channel width.
    /// </summary>
    private static ChamberContour SingleStationContour(double R_mm)
    {
        var station = new ContourStation(
            X_mm:    0.0,
            R_mm:    R_mm,
            Area_mm2: Math.PI * R_mm * R_mm,
            Slope:   0.0,
            Region:  ChamberRegion.Barrel);

        return new ChamberContour(
            Stations:              new[] { station },
            ThroatIndex:           0,
            ThroatRadius_mm:       R_mm,
            ThroatArea_mm2:        Math.PI * R_mm * R_mm,
            ChamberRadius_mm:      R_mm,
            ExitRadius_mm:         R_mm,
            ContractionRatio:      1.0,
            ExpansionRatio:        1.0,
            ChamberLength_mm:      0.0,
            ConvergingLength_mm:   0.0,
            BellLength_mm:         0.0,
            TotalLength_mm:        0.0,
            ChamberVolume_mm3:     0.0,
            CharacteristicLength_m: 1.0);
    }

    /// <summary>
    /// Build a ChannelSchedule where all heights are set to the same value for simplicity.
    /// </summary>
    private static ChannelSchedule Channels(
        int    n,
        double wallMM,
        double ribMM,
        double heightMM) => new ChannelSchedule(
            ChannelCount:              n,
            RibThickness_mm:           ribMM,
            GasSideWallThickness_mm:   wallMM,
            ChannelHeightAtChamber_mm: heightMM,
            ChannelHeightAtThroat_mm:  heightMM,
            ChannelHeightAtExit_mm:    heightMM);

    // ─────────────────────────────────────────────────────────────────
    //  1. Well-resolved design (all features ≥ 3× voxel) → Pass
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void VoxelGate_AllFeaturesBeyond3Voxels_PassesOverall()
    {
        // Voxel = 0.4 mm; wall=1.5 mm, rib=1.5 mm, height=3.0 mm
        // Ratios: 1.5/0.4 = 3.75 (Pass), 3.0/0.4 = 7.5 (Pass)
        // Channel width at R=20mm: 2π×(20+1.5)/40 - 1.5 = 3.38 - 1.5 = 1.88mm → 4.7×voxel (Pass)
        double voxel    = 0.4;
        var channels    = Channels(n: 40, wallMM: 1.5, ribMM: 1.5, heightMM: 3.0);
        var contour     = SingleStationContour(R_mm: 20.0);

        var result = VoxelAdequacyGate.Evaluate(channels, contour, voxel);

        Assert.Equal(VoxelAdequacyLevel.Pass, result.Overall);
        Assert.Equal(4, result.Features.Length);   // wall, rib, height, width
        Assert.All(result.Features, f => Assert.Equal(VoxelAdequacyLevel.Pass, f.Level));
    }

    // ─────────────────────────────────────────────────────────────────
    //  2. Rib exactly between 2× and 3× → Marginal overall
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void VoxelGate_RibInMarginalBand_MarginalOverall()
    {
        // Voxel = 0.4 mm; rib = 1.0 mm → ratio = 2.5 → Marginal
        // All other features set large enough to Pass individually.
        double voxel = 0.4;
        var channels = Channels(n: 20, wallMM: 2.0, ribMM: 1.0, heightMM: 3.0);
        var contour  = SingleStationContour(R_mm: 30.0);
        // Channel width: 2π×32/20 - 1.0 = 10.05 - 1.0 = 9.05mm → 22.6× → Pass

        var result = VoxelAdequacyGate.Evaluate(channels, contour, voxel);

        Assert.Equal(VoxelAdequacyLevel.Marginal, result.Overall);
        // Rib should be marginal, others Pass
        var rib = Assert.Single(result.Features, f => f.FeatureName == "RibThickness");
        Assert.Equal(VoxelAdequacyLevel.Marginal, rib.Level);
        Assert.Equal(1.0 / voxel, rib.VoxelRatio, precision: 4);
    }

    // ─────────────────────────────────────────────────────────────────
    //  3. Wall thickness below 2× voxel → Fail overall
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void VoxelGate_WallBelowTwoVoxels_FailsOverall()
    {
        // Voxel = 0.4 mm; wall = 0.7 mm → ratio = 1.75 → Fail
        double voxel = 0.4;
        var channels = Channels(n: 20, wallMM: 0.7, ribMM: 1.5, heightMM: 3.0);
        var contour  = SingleStationContour(R_mm: 25.0);

        var result = VoxelAdequacyGate.Evaluate(channels, contour, voxel);

        Assert.Equal(VoxelAdequacyLevel.Fail, result.Overall);
        var wall = Assert.Single(result.Features, f => f.FeatureName == "GasSideWall");
        Assert.Equal(VoxelAdequacyLevel.Fail, wall.Level);
        Assert.True(wall.VoxelRatio < VoxelAdequacyGate.FailRatioThreshold);
    }

    // ─────────────────────────────────────────────────────────────────
    //  4. Per-feature ratios are computed correctly (closed-form check).
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void VoxelGate_PerFeatureRatios_MatchExpectedFormulas()
    {
        double voxel    = 0.5;
        double wall     = 1.5;  // ratio = 3.0 → Pass
        double rib      = 1.2;  // ratio = 2.4 → Marginal
        double height   = 2.0;  // ratio = 4.0 → Pass
        int    n        = 30;
        double R        = 15.0; // mm

        var channels = Channels(n: n, wallMM: wall, ribMM: rib, heightMM: height);
        var contour  = SingleStationContour(R_mm: R);

        var result = VoxelAdequacyGate.Evaluate(channels, contour, voxel);

        // Verify individual ratios
        Assert.Equal(wall   / voxel, result.Features.Single(f => f.FeatureName == "GasSideWall").VoxelRatio, precision: 4);
        Assert.Equal(rib    / voxel, result.Features.Single(f => f.FeatureName == "RibThickness").VoxelRatio, precision: 4);
        Assert.Equal(height / voxel, result.Features.Single(f => f.FeatureName == "ChannelHtThroat").VoxelRatio, precision: 4);

        // MinChannelWidth: 2π×(R+wall)/n - rib
        double expectedWidth = 2.0 * Math.PI * (R + wall) / n - rib;
        double widthRatio    = expectedWidth / voxel;
        Assert.Equal(widthRatio, result.Features.Single(f => f.FeatureName == "MinChannelWidth").VoxelRatio, precision: 4);
    }

    // ─────────────────────────────────────────────────────────────────
    //  5. GenerateWith with voxelSize_mm > 0 attaches VoxelAdequacy to result.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateWith_WithVoxelSize_AttachesVoxelAdequacyResult()
    {
        var cond = new OperatingConditions
        {
            Thrust_N              = 2224.0,
            ChamberPressure_Pa    = 6.9e6,
            MixtureRatio          = 3.3,
            CoolantInletTemp_K    = 150.0,
            CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex     = 1,
            PropellantPair        = Voxelforge.Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 60,
        };

        double voxel = 0.4;
        var result = RegenChamberOptimization.GenerateWith(cond, design, voxelSize_mm: voxel);

        Assert.NotNull(result.VoxelAdequacy);
        Assert.Equal(voxel, result.VoxelAdequacy!.VoxelSize_mm, precision: 4);
        Assert.Equal(4, result.VoxelAdequacy.Features.Length);

        // All per-feature sizes should match the design's channel schedule
        Assert.Contains(result.VoxelAdequacy.Features,
            f => f.FeatureName == "GasSideWall" && Math.Abs(f.FeatureSize_mm - design.GasSideWallThickness_mm) < 0.001);
    }

    // ─────────────────────────────────────────────────────────────────
    //  6. GenerateWith WITHOUT voxelSize_mm leaves VoxelAdequacy null.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateWith_WithoutVoxelSize_VoxelAdequacyIsNull()
    {
        var cond = new OperatingConditions
        {
            Thrust_N              = 2224.0,
            ChamberPressure_Pa    = 6.9e6,
            MixtureRatio          = 3.3,
            CoolantInletTemp_K    = 150.0,
            CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex     = 1,
            PropellantPair        = Voxelforge.Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 60,
        };

        var result = RegenChamberOptimization.GenerateWith(cond, design);   // no voxelSize_mm
        Assert.Null(result.VoxelAdequacy);
    }

    // ─────────────────────────────────────────────────────────────────
    //  7. Evaluate returns +∞ when VoxelAdequacy.Overall == Fail.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_VoxelAdequacyFail_ReturnsInfinityScore()
    {
        var cond = new OperatingConditions
        {
            Thrust_N              = 2224.0,
            ChamberPressure_Pa    = 6.9e6,
            MixtureRatio          = 3.3,
            CoolantInletTemp_K    = 150.0,
            CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex     = 1,
            PropellantPair        = Voxelforge.Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 60,
        };

        // Get a real result, then inject a failing VoxelAdequacyResult.
        // Use SafeResult approach from FeasibilityGateTests: clamp physics metrics
        // to safe values so the ONLY violation is the voxel adequacy.
        var mat = Voxelforge.HeatTransfer.WallMaterials.All[1];
        var ch4 = Voxelforge.Coolant.MethaneFluid.Instance;
        var raw = RegenChamberOptimization.GenerateWith(cond, design);
        var safe = raw with
        {
            Thermal = raw.Thermal with
            {
                PeakGasSideWallT_K   = mat.MaxServiceTemp_K - 200.0,
                WallTempExceedsLimit = false,
                CoolantOutletT_K     = ch4.Metadata.MaxBulkT_K - 100.0,
            },
            Stress = raw.Stress with { MinSafetyFactor = 2.5, YieldExceeded = false },
            Manufacturing = raw.Manufacturing with { MinFeatureSize_mm = 0.55, FeatureSizeOK = true },
            Stability = raw.Stability with
            {
                Composite       = Voxelforge.Combustion.Stability.StabilityRating.Pass,
                CompositeReason = "test-injected",
            },
        };

        // Now inject a failing voxel adequacy result (wall < 2× voxel)
        var failFeature = new FeatureAdequacy("GasSideWall", 0.3, 0.3 / 0.4, VoxelAdequacyLevel.Fail);
        var failVoxel = new VoxelAdequacyResult(
            VoxelAdequacyLevel.Fail,
            new[] { failFeature },
            VoxelSize_mm: 0.4);

        var withBadVoxel = safe with { VoxelAdequacy = failVoxel };

        // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var score = RegenChamberOptimization.Evaluate(withBadVoxel, RegenChamberOptimization.Profiles[0]);
        Assert.Equal(double.PositiveInfinity, score.TotalScore);
        Assert.Contains(score.FeasibilityViolations, v => v.ConstraintId == "VOXEL_RESOLUTION");
    }
}
