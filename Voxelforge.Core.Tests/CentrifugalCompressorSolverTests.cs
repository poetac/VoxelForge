// CentrifugalCompressorSolverTests — pins the isentropic-then-corrected
// stage model: T_t2_is = T_t1·π^((γ−1)/γ), ΔT_act = ΔT_is/η, P_t2 = π·P_t1,
// w = cp·ΔT_act, P_shaft = ṁ·w, the ideal-gas density ratio, and the CMP.W2
// polytropic ↔ isentropic conversion pair (mutual inverses). Pure closed
// form → exact assertions. Backfills coverage onto the Linux 'core' CI leg.

using System;
using Voxelforge.Compressor;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class CentrifugalCompressorSolverTests
{
    // Turbocharger-class stage on cold air: ṁ = 1.5 kg/s, sea-level-standard
    // inlet (288.15 K, 101 325 Pa), π_c = 2.5, η = 0.78, γ = 1.4, cp = 1005.
    private static CentrifugalCompressorDesign AirStage()
        => new(CompressorKind.Centrifugal,
               MassFlow_kgs: 1.5, InletTotalTemperature_K: 288.15,
               InletTotalPressure_Pa: 101_325.0, PressureRatio: 2.5,
               IsentropicEfficiency: 0.78, WorkingGasGamma: 1.4,
               WorkingGasSpecificHeat_J_kgK: 1005.0);

    [Fact]
    public void Solve_TemperatureChain_MatchesIsentropicThenCorrectedModel()
    {
        var r = CentrifugalCompressorSolver.Solve(AirStage());
        Assert.Equal(374.3826975949013, r.IsentropicExitTemperature_K, 9);  // T·π^(2/7)
        Assert.Equal(86.23269759490131, r.IsentropicTemperatureRise_K, 9);
        Assert.Equal(110.55474050628372, r.ActualTemperatureRise_K, 9);     // ΔT_is/0.78
        Assert.Equal(398.7047405062837, r.ActualExitTemperature_K, 9);
        // Actual rise always exceeds isentropic for η < 1.
        Assert.True(r.ActualTemperatureRise_K > r.IsentropicTemperatureRise_K);
    }

    [Fact]
    public void Solve_PressureWorkAndDensity_MatchClosedForm()
    {
        var r = CentrifugalCompressorSolver.Solve(AirStage());
        Assert.Equal(253_312.5, r.ExitTotalPressure_Pa, 6);                 // π·P_t1
        Assert.Equal(111_107.51420881513, r.SpecificWork_J_kg, 6);          // cp·ΔT_act
        Assert.Equal(166_661.2713132227, r.ShaftPowerInput_W, 5);           // ṁ·w
        Assert.Equal(1.806788148757029, r.DensityRatio, 9);                 // π·(T_t1/T_t2)
        // Density ratio must be below π (compression heats the gas).
        Assert.True(r.DensityRatio < 2.5);
    }

    [Fact]
    public void PolytropicAndIsentropicConversions_AreMutualInverses()
    {
        // η_pc from η_isen, then back — must round-trip.
        double etaPc = CentrifugalCompressorSolver.ComputePolytropicEfficiency(0.78, 2.5, 1.4);
        Assert.Equal(0.8061753374958642, etaPc, 9);
        Assert.True(etaPc > 0.78, "η_polytropic ≥ η_isentropic for a compressor");
        double roundTrip = CentrifugalCompressorSolver.ComputeIsentropicFromPolytropic(etaPc, 2.5, 1.4);
        Assert.Equal(0.78, roundTrip, 9);
    }

    [Fact]
    public void ComputeIsentropicFromPolytropic_PerfectPolytropic_IsLossless()
        // At η_pc = 1 the polytropic-isentropic gap vanishes for any π.
        => Assert.Equal(1.0, CentrifugalCompressorSolver.ComputeIsentropicFromPolytropic(
               1.0, 2.5, 1.4), 12);

    [Fact]
    public void ComputeIsentropicFromPolytropic_GapWidensWithPressureRatio()
    {
        // Same per-stage η_pc, growing overall π → overall η_isen falls.
        double atLowPi = CentrifugalCompressorSolver.ComputeIsentropicFromPolytropic(0.90, 2.0, 1.4);
        double atHighPi = CentrifugalCompressorSolver.ComputeIsentropicFromPolytropic(0.90, 25.0, 1.4);
        Assert.True(atHighPi < atLowPi,
            $"η_isen must fall as π grows at fixed η_pc; got {atLowPi} → {atHighPi}");
        Assert.True(atLowPi < 0.90 && atHighPi < 0.90);
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        // A compressor cannot reduce pressure.
        Assert.Throws<ArgumentException>(() =>
            CentrifugalCompressorSolver.Solve(AirStage() with { PressureRatio = 0.9 }));
        Assert.Throws<ArgumentException>(() =>
            CentrifugalCompressorSolver.Solve(AirStage() with { WorkingGasGamma = 1.0 }));
        Assert.Throws<ArgumentException>(() =>
            CentrifugalCompressorSolver.Solve(AirStage() with { Kind = CompressorKind.None }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CentrifugalCompressorSolver.ComputePolytropicEfficiency(0.78, 1.0, 1.4));
    }
}
