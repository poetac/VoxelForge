// PortStandardsTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.2: three public types in PortStandards.cs
// but only the PortStandard enum is referenced by other tests. PortSpec and
// the PortStandards static helpers had no direct coverage.

using Voxelforge.Geometry;

namespace Voxelforge.Tests;

public class PortStandardsTests
{
    [Fact]
    public void Get_Plain_ReturnsUnthreadedSpec()
    {
        var spec = PortStandards.Get(PortStandard.Plain);
        Assert.Equal(PortStandard.Plain, spec.Standard);
        Assert.False(spec.IsThreaded);
        Assert.Equal(0f, spec.PitchMM);
        Assert.Equal(0f, spec.TaperPerSide);
        Assert.Equal(10f, spec.NominalBoreMM);
        Assert.False(spec.RequiresSealFace);
    }

    [Fact]
    public void Get_G_1_4_HasKnownIsoDimensions()
    {
        // ISO 228 BSPP G 1/4 — major diameter 13.157 mm, pitch 1.337 mm,
        // no taper, no seal face. Verifies the spec table is honoured
        // (it's the basis of every chamber-port bolt-pattern).
        var spec = PortStandards.Get(PortStandard.G_1_4);
        Assert.Equal(13.157f, spec.MajorDiaMM, precision: 3);
        Assert.Equal(1.337f, spec.PitchMM, precision: 3);
        Assert.Equal(0f, spec.TaperPerSide);
        Assert.True(spec.IsThreaded);
        Assert.False(spec.RequiresSealFace);
    }

    [Fact]
    public void Get_NPT_1_4_HasOneIn16Taper()
    {
        // ANSI B1.20.1 NPT — 1:16 taper per side (= 1/32 included on diameter).
        var spec = PortStandards.Get(PortStandard.NPT_1_4);
        Assert.Equal(1f / 32f, spec.TaperPerSide, precision: 6);
        Assert.True(spec.IsThreaded);
    }

    [Fact]
    public void Get_SAE_4_RequiresSealFace()
    {
        // SAE-4 ORB has an O-ring seal face — distinguishes from
        // pure-thread fittings for the chamber-flange face flattening.
        var spec = PortStandards.Get(PortStandard.SAE_4);
        Assert.True(spec.RequiresSealFace);
        Assert.True(spec.IsThreaded);
    }

    [Fact]
    public void PortSpec_DerivedDiameters_AreFromPitchAndMajor()
    {
        var spec = PortStandards.Get(PortStandard.G_1_4);
        // ThreadDepthMM = 0.6 × pitch
        Assert.Equal(0.6f * spec.PitchMM, spec.ThreadDepthMM, precision: 4);
        // MinorDiaMM = major − 2 × depth
        Assert.Equal(spec.MajorDiaMM - 2f * spec.ThreadDepthMM, spec.MinorDiaMM, precision: 4);
        // BossDiaMM = major + 2 × pitch
        Assert.Equal(spec.MajorDiaMM + 2f * spec.PitchMM, spec.BossDiaMM, precision: 4);
    }

    [Fact]
    public void All_ContainsOneSpecPerEnumValue()
    {
        // 17 enum entries (Plain, M5/M6/M8, G_1_16, NPT_1_16, 5 × G,
        // 4 × NPT, 3 × SAE) = 17 specs.
        var all = PortStandards.All;
        Assert.Equal(System.Enum.GetValues<PortStandard>().Length, all.Length);
        // Standard ordering must match enum int values (Get keys off
        // `(int)Standard` so any mismatch would silently corrupt lookups).
        for (int i = 0; i < all.Length; i++)
            Assert.Equal((PortStandard)i, all[i].Standard);
    }

    [Fact]
    public void Names_HasOneEntryPerSpec()
    {
        var names = PortStandards.Names;
        Assert.Equal(PortStandards.All.Length, names.Length);
        Assert.Contains("Plain bore", names);
        Assert.Contains("G 1/4", names);
    }

    [Fact]
    public void PortSpec_RecordEquality_HoldsAcrossTwoGets()
    {
        // PortSpec is a record — two lookups of the same enum value
        // must compare equal by value.
        var a = PortStandards.Get(PortStandard.G_1_8);
        var b = PortStandards.Get(PortStandard.G_1_8);
        Assert.Equal(a, b);
    }
}
