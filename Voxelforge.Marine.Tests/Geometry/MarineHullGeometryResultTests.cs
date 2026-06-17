// MarineHullGeometryResultTests.cs — ctor + record-equality tests for
// MarineHullGeometryResult, the voxel-pipeline output record.
// Per audit 05-test-gaps.md §4 previously unreferenced.

using Voxelforge;
using Voxelforge.Marine.Geometry;
using Xunit;

namespace Voxelforge.Marine.Tests.Geometry;

public sealed class MarineHullGeometryResultTests
{
    // ── Test double for IVoxelHandle (marker interface) ─────────────────
    private sealed class StubVoxelHandle : IVoxelHandle { }

    // ── Ctor ─────────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_StoresAllFields()
    {
        IVoxelHandle shell = new StubVoxelHandle();
        var r = new MarineHullGeometryResult(
            Shell:            shell,
            HullLength_mm:    1595.0,
            HullDiameter_mm:  190.0,
            ShellVolume_mm3:  4.5e5,
            EstimatedMass_g:  1230.0,
            VoxelSize_mm:     0.20,
            Description:      "REMUS-100 Myring hull");
        Assert.Same(shell,                 r.Shell);
        Assert.Equal(1595.0,               r.HullLength_mm);
        Assert.Equal(190.0,                r.HullDiameter_mm);
        Assert.Equal(4.5e5,                r.ShellVolume_mm3);
        Assert.Equal(1230.0,               r.EstimatedMass_g);
        Assert.Equal(0.20,                 r.VoxelSize_mm);
        Assert.Equal("REMUS-100 Myring hull", r.Description);
    }

    // ── Record equality + with-expression ───────────────────────────────

    [Fact]
    public void RecordEquality_StructurallyComparesByValue()
    {
        IVoxelHandle shell = new StubVoxelHandle();
        var a = new MarineHullGeometryResult(shell, 1500, 200, 4e5, 1000, 0.25, "x");
        var b = new MarineHullGeometryResult(shell, 1500, 200, 4e5, 1000, 0.25, "x");
        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordEquality_DifferentDescription_NotEqual()
    {
        IVoxelHandle shell = new StubVoxelHandle();
        var a = new MarineHullGeometryResult(shell, 1500, 200, 4e5, 1000, 0.25, "Bluefin-21");
        var b = new MarineHullGeometryResult(shell, 1500, 200, 4e5, 1000, 0.25, "REMUS-600");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WithExpression_OverridesScalarFields()
    {
        IVoxelHandle shell = new StubVoxelHandle();
        var baseR = new MarineHullGeometryResult(shell, 1500, 200, 4e5, 1000, 0.25, "base");
        var copy  = baseR with
        {
            HullLength_mm   = 2000.0,
            EstimatedMass_g = 1500.0,
            Description     = "scaled",
        };
        Assert.Equal(2000.0,   copy.HullLength_mm);
        Assert.Equal(1500.0,   copy.EstimatedMass_g);
        Assert.Equal("scaled", copy.Description);
        // Untouched fields propagate.
        Assert.Equal(200.0, copy.HullDiameter_mm);
        Assert.Equal(0.25,  copy.VoxelSize_mm);
        Assert.Same(shell,  copy.Shell);
    }
}
