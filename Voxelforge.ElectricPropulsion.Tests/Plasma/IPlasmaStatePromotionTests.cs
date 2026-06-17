// IPlasmaStatePromotionTests.cs — ADR-029a verification.
//
// Sprint EP.W2.PPT promoted IPlasmaState from
// Voxelforge.ElectricPropulsion.Core/Plasma/ to Voxelforge.Core/Plasma/
// because PPT is the third concrete consumer (after HET + Arcjet),
// firing ADR-029 D1's rule-of-three watch.
//
// These tests pin the promotion's observable shape so a future refactor
// can't accidentally regress the namespace + assembly home.

using Voxelforge.ElectricPropulsion.Plasma;
using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Plasma;

public sealed class IPlasmaStatePromotionTests
{
    [Fact]
    public void IPlasmaState_NamespaceIsVoxelforgePlasma()
    {
        Assert.Equal("Voxelforge.Plasma", typeof(IPlasmaState).Namespace);
    }

    [Fact]
    public void IPlasmaState_LivesInVoxelforgeCoreAssembly()
    {
        Assert.Equal("Voxelforge.Core", typeof(IPlasmaState).Assembly.GetName().Name);
    }

    [Fact]
    public void HetArcjetPpt_AllImplementIPlasmaState()
    {
        // The three concrete consumers that triggered the rule-of-three.
        Assert.True(typeof(IPlasmaState).IsAssignableFrom(typeof(HetPlasmaState)));
        Assert.True(typeof(IPlasmaState).IsAssignableFrom(typeof(ArcjetPlasmaState)));
        Assert.True(typeof(IPlasmaState).IsAssignableFrom(typeof(PptPlasmaState)));
    }

    [Fact]
    public void IonPlasmaState_AlsoImplementsIPlasmaState()
    {
        // GIT is the fourth IPlasmaState consumer (Sprint EP.W2.GIT) —
        // ratifies that ADR-029a's promotion supports continued cross-
        // variant additions without further architectural moves.
        Assert.True(typeof(IPlasmaState).IsAssignableFrom(typeof(IonPlasmaState)));
    }

    [Fact]
    public void MpdPlasmaState_AlsoImplementsIPlasmaState()
    {
        // MPD is the fifth (and final EP-pillar) IPlasmaState consumer
        // (Sprint EP.W2.MPD) — closes the EP plasma-variant portfolio.
        Assert.True(typeof(IPlasmaState).IsAssignableFrom(typeof(MpdPlasmaState)));
    }
}
