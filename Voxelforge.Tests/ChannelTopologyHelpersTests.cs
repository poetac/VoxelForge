// Sprint fix (2026-04-25) — regression tests for the aerospike-preview bug.
//
// The cloaking predicate must classify each ChannelTopology value
// EXPLICITLY. A future enum addition without a corresponding
// IsChannelStyle case throws (by design — see the helper's switch's
// default arm). These tests pin both the current classification AND
// that the throw fires.

using System;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class ChannelTopologyHelpersTests
{
    [Theory]
    [InlineData(ChannelTopology.Axial,               true)]
    [InlineData(ChannelTopology.Helical,             true)]
    [InlineData(ChannelTopology.TpmsGyroid,          true)]
    [InlineData(ChannelTopology.TpmsSchwarzP,        true)]
    [InlineData(ChannelTopology.TpmsSchwarzD,        true)]
    [InlineData(ChannelTopology.ExpansionDeflection, true)]   // outer bell regen jacket — no Fast Preview cloaking
    [InlineData(ChannelTopology.TopologyOptimized,  true)]   // SIMP channel phase — no Fast Preview cloaking
    public void IsChannelStyle_TrueForChannelTopologies(ChannelTopology t, bool expected)
        => Assert.Equal(expected, t.IsChannelStyle());

    [Fact]
    public void IsChannelStyle_FalseForNone()
        => Assert.False(ChannelTopology.None.IsChannelStyle());

    // ── The aerospike-bug regression pin ───────────────────────────────
    //
    // Aerospike + LinearAerospike must be IsChannelStyle == false. If a
    // refactor flips either to true, Fast Preview Mode will silently
    // re-enable the topology cloaking that was the root cause of the
    // 2026-04-25 bug — the user changes "Channel topology" to Aerospike,
    // clicks Generate, and gets a bell-chamber shell instead. These two
    // tests fail loudly if that classification regresses.

    [Fact]
    public void IsChannelStyle_FalseForAerospike_RegressionPin()
        => Assert.False(ChannelTopology.Aerospike.IsChannelStyle(),
            "Aerospike must NOT be channel-style — Fast Preview Mode would otherwise " +
            "cloak it to None and the user would see a bell shell, reproducing the " +
            "2026-04-25 preview bug.");

    [Fact]
    public void IsChannelStyle_FalseForLinearAerospike_RegressionPin()
        => Assert.False(ChannelTopology.LinearAerospike.IsChannelStyle(),
            "LinearAerospike must NOT be channel-style — same reasoning as Aerospike.");

    [Fact]
    public void IsChannelStyle_AllEnumValuesCovered()
    {
        // Loop the enum and call IsChannelStyle on each. The helper's
        // switch has no fall-through; an unhandled value throws. So this
        // single test guarantees that every value in the enum is
        // classified — adding a new ChannelTopology enum value without
        // adding a corresponding case fails this test.
        foreach (ChannelTopology t in Enum.GetValues<ChannelTopology>())
        {
            // Don't care about the answer here — only that no value throws.
            _ = t.IsChannelStyle();
        }
    }
}
