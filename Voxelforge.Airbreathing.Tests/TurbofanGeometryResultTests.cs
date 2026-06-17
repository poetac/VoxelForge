// TurbofanGeometryResultTests — record-equality + null-Printability
// semantics for the turbofan voxel-builder's result type. Audit
// 05-test-gaps.md Section 2 Medium.

using Voxelforge.Airbreathing.Geometry;

namespace Voxelforge.Airbreathing.Tests;

public class TurbofanGeometryResultTests
{
    private sealed class DummyVoxelHandle : Voxelforge.IVoxelHandle { }

    private static TurbofanGeometryResult NominalResult() =>
        new TurbofanGeometryResult(
            Voxels:                       new DummyVoxelHandle(),
            SolidVolume_mm3:                 500_000.0,
            InnerSurfaceArea_mm2:            300_000.0,
            WallThickness_mm:                      2.0,
            BypassDuctWallThickness_mm:            2.0,
            TotalMass_g:                       4_000.0,
            BoundingLength_mm:                 1_400.0,
            BoundingDiameter_mm:                 800.0,
            CoreThroatArea_mm2:               18_000.0,
            BypassExitArea_mm2:               30_000.0,
            BypassRatio:                           0.34,
            ContractionRatio:                      2.5,
            ExpansionRatio:                        1.67,
            Description:                  "F404-like turbofan shell");

    [Fact]
    public void Ctor_PopulatesAllScalars()
    {
        var r = NominalResult();
        Assert.Equal(500_000.0, r.SolidVolume_mm3,        precision: 3);
        Assert.Equal(300_000.0, r.InnerSurfaceArea_mm2,   precision: 3);
        Assert.Equal(      2.0, r.WallThickness_mm,       precision: 6);
        Assert.Equal(      2.0, r.BypassDuctWallThickness_mm, precision: 6);
        Assert.Equal( 18_000.0, r.CoreThroatArea_mm2,     precision: 3);
        Assert.Equal( 30_000.0, r.BypassExitArea_mm2,     precision: 3);
        Assert.Equal(    0.34, r.BypassRatio,            precision: 6);
        Assert.Equal(    2.5,  r.ContractionRatio,       precision: 6);
        Assert.Equal(    1.67, r.ExpansionRatio,         precision: 6);
        Assert.Equal("F404-like turbofan shell", r.Description);
    }

    [Fact]
    public void Printability_DefaultsToNull()
    {
        var r = NominalResult();
        Assert.Null(r.Printability);
    }

    [Fact]
    public void RecordEquality_ComparesValueFields()
    {
        var handle = new DummyVoxelHandle();
        var a = NominalResult() with { Voxels = handle };
        var b = a with { };
        Assert.Equal(a, b);

        var c = a with { BypassRatio = 1.0 };
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void WithExpression_UpdatesBypassWallIndependently()
    {
        // The two wall-thickness fields are independent — bypass-wall can be
        // edited without disturbing the core wall.
        var r = NominalResult();
        var thinner = r with { BypassDuctWallThickness_mm = 1.2 };
        Assert.Equal(2.0, r.WallThickness_mm,                 precision: 6);
        Assert.Equal(2.0, r.BypassDuctWallThickness_mm,       precision: 6);
        Assert.Equal(2.0, thinner.WallThickness_mm,           precision: 6);
        Assert.Equal(1.2, thinner.BypassDuctWallThickness_mm, precision: 6);
    }
}
