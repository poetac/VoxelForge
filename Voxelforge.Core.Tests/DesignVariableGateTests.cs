// DesignVariableGateTests — pins the gate-applicability predicate
// (DesignVariableBinder.IsGateSatisfied) that BenchSweep's --sweep pre-flight
// uses to reject a design variable the selected preset's baseline would
// silently drop in Unpack (#852). A gated-off dimension is exactly the
// "flat, zero-effect CSV with exit 0" failure mode the sweep guard prevents.
//
// Cross-platform (net9.0) so it runs on the GitHub-hosted Linux CI leg.

using System.Linq;
using Voxelforge.Injector;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Core.Tests;

public class DesignVariableGateTests
{
    [Fact]
    public void NoneGate_AlwaysApplies()
    {
        Assert.True(DesignVariableBinder.IsGateSatisfied(
            SaGate.None, new RegenChamberDesign()));
    }

    [Fact]
    public void AerospikeGate_RequiresAerospikeTopology()
    {
        Assert.True(DesignVariableBinder.IsGateSatisfied(
            SaGate.AerospikeTopology,
            new RegenChamberDesign { ChannelTopology = ChannelTopology.Aerospike }));
        Assert.True(DesignVariableBinder.IsGateSatisfied(
            SaGate.AerospikeTopology,
            new RegenChamberDesign { ChannelTopology = ChannelTopology.LinearAerospike }));
        // A non-aerospike baseline gates the dimension off → Unpack would drop it.
        Assert.False(DesignVariableBinder.IsGateSatisfied(
            SaGate.AerospikeTopology,
            new RegenChamberDesign { ChannelTopology = ChannelTopology.Axial }));
    }

    [Fact]
    public void TpmsGate_RequiresTpmsTopology()
    {
        foreach (var topology in new[]
                 { ChannelTopology.TpmsGyroid, ChannelTopology.TpmsSchwarzP, ChannelTopology.TpmsSchwarzD })
        {
            Assert.True(DesignVariableBinder.IsGateSatisfied(
                SaGate.TpmsTopology, new RegenChamberDesign { ChannelTopology = topology }));
        }

        Assert.False(DesignVariableBinder.IsGateSatisfied(
            SaGate.TpmsTopology, new RegenChamberDesign { ChannelTopology = ChannelTopology.Axial }));
    }

    [Fact]
    public void InjectorPatternGate_RequiresNonNullPattern()
    {
        Assert.False(DesignVariableBinder.IsGateSatisfied(
            SaGate.InjectorPatternPresent,
            new RegenChamberDesign { InjectorElementPattern = null }));
    }

    // The exact #852 scenario: every real aerospike-gated SA dimension is
    // detected as gated-off against a non-aerospike baseline — which is what
    // makes BenchSweep reject the variable instead of emitting a flat,
    // zero-effect CSV.
    [Fact]
    public void RealAerospikeDescriptors_AreGatedOff_ForNonAerospikeBaseline()
    {
        var aeroDescriptors = DesignVariableRegistry
            .DescriptorsForMany(typeof(RegenChamberDesign), typeof(InjectorPattern))
            .Where(d => d.Gate == SaGate.AerospikeTopology)
            .ToList();

        Assert.NotEmpty(aeroDescriptors); // guards the test's own premise

        var nonAerospike = new RegenChamberDesign { ChannelTopology = ChannelTopology.Axial };
        foreach (var d in aeroDescriptors)
        {
            Assert.False(DesignVariableBinder.IsGateSatisfied(d.Gate, nonAerospike));
        }
    }
}
