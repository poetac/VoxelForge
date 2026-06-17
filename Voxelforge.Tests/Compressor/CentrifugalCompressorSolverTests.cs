// CentrifugalCompressorSolverTests.cs — Sprint CMP.W1 unit tests for
// the closed-form centrifugal compressor stage performance snapshot.

using System;
using Voxelforge.Compressor;
using Xunit;

namespace Voxelforge.Tests.Compressor;

public sealed class CentrifugalCompressorSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneKind()
    {
        var d = Gt3582rTurbocharger() with { Kind = CompressorKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsPressureRatioAtOrBelowOne()
    {
        var d = Gt3582rTurbocharger() with { PressureRatio = 1.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsIsentropicEfficiencyOutOfBand()
    {
        Assert.Throws<ArgumentException>(
            () => (Gt3582rTurbocharger() with { IsentropicEfficiency = 0.0 }).ValidateSelf());
        Assert.Throws<ArgumentException>(
            () => (Gt3582rTurbocharger() with { IsentropicEfficiency = 1.2 }).ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsGammaOutOfBand()
    {
        Assert.Throws<ArgumentException>(
            () => (Gt3582rTurbocharger() with { WorkingGasGamma = 1.0 }).ValidateSelf());
        Assert.Throws<ArgumentException>(
            () => (Gt3582rTurbocharger() with { WorkingGasGamma = 2.5 }).ValidateSelf());
    }

    // ── GT3582R turbocharger baseline ────────────────────────────────────

    [Fact]
    public void Gt3582r_ShaftPowerInClusterBand()
    {
        // ṁ = 0.30 kg/s, π = 2.5, η = 0.74, T_t1 = 298 K → P_shaft ≈ 36 kW.
        var r = CentrifugalCompressorSolver.Solve(Gt3582rTurbocharger());
        Assert.InRange(r.ShaftPowerInput_W, 30_000.0, 42_000.0);
    }

    [Fact]
    public void Gt3582r_ExitTemperatureRisesBy120K()
    {
        // ΔT_act ≈ 120 K for π=2.5, η=0.74, T_t1=298, γ=1.4.
        var r = CentrifugalCompressorSolver.Solve(Gt3582rTurbocharger());
        Assert.InRange(r.ActualTemperatureRise_K, 110.0, 135.0);
    }

    [Fact]
    public void Gt3582r_ActualTemperatureRise_HigherThanIsentropic()
    {
        // ΔT_actual = ΔT_is / η_isen > ΔT_is since η_isen < 1.
        var r = CentrifugalCompressorSolver.Solve(Gt3582rTurbocharger());
        Assert.True(r.ActualTemperatureRise_K > r.IsentropicTemperatureRise_K);
    }

    [Fact]
    public void Gt3582r_ExitPressureEqualsInletTimesPressureRatio()
    {
        var d = Gt3582rTurbocharger();
        var r = CentrifugalCompressorSolver.Solve(d);
        Assert.Equal(d.InletTotalPressure_Pa * d.PressureRatio,
                     r.ExitTotalPressure_Pa, precision: 4);
    }

    [Fact]
    public void Gt3582r_ShaftPowerEqualsMassFlowTimesSpecificWork()
    {
        var d = Gt3582rTurbocharger();
        var r = CentrifugalCompressorSolver.Solve(d);
        Assert.Equal(d.MassFlow_kgs * r.SpecificWork_J_kg,
                     r.ShaftPowerInput_W, precision: 4);
    }

    [Fact]
    public void Gt3582r_DensityRatio_LessThanPressureRatio()
    {
        // ρ_2/ρ_1 = (P_2/P_1)·(T_1/T_2) < P_2/P_1 since T_2 > T_1.
        var d = Gt3582rTurbocharger();
        var r = CentrifugalCompressorSolver.Solve(d);
        Assert.True(r.DensityRatio < d.PressureRatio,
            $"Density ratio ({r.DensityRatio:F3}) expected < pressure ratio "
          + $"({d.PressureRatio:F3}).");
        // But > 1 (compression increases density).
        Assert.True(r.DensityRatio > 1.0);
    }

    // ── Scaling / sensitivity ───────────────────────────────────────────

    [Fact]
    public void ShaftPower_LinearInMassFlow()
    {
        var lo = CentrifugalCompressorSolver.Solve(Gt3582rTurbocharger() with { MassFlow_kgs = 0.15 });
        var hi = CentrifugalCompressorSolver.Solve(Gt3582rTurbocharger() with { MassFlow_kgs = 0.30 });
        Assert.Equal(2.0, hi.ShaftPowerInput_W / lo.ShaftPowerInput_W, precision: 6);
    }

    [Fact]
    public void HigherPressureRatio_IncreasesExitTemperature()
    {
        var lowPi = CentrifugalCompressorSolver.Solve(Gt3582rTurbocharger() with { PressureRatio = 1.5 });
        var hiPi  = CentrifugalCompressorSolver.Solve(Gt3582rTurbocharger() with { PressureRatio = 4.0 });
        Assert.True(hiPi.ActualExitTemperature_K > lowPi.ActualExitTemperature_K);
    }

    [Fact]
    public void LowerEfficiency_IncreasesExitTemperature()
    {
        // Lower η_isen → more wasted heat → higher T_t2.
        var goodEta = CentrifugalCompressorSolver.Solve(Gt3582rTurbocharger() with { IsentropicEfficiency = 0.85 });
        var poorEta = CentrifugalCompressorSolver.Solve(Gt3582rTurbocharger() with { IsentropicEfficiency = 0.65 });
        Assert.True(poorEta.ActualExitTemperature_K > goodEta.ActualExitTemperature_K);
    }

    [Fact]
    public void IsentropicEfficiencyOfOne_GivesZeroExcessOverIsentropic()
    {
        // At η_isen = 1 → ΔT_act = ΔT_is exactly (no inefficiency loss).
        var d = Gt3582rTurbocharger() with { IsentropicEfficiency = 1.0 };
        var r = CentrifugalCompressorSolver.Solve(d);
        Assert.Equal(r.IsentropicTemperatureRise_K,
                     r.ActualTemperatureRise_K, precision: 6);
        Assert.Equal(r.IsentropicExitTemperature_K,
                     r.ActualExitTemperature_K, precision: 6);
    }

    // ── Polytropic efficiency helper ────────────────────────────────────

    [Fact]
    public void PolytropicEfficiency_ExceedsIsentropic_AtPositivePressureRatio()
    {
        // For a compressor, η_polytropic > η_isentropic when π > 1.
        double eta_pc = CentrifugalCompressorSolver.ComputePolytropicEfficiency(
            isentropicEfficiency: 0.74,
            pressureRatio:        2.5,
            gamma:                1.4);
        Assert.True(eta_pc > 0.74);
        Assert.True(eta_pc < 1.0);
    }

    [Fact]
    public void PolytropicEfficiency_RejectsBadInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CentrifugalCompressorSolver.ComputePolytropicEfficiency(
                isentropicEfficiency: 0.0, pressureRatio: 2.5, gamma: 1.4));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CentrifugalCompressorSolver.ComputePolytropicEfficiency(
                isentropicEfficiency: 0.8, pressureRatio: 1.0, gamma: 1.4));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CentrifugalCompressorSolver.ComputePolytropicEfficiency(
                isentropicEfficiency: 0.8, pressureRatio: 2.5, gamma: 1.0));
    }

    [Fact]
    public void PolytropicEfficiency_MonotonicAboveIsentropic_AsPressureRatioGrows()
    {
        // As π grows, the gap η_pc − η_isen grows (more rehearsing
        // stages of compression).
        double eta_pi2 = CentrifugalCompressorSolver.ComputePolytropicEfficiency(0.78, 2.0, 1.4);
        double eta_pi5 = CentrifugalCompressorSolver.ComputePolytropicEfficiency(0.78, 5.0, 1.4);
        double gap2 = eta_pi2 - 0.78;
        double gap5 = eta_pi5 - 0.78;
        Assert.True(gap5 > gap2,
            $"η_pc gap at π=5 ({gap5:F4}) expected > gap at π=2 ({gap2:F4}).");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Garrett GT3582R turbocharger-class baseline (~ 36 kW shaft, π = 2.5
    // air compression, η_isen = 0.74). Representative of small aero
    // turbochargers and HVAC chillers.
    private static CentrifugalCompressorDesign Gt3582rTurbocharger() => new(
        Kind:                            CompressorKind.Centrifugal,
        MassFlow_kgs:                    0.30,
        InletTotalTemperature_K:         298.0,
        InletTotalPressure_Pa:           101325.0,
        PressureRatio:                   2.5,
        IsentropicEfficiency:            0.74,
        WorkingGasGamma:                 1.40,
        WorkingGasSpecificHeat_J_kgK:    1005.0);
}
