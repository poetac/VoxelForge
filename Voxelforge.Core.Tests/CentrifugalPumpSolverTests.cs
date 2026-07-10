// CentrifugalPumpSolverTests — pins the closed-form pump math: hydraulic
// power ρ·g·Q·H, shaft power P_hyd/η, SI specific speed ω·√Q/(g·H)^0.75,
// the NPSH_a suction balance, the Thoma-fit NPSH_r, the affinity laws
// (Q∝N, H∝N², P∝N³), and the PMP.W2 positive-displacement flow helper.
// Pure closed form → exact assertions. Backfills coverage onto the Linux
// 'core' CI leg.

using System;
using Voxelforge.Pump;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class CentrifugalPumpSolverTests
{
    // Goulds-3196-class process pump on 20 °C water (all fluid defaults):
    // Q = 0.05 m³/s, H = 50 m, N = 1450 rpm, η = 0.75.
    private static CentrifugalPumpDesign ProcessPump()
        => new(PumpKind.Centrifugal,
               VolumetricFlowRate_m3s: 0.05, HeadRise_m: 50.0,
               RotationSpeed_rpm: 1450.0, OverallEfficiency: 0.75);

    [Fact]
    public void Solve_PowerAndSpecificSpeed_MatchClosedForm()
    {
        var r = CentrifugalPumpSolver.Solve(ProcessPump());
        Assert.Equal(24_516.625, r.HydraulicPower_W, 9);            // ρ·g·Q·H
        Assert.Equal(24_516.625 / 0.75, r.ShaftPowerInput_W, 6);    // P_hyd/η
        // N_s = ω·√Q/(g·H)^0.75 ≈ 0.326 — radial-flow cluster [0.2, 1.0].
        Assert.Equal(0.32584691412478783, r.SpecificSpeedSI, 9);
    }

    [Fact]
    public void Solve_NpshBalance_MatchesSuctionArithmetic()
    {
        var r = CentrifugalPumpSolver.Solve(ProcessPump());
        // NPSH_a = (101325 − 2340)/(1000·9.80665) − 0 − 0 ≈ 10.094 m.
        Assert.Equal(10.093660934162022, r.NetPositiveSuctionHeadAvailable_m, 9);
        // NPSH_r = 0.05·H·(N_s/0.5)^(4/3) ≈ 1.413 m (Thoma cluster fit).
        Assert.Equal(1.4125315366854265, r.NetPositiveSuctionHeadRequired_m, 9);
        Assert.Equal(r.NetPositiveSuctionHeadAvailable_m - r.NetPositiveSuctionHeadRequired_m,
            r.CavitationMargin_m, 12);
        Assert.True(r.CavitationMargin_m > 0, "flooded ambient suction should be cavitation-safe");
    }

    [Fact]
    public void Solve_SuctionLiftAndFriction_ReduceNpshAvailableLinearly()
    {
        var flooded = CentrifugalPumpSolver.Solve(ProcessPump());
        var lifted = CentrifugalPumpSolver.Solve(ProcessPump() with
        {
            InletElevationLift_m = 3.0,
            InletFrictionLoss_m = 1.5,
        });
        Assert.Equal(flooded.NetPositiveSuctionHeadAvailable_m - 4.5,
            lifted.NetPositiveSuctionHeadAvailable_m, 9);
        // NPSH_r depends only on (H, N_s) — unchanged by suction layout.
        Assert.Equal(flooded.NetPositiveSuctionHeadRequired_m,
            lifted.NetPositiveSuctionHeadRequired_m, 12);
    }

    [Fact]
    public void ApplyAffinityLaws_ScaleAsNSquaredCubed()
    {
        // Doubling speed: Q ×2, H ×4, P ×8 — exactly.
        var (q2, h2, p2) = CentrifugalPumpSolver.ApplyAffinityLaws(
            Q1: 0.05, H1: 50.0, P1: 32_000.0, N1: 1450.0, N2: 2900.0);
        Assert.Equal(0.10, q2, 12);
        Assert.Equal(200.0, h2, 9);
        Assert.Equal(256_000.0, p2, 6);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CentrifugalPumpSolver.ApplyAffinityLaws(0.05, 50.0, 32_000.0, 0.0, 2900.0));
    }

    [Fact]
    public void ComputePositiveDisplacementFlow_IsDisplacementTimesRevRate()
    {
        // Q = V·(N/60)·η_vol = 1e-4·20·0.95 = 1.9e-3 m³/s exactly.
        Assert.Equal(0.0019, CentrifugalPumpSolver.ComputePositiveDisplacementFlow(
            displacementPerRevolution_m3: 1.0e-4, rotationSpeed_rpm: 1200.0), 15);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CentrifugalPumpSolver.ComputePositiveDisplacementFlow(0.0, 1200.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CentrifugalPumpSolver.ComputePositiveDisplacementFlow(1.0e-4, 1200.0, 1.1));
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            CentrifugalPumpSolver.Solve(ProcessPump() with { OverallEfficiency = 1.2 }));
        Assert.Throws<ArgumentException>(() =>
            CentrifugalPumpSolver.Solve(ProcessPump() with { VolumetricFlowRate_m3s = 0.0 }));
        Assert.Throws<ArgumentException>(() =>
            CentrifugalPumpSolver.Solve(ProcessPump() with { Kind = PumpKind.None }));
    }
}
