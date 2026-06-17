// RamjetGeometryResultTests — record-equality + null-Printability
// semantics for the ramjet voxel-builder's result type.

using Voxelforge.Airbreathing.Geometry;
using Xunit;

namespace Voxelforge.Airbreathing.Tests;

public class RamjetGeometryResultTests
{
    [Fact]
    public void RecordEquality_ComparesValueFields()
    {
        // Use a synthetic IVoxelHandle (the abstract marker — concrete
        // PicoGKVoxelHandle requires PicoGK, which isn't available here).
        var dummy = new DummyVoxelHandle();

        var a = new RamjetGeometryResult(
            Voxels:               dummy,
            SolidVolume_mm3:      100.0,
            InnerSurfaceArea_mm2: 50.0,
            WallThickness_mm:     2.0,
            TotalMass_g:          5.0,
            BoundingLength_mm:    150.0,
            BoundingDiameter_mm:  60.0,
            ThroatArea_mm2:       314.0,
            ContractionRatio:     2.5,
            ExpansionRatio:       5.0,
            Description:          "test ramjet");
        var b = a with { };
        Assert.Equal(a, b);

        var c = a with { ContractionRatio = 3.0 };
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Printability_DefaultsToNull()
    {
        var r = new RamjetGeometryResult(
            Voxels:               new DummyVoxelHandle(),
            SolidVolume_mm3:      100.0,
            InnerSurfaceArea_mm2: 50.0,
            WallThickness_mm:     2.0,
            TotalMass_g:          5.0,
            BoundingLength_mm:    150.0,
            BoundingDiameter_mm:  60.0,
            ThroatArea_mm2:       314.0,
            ContractionRatio:     2.5,
            ExpansionRatio:       5.0,
            Description:          "test ramjet");
        Assert.Null(r.Printability);
    }

    private sealed class DummyVoxelHandle : Voxelforge.IVoxelHandle { }
}
