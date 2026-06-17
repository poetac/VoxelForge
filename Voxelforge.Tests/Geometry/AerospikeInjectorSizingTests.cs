// AerospikeInjectorSizingTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.2: result record for the aerospike
// injector-ring sizing pass. Drives the AEROSPIKE_ELEMENT_CLEARANCE
// feasibility gate via `ClearanceOk`. Never named directly in tests.

using Voxelforge.Geometry;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;

namespace Voxelforge.Tests;

public class AerospikeInjectorSizingTests
{
    private static PatternSizingResult SamplePatternSizing() => new(
        ElementCount:        24,
        PerElementResult:    new OrificeResult(
            OxOrificeArea_mm2:   2.5,
            FuelOrificeArea_mm2: 5.0,
            OxVelocity_ms:       50.0,
            FuelVelocity_ms:     60.0,
            VelocityRatio:       1.2,
            MomentumRatio:       1.0,
            Notes:               System.Array.Empty<string>()),
        TotalOxArea_mm2:    60.0,
        TotalFuelArea_mm2: 120.0,
        FlowSplitCheck:     1.0,
        Warnings:           System.Array.Empty<string>());

    private static AerospikeInjectorSizing Sample(
        bool clearanceOk = true,
        double minClearance_mm = 1.0) => new(
            PatternSizing:           SamplePatternSizing(),
            PitchCircleRadius_mm:    18.0,
            ArcSpacing_mm:           4.5,
            ElementOuterDiameter_mm: 3.0,
            MinClearance_mm:         minClearance_mm,
            ClearanceOk:             clearanceOk);

    [Fact]
    public void Ctor_StoresAllFieldsVerbatim()
    {
        var s = Sample();
        Assert.Equal(24, s.PatternSizing.ElementCount);
        Assert.Equal(18.0, s.PitchCircleRadius_mm, precision: 6);
        Assert.Equal(4.5, s.ArcSpacing_mm, precision: 6);
        Assert.Equal(3.0, s.ElementOuterDiameter_mm, precision: 6);
        Assert.Equal(1.0, s.MinClearance_mm, precision: 6);
        Assert.True(s.ClearanceOk);
    }

    [Fact]
    public void RecordEquality_DistinguishesByClearanceFlag()
    {
        var ok  = Sample(clearanceOk: true);
        var bad = Sample(clearanceOk: false, minClearance_mm: -0.2);
        Assert.NotEqual(ok, bad);
    }

    [Fact]
    public void NegativeMinClearance_IsStoredAsIs()
    {
        // The record is a passive carrier — negative clearances are
        // valid and indicate adjacent-element overlap. The
        // AEROSPIKE_ELEMENT_CLEARANCE gate reads ClearanceOk; the
        // numeric MinClearance is reported verbatim for diagnostics.
        var s = Sample(clearanceOk: false, minClearance_mm: -0.5);
        Assert.Equal(-0.5, s.MinClearance_mm, precision: 6);
        Assert.False(s.ClearanceOk);
    }

    [Fact]
    public void WithExpression_OnlyOverridesTargetedField()
    {
        var a = Sample();
        var b = a with { ArcSpacing_mm = 7.0 };
        Assert.Equal(7.0, b.ArcSpacing_mm, precision: 6);
        Assert.Equal(a.PitchCircleRadius_mm, b.PitchCircleRadius_mm, precision: 6);
        Assert.NotEqual(a, b);
    }
}
