// InjectorFaceImportOptionsTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.2: pure-data record. Never named by
// existing tests. Ctor / equality / with-expression coverage only.

using Voxelforge.Geometry;

namespace Voxelforge.Tests;

public class InjectorFaceImportOptionsTests
{
    private static InjectorFaceImportOptions Sample() => new(
        StlPath:       @"C:\fake\injector-face.stl",
        Enabled:       true,
        OffsetX_mm:    1.5,
        UniformScale:  1.0,
        AutoCenterYZ:  true);

    [Fact]
    public void Ctor_StoresAllFieldsVerbatim()
    {
        var o = Sample();
        Assert.Equal(@"C:\fake\injector-face.stl", o.StlPath);
        Assert.True(o.Enabled);
        Assert.Equal(1.5, o.OffsetX_mm, precision: 6);
        Assert.Equal(1.0, o.UniformScale, precision: 6);
        Assert.True(o.AutoCenterYZ);
    }

    [Fact]
    public void RecordEquality_HoldsOnIdenticalFieldValues()
    {
        var a = Sample();
        var b = Sample();
        Assert.Equal(a, b);
    }

    [Fact]
    public void WithExpression_OverridesScaleWithoutTouchingOtherFields()
    {
        var a = Sample();
        var b = a with { UniformScale = 0.5 };

        Assert.Equal(0.5, b.UniformScale, precision: 6);
        Assert.Equal(a.StlPath, b.StlPath);
        Assert.Equal(a.Enabled, b.Enabled);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EnabledFalse_DistinguishesEquality()
    {
        var a = Sample();
        var b = a with { Enabled = false };
        Assert.NotEqual(a, b);
    }
}
