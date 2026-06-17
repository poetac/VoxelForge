// PulsejetGeometryResultTests — record-equality + null-Printability
// semantics for the pulsejet voxel-builder's result type. Audit
// 05-test-gaps.md Section 2 Medium.

using Voxelforge.Airbreathing.Geometry;

namespace Voxelforge.Airbreathing.Tests;

public class PulsejetGeometryResultTests
{
    private sealed class DummyVoxelHandle : Voxelforge.IVoxelHandle { }

    private static PulsejetGeometryResult NominalResult() =>
        new PulsejetGeometryResult(
            Voxels:                 new DummyVoxelHandle(),
            SolidVolume_mm3:        125_000.0,
            InnerSurfaceArea_mm2:    80_000.0,
            WallThickness_mm:            1.5,
            TotalMass_g:             1_000.0,
            BoundingLength_mm:       3_400.0,
            BoundingDiameter_mm:       400.0,
            IntakeArea_mm2:           3_000.0,
            TailpipeArea_mm2:         4_000.0,
            TubeLength_mm:            3_400.0,
            Description:            "v1-like pulsejet shell");

    [Fact]
    public void Ctor_PopulatesAllScalars()
    {
        var r = NominalResult();
        Assert.Equal(125_000.0, r.SolidVolume_mm3,       precision: 3);
        Assert.Equal( 80_000.0, r.InnerSurfaceArea_mm2,  precision: 3);
        Assert.Equal(     1.5, r.WallThickness_mm,      precision: 6);
        Assert.Equal(  1_000.0, r.TotalMass_g,           precision: 3);
        Assert.Equal( 3_400.0, r.BoundingLength_mm,     precision: 3);
        Assert.Equal(   400.0, r.BoundingDiameter_mm,   precision: 3);
        Assert.Equal( 3_000.0, r.IntakeArea_mm2,        precision: 3);
        Assert.Equal( 4_000.0, r.TailpipeArea_mm2,      precision: 3);
        Assert.Equal( 3_400.0, r.TubeLength_mm,         precision: 3);
        Assert.Equal("v1-like pulsejet shell", r.Description);
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
        // Same VoxelHandle reference — record-equality on the rest must
        // hold even though IVoxelHandle has no semantic equality.
        var handle = new DummyVoxelHandle();
        var a = NominalResult() with { Voxels = handle };
        var b = a with { };
        Assert.Equal(a, b);

        var c = a with { TubeLength_mm = 2_000.0 };
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void WithExpression_UpdatesOneFieldOnly()
    {
        var r = NominalResult();
        var heavier = r with { TotalMass_g = 1_500.0 };
        Assert.Equal(1_000.0, r.TotalMass_g, precision: 3);
        Assert.Equal(1_500.0, heavier.TotalMass_g, precision: 3);
        Assert.Equal(r.WallThickness_mm, heavier.WallThickness_mm, precision: 6);
    }
}
