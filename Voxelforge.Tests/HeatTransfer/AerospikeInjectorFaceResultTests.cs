// AerospikeInjectorFaceResultTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.5: AerospikeInjectorFaceThermal.Estimate
// has end-to-end coverage in SprintUpgradesTests via the Voxels-side
// AerospikeBuilder.BuildPhysicsOnly path. The result record itself
// (AerospikeInjectorFaceResult) had no direct ctor / equality / default-
// MaxServiceTemp coverage. This test pins those.

using Voxelforge.HeatTransfer;

namespace Voxelforge.Tests;

public class AerospikeInjectorFaceResultTests
{
    private static AerospikeInjectorFaceResult Sample(
        double tFace = 900.0,
        double tAw   = 3200.0) => new(
            TFace_K:          tFace,
            TAwCore_K:        tAw,
            TPropAvg_K:       150.0,
            HeatFlux_Wm2:     5e6,
            HGasSide_Wm2K:    8000.0,
            HPropSide_Wm2K:   2000.0,
            FaceArea_cm2:     12.0,
            BoreAreaFraction: 0.04,
            Method:           "aerospike-face-equilibrium-v1",
            Warnings:         System.Array.Empty<string>());

    [Fact]
    public void Ctor_StoresAllFieldsVerbatim()
    {
        var r = Sample();
        Assert.Equal(900.0, r.TFace_K, precision: 6);
        Assert.Equal(3200.0, r.TAwCore_K, precision: 6);
        Assert.Equal(150.0, r.TPropAvg_K, precision: 6);
        Assert.Equal(5e6, r.HeatFlux_Wm2, precision: 0);
        Assert.Equal(8000.0, r.HGasSide_Wm2K, precision: 6);
        Assert.Equal(2000.0, r.HPropSide_Wm2K, precision: 6);
        Assert.Equal(12.0, r.FaceArea_cm2, precision: 6);
        Assert.Equal(0.04, r.BoreAreaFraction, precision: 6);
        Assert.Equal("aerospike-face-equilibrium-v1", r.Method);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Ctor_DefaultMaxServiceTemp_IsTwelveHundredKelvin()
    {
        // PH-35 default: IN625/SS injector face → 1200 K cap.
        var r = Sample();
        Assert.Equal(1200.0, r.MaxServiceTemp_K, precision: 6);
    }

    [Fact]
    public void Ctor_MaxServiceTempOverride_IsHonoured()
    {
        var r = Sample() with { MaxServiceTemp_K = 1400.0 };
        Assert.Equal(1400.0, r.MaxServiceTemp_K, precision: 6);
    }

    [Fact]
    public void RecordEquality_DistinguishesByTFace()
    {
        var hot = Sample(tFace: 1100.0);
        var cool = Sample(tFace: 800.0);
        Assert.NotEqual(hot, cool);
    }

    [Fact]
    public void WithExpression_DoesNotMutateSource()
    {
        var a = Sample();
        var b = a with { TFace_K = 1000.0 };
        Assert.Equal(900.0, a.TFace_K, precision: 6);
        Assert.Equal(1000.0, b.TFace_K, precision: 6);
    }

    [Fact]
    public void Constants_MatchAdvertisedDefaults()
    {
        // The static-class constants are public; pin them so any
        // accidental edit reflects in a test failure rather than a
        // silent physics change. Wave-3 of #558 codified these names.
        Assert.Equal(0.005, AerospikeInjectorFaceThermal.MinBoreAreaFraction, precision: 6);
        Assert.Equal(0.90, AerospikeInjectorFaceThermal.RecoveryFactor, precision: 6);
        Assert.Equal(0.1, AerospikeInjectorFaceThermal.FaceMachNumber, precision: 6);
        Assert.Equal(1000.0, AerospikeInjectorFaceThermal.FaceWallTempSeed_K, precision: 6);
        Assert.Equal(0.026, AerospikeInjectorFaceThermal.BartzChamberScale, precision: 6);
    }
}
