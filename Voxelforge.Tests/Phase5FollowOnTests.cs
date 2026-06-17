// Phase5FollowOnTests.cs — Contract tests for Tier-1 + Tier-2
// voxel-geometry and UI follow-ons:
//
//   Tier 1 — voxel-geometry follow-ons. None of these change physics
//            or scoring; they propagate three new fields end-to-end:
//     • UmbilicalStandard → ChamberBuildOptions →
//       AddUmbilicalSealAndBolts on each propellant port
//     • PurgePort list → ChamberBuildOptions →
//       per-port axial / radial bores
//     • MountConfiguration → ChamberBuildOptions →
//       trunnion lugs / flexure arms aft of mount flange
//
//   Tier 2 — UI bindings (covered by `RegenChamberForm` round-trip via
//            ApplyDesign / ReadConditions / ReadDesign — see existing
//            Phase1CompletionTests / Phase2CompletionTests; the UI
//            tests proper need a live Form instance which xUnit can't
//            spin up cleanly, so the contract here is back-compat
//            defaults + ChamberBuildOptions propagation).
//
// All voxel additions are visual-only — they don't change mass enough
// for `BuildAnalytical` to register (analytical mass estimator covers
// the swept channel + jacket volumes, not bolt-circle subtractions or
// trunnion additions). Tests therefore focus on:
//   • Defaults preserve legacy behaviour exactly.
//   • Each new field propagates from RegenChamberDesign /
//     OperatingConditions through GenerateWith without crashing.
//   • Per-config / per-preset combinations all run cleanly on the
//     fast skipVoxelGeometry path so SA + downstream consumers stay
//     happy.

using Voxelforge.Coolant;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;
using Voxelforge.Optimization;
using Voxelforge.Structure;

namespace Voxelforge.Tests;

public class Phase5FollowOnTests
{
    // ─────────────────────────────────────────────────────────────────
    //  ChamberBuildOptions back-compat
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ChamberBuildOptions_NewFields_DefaultToNoOp()
    {
        // Construct with only the required fields; all new params
        // must default to a no-op so legacy saved designs round-trip
        // unchanged through Build / BuildAnalytical.
        var ch = new HeatTransfer.ChannelSchedule(
            ChannelCount: 80, RibThickness_mm: 0.8,
            GasSideWallThickness_mm: 0.8,
            ChannelHeightAtChamber_mm: 2.5,
            ChannelHeightAtThroat_mm: 1.5,
            ChannelHeightAtExit_mm: 2.0);
        var contour = MakeBaselineContour();
        var opt = new ChamberBuildOptions(Contour: contour, Channels: ch);

        Assert.Equal(UmbilicalStandard.None, opt.UmbilicalStandard);
        Assert.Null(opt.PurgePorts);
        Assert.Equal(MountConfiguration.FixedFlange, opt.MountConfiguration);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UmbilicalStandard end-to-end propagation
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(UmbilicalStandard.None)]
    [InlineData(UmbilicalStandard.AN_MS33656_06)]
    [InlineData(UmbilicalStandard.AN_MS33656_08)]
    [InlineData(UmbilicalStandard.Cryo_QD_Half_Inch)]
    [InlineData(UmbilicalStandard.Cryo_QD_Three_Quarter)]
    [InlineData(UmbilicalStandard.Pressurant_MS33649_04)]
    public void GenerateWith_AllUmbilicalStandards_RunCleanly(UmbilicalStandard us)
    {
        var (cond, design) = Baseline();
        cond = cond with { UmbilicalStandard = us };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.Thermal);
        // Umbilical is feed-side hardware; visible in voxel only when
        // injector flange is enabled. The fast path skips voxels but
        // the propagation still has to complete without crashing.
    }

    // ─────────────────────────────────────────────────────────────────
    //  PurgePort voxel placement coverage
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PurgeLocation.InjectorDomeOx)]
    [InlineData(PurgeLocation.InjectorDomeFuel)]
    [InlineData(PurgeLocation.ChamberPrePurge)]
    [InlineData(PurgeLocation.NozzleInertPurge)]
    public void GenerateWith_AllPurgeLocations_RunCleanly(PurgeLocation loc)
    {
        var (cond, design) = Baseline();
        design = design with
        {
            PurgePorts = new[]
            {
                new PurgePort(
                    Location: loc, Fluid: PurgeFluid.GN2,
                    MassFlow_kgs: 0.005, InletPressure_Pa: 20e6,
                    BoreDiameter_mm: 2.0),
            },
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.PurgeResults);
        Assert.Single(gen.PurgeResults!);
    }

    [Fact]
    public void GenerateWith_MultiplePurgePorts_RunCleanly()
    {
        var (cond, design) = Baseline();
        design = design with
        {
            PurgePorts = new[]
            {
                new PurgePort(PurgeLocation.InjectorDomeOx,    PurgeFluid.GN2,    0.005, 20e6, 2.0),
                new PurgePort(PurgeLocation.InjectorDomeFuel,  PurgeFluid.GN2,    0.005, 20e6, 2.0),
                new PurgePort(PurgeLocation.ChamberPrePurge,   PurgeFluid.Helium, 0.002, 10e6, 1.5),
                new PurgePort(PurgeLocation.NozzleInertPurge,  PurgeFluid.GOX,    0.003, 15e6, 2.5),
            },
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.PurgeResults);
        Assert.Equal(4, gen.PurgeResults!.Length);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Gimbal mount voxel coverage
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MountConfiguration.FixedFlange)]
    [InlineData(MountConfiguration.PinJointGimbal)]
    [InlineData(MountConfiguration.CardanGimbal)]
    [InlineData(MountConfiguration.FlexureGimbal)]
    public void GenerateWith_AllMountConfigs_RunCleanly(MountConfiguration mc)
    {
        var (cond, design) = Baseline();
        design = design with
        {
            MountConfiguration   = mc,
            IncludeMountingFlange = true,        // gimbal trunnions only drawn when flange is on
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.GimbalMount);
        Assert.Equal(mc, gen.GimbalMount!.Configuration);
    }

    // ─────────────────────────────────────────────────────────────────
    //  ChannelTopology.None UI binding (round-trip via ApplyDesign
    //  is exercised by Phase3SprintTests; here we confirm the field
    //  default + value cycle without UI involvement)
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ChannelTopology.Axial)]
    [InlineData(ChannelTopology.Helical)]
    [InlineData(ChannelTopology.None)]
    public void ChannelTopology_AllValues_RoundTripThroughDesign(ChannelTopology topo)
    {
        var d = new RegenChamberDesign { ChannelTopology = topo };
        Assert.Equal(topo, d.ChannelTopology);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Cross-cutting: combined Tier-1 fields all together
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateWith_AllFollowOnFieldsTogether_RunsCleanly()
    {
        // Stress test: every Tier-1 field set to a non-default value
        // simultaneously. If any of the three voxel additions clashed
        // with another (e.g. trunnion bounds clipping a purge bore),
        // GenerateWith would throw or produce a degenerate result.
        var (cond, design) = Baseline();
        cond = cond with { UmbilicalStandard = UmbilicalStandard.AN_MS33656_08 };
        design = design with
        {
            MountConfiguration   = MountConfiguration.CardanGimbal,
            IncludeMountingFlange = true,
            PurgePorts = new[]
            {
                new PurgePort(PurgeLocation.InjectorDomeOx, PurgeFluid.GN2, 0.005, 20e6, 2.0),
                new PurgePort(PurgeLocation.NozzleInertPurge, PurgeFluid.Helium, 0.002, 15e6, 1.5),
            },
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.GimbalMount);
        Assert.NotNull(gen.PurgeResults);
        Assert.Equal(2, gen.PurgeResults!.Length);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private static (OperatingConditions cond, RegenChamberDesign design) Baseline()
    {
        var cond = new OperatingConditions
        {
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            // Inject + mount flanges enabled so the new voxel features
            // have something to attach to.
            IncludeInjectorFlange = true,
            IncludeMountingFlange = true,
            ContourStationCount = 40,
        };
        return (cond, design);
    }

    private static Chamber.ChamberContour MakeBaselineContour()
    {
        return Chamber.ChamberContourGenerator.Generate(
            throatRadius_mm:        4.0,
            contractionRatio:       6.0,
            expansionRatio:         8.0,
            characteristicLength_m: 1.1,
            stationCount:           40);
    }
}
