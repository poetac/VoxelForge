// tech-debt T5 (2026-04-28) — coverage for the new dispatcher.
//
// Pins the family classification + every predicate so a future enum
// addition forces a deliberate update here (the throw-on-default arm
// in ChannelTopologyDispatcher.ClassifyFamily fires before the
// predicates run; this test calls every entrypoint with every enum
// value to make sure none of them silently misclassify).

using System;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class ChannelTopologyDispatcherTests
{
    [Theory]
    [InlineData(ChannelTopology.None,               ChannelTopologyDispatcher.Family.Bell)]
    [InlineData(ChannelTopology.Axial,              ChannelTopologyDispatcher.Family.AxialHelical)]
    [InlineData(ChannelTopology.Helical,            ChannelTopologyDispatcher.Family.AxialHelical)]
    [InlineData(ChannelTopology.TpmsGyroid,         ChannelTopologyDispatcher.Family.Tpms)]
    [InlineData(ChannelTopology.TpmsSchwarzP,       ChannelTopologyDispatcher.Family.Tpms)]
    [InlineData(ChannelTopology.TpmsSchwarzD,       ChannelTopologyDispatcher.Family.Tpms)]
    [InlineData(ChannelTopology.Aerospike,          ChannelTopologyDispatcher.Family.Aerospike)]
    [InlineData(ChannelTopology.LinearAerospike,    ChannelTopologyDispatcher.Family.LinearAerospike)]
    [InlineData(ChannelTopology.ExpansionDeflection, ChannelTopologyDispatcher.Family.ExpanderDeflector)]
    [InlineData(ChannelTopology.TopologyOptimized,  ChannelTopologyDispatcher.Family.TopologyOptimized)]
    public void ClassifyFamily_MapsEveryValue(
        ChannelTopology topology, ChannelTopologyDispatcher.Family expected)
        => Assert.Equal(expected, ChannelTopologyDispatcher.ClassifyFamily(topology));

    [Theory]
    [InlineData(ChannelTopology.Aerospike,           true)]
    [InlineData(ChannelTopology.LinearAerospike,     true)]
    [InlineData(ChannelTopology.None,                false)]
    [InlineData(ChannelTopology.Axial,               false)]
    [InlineData(ChannelTopology.Helical,             false)]
    [InlineData(ChannelTopology.TpmsGyroid,          false)]
    [InlineData(ChannelTopology.ExpansionDeflection, false)]
    [InlineData(ChannelTopology.TopologyOptimized,  false)]
    public void IsAerospike_CoversBothVariants(ChannelTopology t, bool expected)
        => Assert.Equal(expected, ChannelTopologyDispatcher.IsAerospike(t));

    [Theory]
    [InlineData(ChannelTopology.Aerospike,       true)]
    [InlineData(ChannelTopology.LinearAerospike, false)]
    [InlineData(ChannelTopology.None,            false)]
    public void IsAerospikeAxisymmetric_OnlyAxisymmetric(ChannelTopology t, bool expected)
        => Assert.Equal(expected, ChannelTopologyDispatcher.IsAerospikeAxisymmetric(t));

    [Theory]
    [InlineData(ChannelTopology.TpmsGyroid,   true)]
    [InlineData(ChannelTopology.TpmsSchwarzP, true)]
    [InlineData(ChannelTopology.TpmsSchwarzD, true)]
    [InlineData(ChannelTopology.Axial,        false)]
    [InlineData(ChannelTopology.Aerospike,    false)]
    [InlineData(ChannelTopology.None,         false)]
    public void IsTpms_OnlyTpmsVariants(ChannelTopology t, bool expected)
        => Assert.Equal(expected, ChannelTopologyDispatcher.IsTpms(t));

    [Theory]
    [InlineData(ChannelTopology.Axial,               true)]
    [InlineData(ChannelTopology.Helical,             true)]
    [InlineData(ChannelTopology.TpmsGyroid,          true)]
    [InlineData(ChannelTopology.ExpansionDeflection, true)]   // outer bell regen jacket runs
    [InlineData(ChannelTopology.TopologyOptimized,  true)]   // SIMP channel phase runs
    [InlineData(ChannelTopology.None,                false)]
    [InlineData(ChannelTopology.Aerospike,           false)]
    [InlineData(ChannelTopology.LinearAerospike,     false)]
    public void HasChannelPhase_MatchesIsChannelStyle(ChannelTopology t, bool expected)
    {
        Assert.Equal(expected, ChannelTopologyDispatcher.HasChannelPhase(t));
        // Cross-check: HasChannelPhase must agree with IsChannelStyle for every value.
        Assert.Equal(t.IsChannelStyle(), ChannelTopologyDispatcher.HasChannelPhase(t));
    }

    [Theory]
    [InlineData(ChannelTopology.None,            true)]
    [InlineData(ChannelTopology.Axial,           false)]
    [InlineData(ChannelTopology.Aerospike,       false)]
    [InlineData(ChannelTopology.LinearAerospike,     false)]
    [InlineData(ChannelTopology.TopologyOptimized,  false)]
    public void ShouldSkipChannelGeneration_BellOnly(ChannelTopology t, bool expected)
        => Assert.Equal(expected, ChannelTopologyDispatcher.ShouldSkipChannelGeneration(t));

    [Theory]
    [InlineData(ChannelTopology.ExpansionDeflection, true)]
    [InlineData(ChannelTopology.None,                false)]
    [InlineData(ChannelTopology.Axial,               false)]
    [InlineData(ChannelTopology.Aerospike,           false)]
    [InlineData(ChannelTopology.LinearAerospike,     false)]
    [InlineData(ChannelTopology.TopologyOptimized,  false)]
    public void IsExpansionDeflection_OnlyExpanderDeflectorFamily(ChannelTopology t, bool expected)
        => Assert.Equal(expected, ChannelTopologyDispatcher.IsExpansionDeflection(t));

    [Fact]
    public void ClassifyFamily_AllEnumValuesCovered()
    {
        // Loop the enum and call every dispatcher entrypoint on each
        // value. ClassifyFamily throws on unhandled values; the per-
        // predicate helpers route through it so they'd throw too.
        // Adding a new ChannelTopology value without updating the
        // dispatcher fails this test.
        foreach (ChannelTopology t in Enum.GetValues<ChannelTopology>())
        {
            _ = ChannelTopologyDispatcher.ClassifyFamily(t);
            _ = ChannelTopologyDispatcher.IsAerospike(t);
            _ = ChannelTopologyDispatcher.IsAerospikeAxisymmetric(t);
            _ = ChannelTopologyDispatcher.IsTpms(t);
            _ = ChannelTopologyDispatcher.HasChannelPhase(t);
            _ = ChannelTopologyDispatcher.ShouldSkipChannelGeneration(t);
            _ = ChannelTopologyDispatcher.IsExpansionDeflection(t);
            _ = ChannelTopologyDispatcher.IsTopologyOptimized(t);
        }
    }

    [Theory]
    [InlineData(ChannelTopology.TopologyOptimized,  true)]
    [InlineData(ChannelTopology.Axial,              false)]
    [InlineData(ChannelTopology.None,               false)]
    [InlineData(ChannelTopology.Aerospike,          false)]
    [InlineData(ChannelTopology.ExpansionDeflection, false)]
    public void IsTopologyOptimized_OnlyTopologyOptimized(ChannelTopology t, bool expected)
        => Assert.Equal(expected, ChannelTopologyDispatcher.IsTopologyOptimized(t));
}
