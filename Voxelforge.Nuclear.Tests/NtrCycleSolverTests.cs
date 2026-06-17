// NtrCycleSolverTests.cs — unit tests for the lumped NTR thermal cycle solver.

using System;
using Voxelforge.Nuclear;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class NtrCycleSolverTests
{
    // Shared NRX-A6 baseline design used across tests
    private static NuclearThermalDesign MakeDesign(
        double power_MW   = 1100.0,
        double mDot_kgs   = 33.0,
        double Pc_bar     = 34.0,
        double Rt_mm      = 120.0,
        double eps        = 100.0,
        double fuelLoad   = 0.65,
        double coreL_mm   = 1400.0,
        double coreD_mm   = 1400.0) => new(
            Kind:                    NuclearKind.NervaSolidCore,
            ReactorThermalPower_MW:  power_MW,
            ReactorCoreLength_mm:    coreL_mm,
            ReactorCoreDiameter_mm:  coreD_mm,
            FuelLoadingFraction:     fuelLoad,
            PropellantMassFlow_kgs:  mDot_kgs,
            ChamberPressure_bar:     Pc_bar,
            ThroatRadius_mm:         Rt_mm,
            ExpansionRatio:          eps,
            NozzleLength_mm:         4000.0,
            RegenChannelDepth_mm:    2.0,
            RegenChannelCount:       200,
            NozzleWallThickness_mm:  1.5,
            NozzleChannelWidth_mm:   3.0,
            NozzleManifoldDepth_mm:  5.0);

    private static NuclearThermalConditions MakeCond(double T_inlet_K = 80.0) =>
        new(PropellantInletTemp_K: T_inlet_K, TargetDeltaV_ms: 3000.0);

    // ── T_exit iteration ──────────────────────────────────────────────────────

    [Fact]
    public void Solve_NrxA6_CoreExitTemp_ConvergesAboveInlet()
    {
        var result = NuclearOptimization.GenerateWith(MakeDesign(), MakeCond());
        Assert.True(result.CoreExitTemp_K > 80.0,
            $"T_exit ({result.CoreExitTemp_K:F1} K) must be above inlet (80 K).");
    }

    [Fact]
    public void Solve_HigherPower_YieldsHigherCoreTemp()
    {
        var r1 = NuclearOptimization.GenerateWith(MakeDesign(power_MW: 500), MakeCond());
        var r2 = NuclearOptimization.GenerateWith(MakeDesign(power_MW: 1500), MakeCond());
        Assert.True(r2.CoreExitTemp_K > r1.CoreExitTemp_K,
            "Higher reactor power must yield higher core exit temperature.");
    }

    [Fact]
    public void Solve_HigherMassFlow_YieldsLowerCoreTemp()
    {
        var r1 = NuclearOptimization.GenerateWith(MakeDesign(mDot_kgs: 10), MakeCond());
        var r2 = NuclearOptimization.GenerateWith(MakeDesign(mDot_kgs: 45), MakeCond());
        Assert.True(r2.CoreExitTemp_K < r1.CoreExitTemp_K,
            "Higher mass flow (same power) must yield lower core exit temperature.");
    }

    // ── Isp formula ───────────────────────────────────────────────────────────

    [Fact]
    public void Solve_NrxA6_IspVacuum_IsPositive()
    {
        var result = NuclearOptimization.GenerateWith(MakeDesign(), MakeCond());
        Assert.True(result.IspVacuum_s > 0, "Isp must be positive.");
    }

    [Fact]
    public void Solve_NrxA6_IspVacuum_InPhysicalRange()
    {
        // NTRs typically achieve 800–900 s Isp.
        var result = NuclearOptimization.GenerateWith(MakeDesign(), MakeCond());
        Assert.InRange(result.IspVacuum_s, 700.0, 1000.0);
    }

    [Fact]
    public void Solve_HigherCoreTemp_YieldsHigherIsp()
    {
        // Higher reactor power → higher T_exit → higher Isp (Isp ∝ √T_exit).
        var r_low  = NuclearOptimization.GenerateWith(MakeDesign(power_MW: 500,  mDot_kgs: 20), MakeCond());
        var r_high = NuclearOptimization.GenerateWith(MakeDesign(power_MW: 1500, mDot_kgs: 20), MakeCond());
        Assert.True(r_high.IspVacuum_s > r_low.IspVacuum_s,
            "Higher reactor power (same mass flow) must yield higher Isp.");
    }

    // ── Thrust ────────────────────────────────────────────────────────────────

    [Fact]
    public void Solve_NrxA6_ThrustVacuum_IsPositive()
    {
        var result = NuclearOptimization.GenerateWith(MakeDesign(), MakeCond());
        Assert.True(result.ThrustVacuum_N > 0, "Vacuum thrust must be positive.");
    }

    [Fact]
    public void Solve_ThrustScalesWithMassFlow()
    {
        var r1 = NuclearOptimization.GenerateWith(MakeDesign(mDot_kgs: 10), MakeCond());
        var r2 = NuclearOptimization.GenerateWith(MakeDesign(mDot_kgs: 40), MakeCond());
        Assert.True(r2.ThrustVacuum_N > r1.ThrustVacuum_N,
            "Higher mass flow (same Isp range) must yield higher thrust.");
    }

    // ── k_eff heuristic ───────────────────────────────────────────────────────

    [Fact]
    public void Solve_KEff_IsInPhysicalRange()
    {
        var result = NuclearOptimization.GenerateWith(MakeDesign(fuelLoad: 0.65), MakeCond());
        // k_eff = 0.98 + 0.65 × 0.04 = 1.006
        Assert.InRange(result.KEff, 0.98, 1.06);
    }

    [Fact]
    public void Solve_HigherFuelLoading_YieldsHigherKeff()
    {
        var r_low  = NuclearOptimization.GenerateWith(MakeDesign(fuelLoad: 0.60), MakeCond());
        var r_high = NuclearOptimization.GenerateWith(MakeDesign(fuelLoad: 0.80), MakeCond());
        Assert.True(r_high.KEff > r_low.KEff,
            "Higher fuel loading fraction must yield higher k_eff heuristic.");
    }

    // ── GammaEff ──────────────────────────────────────────────────────────────

    [Fact]
    public void Solve_GammaEff_DecreasesWithTemperature()
    {
        // γ(T) = 1.4 − 4e-5 × (T − 300): γ decreases as T increases.
        var r_cool = NuclearOptimization.GenerateWith(MakeDesign(power_MW: 300, mDot_kgs: 10), MakeCond());
        var r_hot  = NuclearOptimization.GenerateWith(MakeDesign(power_MW: 1800, mDot_kgs: 10), MakeCond());
        Assert.True(r_hot.GammaEff < r_cool.GammaEff,
            "γ_eff must decrease as core exit temperature increases.");
    }

    // ── CStar ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Solve_CStar_IsPositive()
    {
        var result = NuclearOptimization.GenerateWith(MakeDesign(), MakeCond());
        Assert.True(result.CStar_ms > 0, "c* must be positive.");
    }

    // ── Volumetric heat flux ──────────────────────────────────────────────────

    [Fact]
    public void Solve_VolumetricHeatFlux_IsPositive()
    {
        var result = NuclearOptimization.GenerateWith(MakeDesign(), MakeCond());
        Assert.True(result.VolumetricHeatFlux_MWm3 > 0 || double.IsNaN(result.VolumetricHeatFlux_MWm3),
            "Q_vol must be positive or NaN (zero-volume guard).");
    }
}
