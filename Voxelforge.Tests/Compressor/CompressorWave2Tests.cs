// CompressorWave2Tests.cs — Sprint CMP.W2 unit tests for the axial-flow
// kind + multi-stage helper.

using System;
using Voxelforge.Compressor;
using Xunit;

namespace Voxelforge.Tests.Compressor;

public sealed class CompressorWave2Tests
{
    [Fact]
    public void Validate_AcceptsAxialFlow()
    {
        var d = LM2500_AxialClass();
        d.ValidateSelf();   // must not throw
    }

    [Fact]
    public void AxialFlow_SolveProducesNonZeroPower()
    {
        var r = CentrifugalCompressorSolver.Solve(LM2500_AxialClass());
        Assert.True(r.ShaftPowerInput_W > 0);
    }

    // ── ComputeIsentropicFromPolytropic helper ──────────────────────────

    [Fact]
    public void Isentropic_AtPolytropicOne_EqualsOne()
    {
        // At η_pc = 1, η_isen = 1 regardless of π.
        double eta = CentrifugalCompressorSolver.ComputeIsentropicFromPolytropic(
            perStagePolytropicEfficiency: 1.0,
            overallPressureRatio:         20.0,
            gamma:                        1.4);
        Assert.Equal(1.0, eta, precision: 6);
    }

    [Fact]
    public void Isentropic_BelowPolytropic_AtMultiStagePratio()
    {
        // For a real axial compressor with η_pc = 0.90 + π = 20:
        // η_isen should be less than η_pc.
        double eta = CentrifugalCompressorSolver.ComputeIsentropicFromPolytropic(
            perStagePolytropicEfficiency: 0.90,
            overallPressureRatio:         20.0,
            gamma:                        1.4);
        Assert.True(eta < 0.90);
        Assert.True(eta > 0.80);     // typical LM2500-class η_isen
    }

    [Fact]
    public void Isentropic_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CentrifugalCompressorSolver.ComputeIsentropicFromPolytropic(
                0.0, 10.0, 1.4));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CentrifugalCompressorSolver.ComputeIsentropicFromPolytropic(
                0.9, 1.0, 1.4));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CentrifugalCompressorSolver.ComputeIsentropicFromPolytropic(
                0.9, 10.0, 1.0));
    }

    [Fact]
    public void Isentropic_DecreasesWithIncreasingPressureRatio_AtConstantEtapc()
    {
        double eta_low = CentrifugalCompressorSolver.ComputeIsentropicFromPolytropic(
            0.9, 5.0, 1.4);
        double eta_hi  = CentrifugalCompressorSolver.ComputeIsentropicFromPolytropic(
            0.9, 25.0, 1.4);
        Assert.True(eta_hi < eta_low,
            $"η_isen at π=25 ({eta_hi:F4}) should be < η_isen at π=5 "
          + $"({eta_low:F4}) at constant η_pc.");
    }

    // GE LM2500-class axial-flow compressor: 18:1 overall Pratio.
    private static CentrifugalCompressorDesign LM2500_AxialClass() => new(
        Kind:                            CompressorKind.AxialFlow,
        MassFlow_kgs:                    70.0,
        InletTotalTemperature_K:        288.0,
        InletTotalPressure_Pa:        101325.0,
        PressureRatio:                  18.0,
        IsentropicEfficiency:            0.85,
        WorkingGasGamma:                 1.40,
        WorkingGasSpecificHeat_J_kgK:   1005.0);
}
