// HetGeometryResultTests — record-equality + ctor coverage for the
// Hall-Effect Thruster envelope-build result type. Audit 05-test-gaps.md
// Section 3 High.

namespace Voxelforge.ElectricPropulsion.Tests;

public class HetGeometryResultTests
{
    private sealed class DummyVoxelHandle : Voxelforge.IVoxelHandle { }

    private static HetGeometryResult NominalResult() =>
        new HetGeometryResult(
            Voxels:                new DummyVoxelHandle(),
            SolidVolume_mm3:       180_000.0,
            WallThickness_mm:           2.0,
            TotalMass_g:           1_500.0,
            BoundingLength_mm:        140.0,
            BoundingDiameter_mm:      120.0,
            ChannelInnerRadius_mm:     20.0,
            ChannelOuterRadius_mm:     30.0,
            ChannelWidth_mm:           10.0,
            CathodePostLength_mm:      40.0,
            Description:           "SPT-100-like HET envelope");

    [Fact]
    public void Ctor_PopulatesAllScalars()
    {
        var r = NominalResult();
        Assert.Equal(180_000.0, r.SolidVolume_mm3,        precision: 3);
        Assert.Equal(      2.0, r.WallThickness_mm,       precision: 6);
        Assert.Equal( 1_500.0,  r.TotalMass_g,            precision: 3);
        Assert.Equal(    140.0, r.BoundingLength_mm,      precision: 3);
        Assert.Equal(    120.0, r.BoundingDiameter_mm,    precision: 3);
        Assert.Equal(     20.0, r.ChannelInnerRadius_mm,  precision: 6);
        Assert.Equal(     30.0, r.ChannelOuterRadius_mm,  precision: 6);
        Assert.Equal(     10.0, r.ChannelWidth_mm,        precision: 6);
        Assert.Equal(     40.0, r.CathodePostLength_mm,   precision: 6);
        Assert.Equal("SPT-100-like HET envelope", r.Description);
    }

    [Fact]
    public void RecordEqualityComparesValueFields()
    {
        var handle = new DummyVoxelHandle();
        var a = NominalResult() with { Voxels = handle };
        var b = a with { };
        Assert.Equal(a, b);

        var c = a with { CathodePostLength_mm = 60.0 };
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void WithExpression_UpdatesChannelGeometryIndependently()
    {
        // ChannelOuterRadius_mm should equal AnodeRadius_mm by convention,
        // and ChannelWidth_mm should equal Outer - Inner. The record itself
        // does not enforce this — but the field is editable via with-expr.
        var r = NominalResult();
        var modified = r with
        {
            ChannelInnerRadius_mm = 25.0,
            ChannelOuterRadius_mm = 35.0,
            ChannelWidth_mm       = 10.0,
        };
        Assert.Equal(25.0, modified.ChannelInnerRadius_mm, precision: 6);
        Assert.Equal(35.0, modified.ChannelOuterRadius_mm, precision: 6);
        Assert.Equal(10.0, modified.ChannelWidth_mm,       precision: 6);
        // Original is unchanged.
        Assert.Equal(20.0, r.ChannelInnerRadius_mm, precision: 6);
    }

    [Fact]
    public void Description_IsPersisted()
    {
        var r = NominalResult() with { Description = "test override" };
        Assert.Equal("test override", r.Description);
    }
}
