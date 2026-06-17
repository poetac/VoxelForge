// ResistojetGeometryResultTests — record-equality + ctor coverage for
// the resistojet voxel-builder's result type. Audit 05-test-gaps.md
// Section 3 Medium.

namespace Voxelforge.ElectricPropulsion.Tests;

public class ResistojetGeometryResultTests
{
    private sealed class DummyVoxelHandle : Voxelforge.IVoxelHandle { }

    private static ResistojetGeometryResult NominalResult() =>
        new ResistojetGeometryResult(
            Voxels:                new DummyVoxelHandle(),
            SolidVolume_mm3:       8_000.0,
            WallThickness_mm:           1.5,
            TotalMass_g:               68.8,
            BoundingLength_mm:         55.0,
            BoundingDiameter_mm:       25.0,
            ThroatArea_mm2:             3.14,
            ExitArea_mm2:             314.0,
            AreaRatio:                100.0,
            Description:           "MR-501-class resistojet");

    [Fact]
    public void Ctor_PopulatesAllScalars()
    {
        var r = NominalResult();
        Assert.Equal(8_000.0, r.SolidVolume_mm3,    precision: 3);
        Assert.Equal(    1.5, r.WallThickness_mm,   precision: 6);
        Assert.Equal(   68.8, r.TotalMass_g,        precision: 3);
        Assert.Equal(   55.0, r.BoundingLength_mm,  precision: 3);
        Assert.Equal(   25.0, r.BoundingDiameter_mm, precision: 3);
        Assert.Equal(    3.14, r.ThroatArea_mm2,    precision: 6);
        Assert.Equal(  314.0, r.ExitArea_mm2,       precision: 6);
        Assert.Equal(  100.0, r.AreaRatio,          precision: 6);
        Assert.Equal("MR-501-class resistojet", r.Description);
    }

    [Fact]
    public void PrintabilityDefaultsToNull()
    {
        var r = NominalResult();
        Assert.Null(r.Printability);
    }

    [Fact]
    public void RecordEqualityComparesValueFields()
    {
        var handle = new DummyVoxelHandle();
        var a = NominalResult() with { Voxels = handle };
        var b = a with { };
        Assert.Equal(a, b);

        var c = a with { AreaRatio = 50.0 };
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void WithExpression_UpdatesAreaRatioIndependently()
    {
        var r = NominalResult();
        var doubled = r with { AreaRatio = 200.0 };
        Assert.Equal(100.0, r.AreaRatio,       precision: 6);
        Assert.Equal(200.0, doubled.AreaRatio, precision: 6);
        Assert.Equal(r.ThroatArea_mm2, doubled.ThroatArea_mm2, precision: 6);
    }
}
