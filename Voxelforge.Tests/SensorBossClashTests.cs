// SensorBossClashTests.cs — Sprint 28 (2026-04-24):
// xUnit coverage for the SensorBossClashEvaluator + the
// INSTRUMENTATION_TAP_INTERFERENCE feasibility gate. Voxel-free per
// ADR-005 — no PicoGK.Library instantiation.
//
// Coverage
//   1. Empty / no-topology paths: boss list empty → no clashes;
//      non-axial topology → channel check skipped.
//   2. Channel overlap: boss placed at channel centerline θ=0 with
//      low channel count fires the gate; boss shifted to mid-rib
//      passes.
//   3. Boss-vs-boss overlap: two bosses at identical (axial, azimuth)
//      fires; separating them axially past their OD sum passes.
//   4. End-to-end: GenerateWith pipes SensorBosses + ChannelCount
//      through to the result; FeasibilityGate.Evaluate surfaces the
//      violation.

using System.Collections.Generic;
using Voxelforge.Chamber;
using Voxelforge.Geometry;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class SensorBossClashTests
{
    private static ChamberContour FixtureContour()
        => ChamberContourGenerator.Generate(
            throatRadius_mm:          10.0,
            contractionRatio:         6.0,
            expansionRatio:           8.0,
            characteristicLength_m:   1.1,
            thetaN_deg:               30.0,
            thetaE_deg:               10.0,
            bellLengthFraction:       0.8,
            stationCount:             80);

    // ═══════════════════════════════════════════════════════════════
    //   No-op paths: empty list, no channels, non-axial topology
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_EmptyBosses_ReturnsEmpty()
    {
        var reports = SensorBossClashEvaluator.Evaluate(
            bosses:       System.Array.Empty<SensorBoss>(),
            channelCount: 80,
            topology:     ChannelTopology.Axial,
            contour:      FixtureContour());
        Assert.Empty(reports);
    }

    [Fact]
    public void Evaluate_NullBosses_ReturnsEmpty()
    {
        var reports = SensorBossClashEvaluator.Evaluate(
            bosses:       null,
            channelCount: 80,
            topology:     ChannelTopology.Axial,
            contour:      FixtureContour());
        Assert.Empty(reports);
    }

    [Fact]
    public void Evaluate_HelicalTopology_SkipsChannelCheck()
    {
        // Boss placed exactly at θ=0 would clash on axial. Helical
        // topology must not flag it — the clash evaluator's scope
        // doesn't include helical channels (documented limitation).
        var bosses = new[]
        {
            new SensorBoss(AxialFraction: 0.5, AzimuthDeg: 0.0, Type: SensorBossType.Pressure_M5),
        };
        var reports = SensorBossClashEvaluator.Evaluate(
            bosses:       bosses,
            channelCount: 80,
            topology:     ChannelTopology.Helical,
            contour:      FixtureContour());
        Assert.Empty(reports);
    }

    [Fact]
    public void Evaluate_NoneTopology_SkipsChannelCheck()
    {
        // Ablative-only design: no regen jacket, no channels.
        var bosses = new[]
        {
            new SensorBoss(AxialFraction: 0.5, AzimuthDeg: 0.0, Type: SensorBossType.Pressure_M5),
        };
        var reports = SensorBossClashEvaluator.Evaluate(
            bosses:       bosses,
            channelCount: 0,
            topology:     ChannelTopology.None,
            contour:      FixtureContour());
        Assert.Empty(reports);
    }

    // ═══════════════════════════════════════════════════════════════
    //   Channel-overlap check (axial topology)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_BossOnChannelCenterline_FiresChannelOverlap()
    {
        // With 8 channels (pitch = 45°) a boss at θ = 0° sits exactly
        // on channel 0. Arc distance to that channel is 0 — well below
        // any non-trivial clearance threshold.
        var bosses = new[]
        {
            new SensorBoss(AxialFraction: 0.5, AzimuthDeg: 0.0, Type: SensorBossType.Pressure_M5),
        };
        var reports = SensorBossClashEvaluator.Evaluate(
            bosses:       bosses,
            channelCount: 8,
            topology:     ChannelTopology.Axial,
            contour:      FixtureContour());
        var clash = Assert.Single(reports);
        Assert.Equal(SensorBossClashKind.ChannelOverlap, clash.Kind);
        Assert.Equal(0, clash.BossIndex);
        Assert.Equal(0, clash.OtherIndex);
        Assert.True(clash.ArcDistance_mm < clash.MinClearance_mm,
            $"expected arc < clearance; got {clash.ArcDistance_mm} vs {clash.MinClearance_mm}");
    }

    [Fact]
    public void Evaluate_BossAtMidRib_DoesNotFire()
    {
        // With 8 channels (pitch = 45°) a boss at θ = 22.5° sits at
        // the midpoint between channels 0 and 1 — maximum possible
        // clearance. At chamber-radius scale (~25 mm) that's ~9 mm
        // arc distance, well above any clearance threshold.
        var bosses = new[]
        {
            new SensorBoss(AxialFraction: 0.5, AzimuthDeg: 22.5, Type: SensorBossType.Pressure_M5),
        };
        var reports = SensorBossClashEvaluator.Evaluate(
            bosses:       bosses,
            channelCount: 8,
            topology:     ChannelTopology.Axial,
            contour:      FixtureContour());
        Assert.Empty(reports);
    }

    [Fact]
    public void Evaluate_HighChannelCount_CompressesRib_MayFire()
    {
        // With many channels (N=180, pitch = 2°) the half-pitch
        // arc at chamber radius is small — even a boss placed at
        // mid-rib has little room. A 7-mm boss OD is bigger than
        // the available rib width, so the clearance floor is
        // violated regardless of azimuth within that pitch.
        var bosses = new[]
        {
            new SensorBoss(AxialFraction: 0.5, AzimuthDeg: 1.0, Type: SensorBossType.Pressure_M5),
        };
        var reports = SensorBossClashEvaluator.Evaluate(
            bosses:       bosses,
            channelCount: 180,
            topology:     ChannelTopology.Axial,
            contour:      FixtureContour());
        Assert.NotEmpty(reports);
        Assert.Equal(SensorBossClashKind.ChannelOverlap, reports[0].Kind);
    }

    // ═══════════════════════════════════════════════════════════════
    //   Boss-vs-boss overlap check
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_TwoBossesCoincident_FiresBossOverlap()
    {
        // Two bosses at the same (axialFraction, azimuth): every
        // clearance floor violated. The channel check may or may not
        // also fire depending on angle; boss-overlap must always fire.
        var bosses = new[]
        {
            new SensorBoss(AxialFraction: 0.5, AzimuthDeg: 22.5, Type: SensorBossType.Pressure_M5),
            new SensorBoss(AxialFraction: 0.5, AzimuthDeg: 22.5, Type: SensorBossType.Thermocouple_1_8_NPT),
        };
        var reports = SensorBossClashEvaluator.Evaluate(
            bosses:       bosses,
            channelCount: 8,
            topology:     ChannelTopology.Axial,
            contour:      FixtureContour());
        Assert.Contains(reports, r => r.Kind == SensorBossClashKind.BossOverlap
                                    && r.BossIndex == 0
                                    && r.OtherIndex == 1);
    }

    [Fact]
    public void Evaluate_TwoBossesOnOppositeSides_NoOverlap()
    {
        // Bosses at θ = 22.5° and θ = 202.5° (180° apart) are on
        // opposite sides of the chamber. Arc distance ≈ π·R (chamber-
        // radius scale) — way above any clearance threshold.
        var bosses = new[]
        {
            new SensorBoss(AxialFraction: 0.5, AzimuthDeg: 22.5,  Type: SensorBossType.Pressure_M5),
            new SensorBoss(AxialFraction: 0.5, AzimuthDeg: 202.5, Type: SensorBossType.Thermocouple_1_8_NPT),
        };
        var reports = SensorBossClashEvaluator.Evaluate(
            bosses:       bosses,
            channelCount: 8,
            topology:     ChannelTopology.Axial,
            contour:      FixtureContour());
        // Neither boss should overlap with a channel (both at mid-rib)
        // AND they shouldn't overlap with each other.
        Assert.DoesNotContain(reports, r => r.Kind == SensorBossClashKind.BossOverlap);
    }

    [Fact]
    public void Evaluate_TwoBossesAxiallySeparated_NoOverlap()
    {
        // Same azimuth but different axial stations — far enough
        // apart in x that the two bores don't share any volume. The
        // evaluator's "axialGap > halfOdSum" short-circuit kicks in.
        var bosses = new[]
        {
            new SensorBoss(AxialFraction: 0.1, AzimuthDeg: 22.5, Type: SensorBossType.Pressure_M5),
            new SensorBoss(AxialFraction: 0.9, AzimuthDeg: 22.5, Type: SensorBossType.Pressure_M5),
        };
        var reports = SensorBossClashEvaluator.Evaluate(
            bosses:       bosses,
            channelCount: 8,
            topology:     ChannelTopology.Axial,
            contour:      FixtureContour());
        Assert.DoesNotContain(reports, r => r.Kind == SensorBossClashKind.BossOverlap);
    }

    // ═══════════════════════════════════════════════════════════════
    //   End-to-end through GenerateWith + FeasibilityGate.Evaluate
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Gate_FiresOnChannelOverlap_EndToEnd()
    {
        var cond = new OperatingConditions
        {
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            ChannelCount          = 8,
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 40,
            SensorBosses          = new SensorBoss[]
            {
                new(AxialFraction: 0.5, AzimuthDeg: 0.0, Type: SensorBossType.Pressure_M5),
            },
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var feas = FeasibilityGate.Evaluate(gen);
        Assert.Contains(feas.Violations,
            v => v.ConstraintId == "INSTRUMENTATION_TAP_INTERFERENCE");
    }

    [Fact]
    public void Gate_SilentOnFreshDesign_NoBosses()
    {
        // Every pre-Sprint-28 design had no sensor bosses. The gate
        // must stay silent when the list is empty.
        var cond = new OperatingConditions
        {
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 40,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var feas = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "INSTRUMENTATION_TAP_INTERFERENCE");
    }

    [Fact]
    public void Gate_SilentOnSafeBossPlacement()
    {
        // Boss at mid-rib with low channel count — safe.
        var cond = new OperatingConditions
        {
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            ChannelCount          = 8,
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 40,
            SensorBosses          = new SensorBoss[]
            {
                new(AxialFraction: 0.5, AzimuthDeg: 22.5, Type: SensorBossType.Pressure_M5),
            },
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var feas = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "INSTRUMENTATION_TAP_INTERFERENCE");
    }

    [Fact]
    public void Gate_ReportsMultipleBosses_OneViolationEach()
    {
        // Three bosses, two of which clash with channels. The gate
        // should emit one violation per bad boss so the UI can list
        // every offender, not just the first.
        var cond = new OperatingConditions
        {
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            ChannelCount          = 8,
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 40,
            SensorBosses          = new SensorBoss[]
            {
                new(AxialFraction: 0.3, AzimuthDeg: 0.0,  Type: SensorBossType.Pressure_M5),    // clash
                new(AxialFraction: 0.5, AzimuthDeg: 22.5, Type: SensorBossType.Thermocouple_1_8_NPT), // safe
                new(AxialFraction: 0.7, AzimuthDeg: 45.0, Type: SensorBossType.StaticTap_G_1_16), // clash (on channel 1)
            },
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var feas = FeasibilityGate.Evaluate(gen);
        var clashes = new List<FeasibilityViolation>();
        foreach (var v in feas.Violations)
            if (v.ConstraintId == "INSTRUMENTATION_TAP_INTERFERENCE")
                clashes.Add(v);
        Assert.Equal(2, clashes.Count);
    }
}
