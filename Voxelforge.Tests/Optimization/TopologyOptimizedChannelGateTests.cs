// TopologyOptimizedChannelGateTests.cs — OOB-2 Sprint 3 gate + wiring tests.
//
// Coverage:
//   a. Gate fires      — TOPOLOGY_CHANNEL_NOT_PRINTABLE emits when a station
//                        has too many channels (channel width < LPBF floor).
//   b. Gate silent     — Advisory is absent when all topology-optimized widths
//                        are comfortably above the LPBF feature floor.
//   c. GenerateWith populates TopologyChannels on TopologyOptimized topology.
//   d. Non-topology design keeps TopologyChannels == null (no regression).

using System.Linq;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class TopologyOptimizedChannelGateTests
{
    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N                = 2224.0,
        ChamberPressure_Pa      = 6.9e6,
        MixtureRatio            = 3.3,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex       = 1,
        PropellantPair          = Combustion.PropellantPair.LOX_CH4,
    };

    private static RegenChamberDesign BaseDesign() => new()
    {
        IncludeManifolds      = false,
        IncludePorts          = false,
        IncludeInjectorFlange = false,
        ContourStationCount   = 40,
        ChannelCount          = 80,
        RibThickness_mm       = 0.6,
    };

    // ── a. Gate fires when a station is too narrow ────────────────────

    [Fact]
    public void Gate_FiresWhenNarrowStation()
    {
        var cond   = DefaultConditions();
        var design = BaseDesign() with { ChannelTopology = ChannelTopology.Helical };
        var raw    = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true);

        int n = raw.Contour.Stations.Length;

        // 500 channels per station → W = (2π·R / 500) − rib.
        // Even at the chamber barrel (R ≈ 40 mm): 2π·40/500 ≈ 0.50 mm − 0.6 mm < 0.
        // Every station is below the 0.30 mm LPBF floor.
        var narrowTopo = new TopologyChannelResult(
            DensityField:             new double[n],
            ChannelsPerStation:       Enumerable.Repeat(500, n).ToArray(),
            VolumeFractionAchieved:   1.0,
            BaselinePressureDrop_Pa:  0.0,
            OptimizedPressureDrop_Pa: 0.0,
            IterationsRun:            1,
            Converged:                true);

        var result = raw with
        {
            ChannelTopology  = ChannelTopology.TopologyOptimized,
            TopologyChannels = narrowTopo,
        };

        var gates = FeasibilityGate.Evaluate(result);

        Assert.Contains(gates.Violations,
            v => v.ConstraintId == "TOPOLOGY_CHANNEL_NOT_PRINTABLE");
    }

    // ── b. Gate is silent when all widths are above the floor ─────────

    [Fact]
    public void Gate_SilentWhenAllStationsAboveFloor()
    {
        var cond   = DefaultConditions();
        var design = BaseDesign() with { ChannelTopology = ChannelTopology.Helical };
        var raw    = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true);

        int n = raw.Contour.Stations.Length;

        // 8 channels per station → W = (2π·R / 8) − rib.
        // At the throat (R ≈ 15 mm): 2π·15/8 ≈ 11.8 mm − 0.6 mm ≈ 11.2 mm.
        // Well above the 0.30 mm floor at every station.
        var wideTopo = new TopologyChannelResult(
            DensityField:             new double[n],
            ChannelsPerStation:       Enumerable.Repeat(8, n).ToArray(),
            VolumeFractionAchieved:   1.0,
            BaselinePressureDrop_Pa:  0.0,
            OptimizedPressureDrop_Pa: 0.0,
            IterationsRun:            1,
            Converged:                true);

        var result = raw with
        {
            ChannelTopology  = ChannelTopology.TopologyOptimized,
            TopologyChannels = wideTopo,
        };

        var gates = FeasibilityGate.Evaluate(result);

        Assert.DoesNotContain(gates.Violations,
            v => v.ConstraintId == "TOPOLOGY_CHANNEL_NOT_PRINTABLE");
    }

    // ── c. GenerateWith populates TopologyChannels on TopologyOptimized ──

    [Fact]
    public void GenerateWith_TopologyOptimized_PopulatesTopologyChannels()
    {
        var cond   = DefaultConditions();
        var design = BaseDesign() with { ChannelTopology = ChannelTopology.TopologyOptimized };

        var result = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true);

        Assert.NotNull(result.TopologyChannels);
        Assert.Equal(
            result.Contour.Stations.Length,
            result.TopologyChannels.ChannelsPerStation.Length);
        Assert.True(result.TopologyChannels.ChannelsPerStation.All(n => n >= 1));
    }

    // ── d. Non-topology design keeps TopologyChannels null ────────────

    [Fact]
    public void GenerateWith_HelicalTopology_TopologyChannelsIsNull()
    {
        var cond   = DefaultConditions();
        var design = BaseDesign() with { ChannelTopology = ChannelTopology.Helical };

        var result = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true);

        Assert.Null(result.TopologyChannels);
    }
}
