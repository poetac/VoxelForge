// CentrifugalPumpSolverTests.cs — Sprint PMP.W1 unit tests for the
// closed-form centrifugal pump performance snapshot.

using System;
using Voxelforge.Pump;
using Xunit;

namespace Voxelforge.Tests.Pump;

public sealed class CentrifugalPumpSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneKind()
    {
        var d = Goulds3196() with { Kind = PumpKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroFlow()
    {
        var d = Goulds3196() with { VolumetricFlowRate_m3s = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroHead()
    {
        var d = Goulds3196() with { HeadRise_m = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsEfficiencyOutOfBand()
    {
        Assert.Throws<ArgumentException>(
            () => (Goulds3196() with { OverallEfficiency = 0.0 }).ValidateSelf());
        Assert.Throws<ArgumentException>(
            () => (Goulds3196() with { OverallEfficiency = 1.5 }).ValidateSelf());
    }

    // ── Goulds 3196 process-pump baseline ────────────────────────────────

    [Fact]
    public void Goulds3196_HydraulicPowerInClusterBand()
    {
        // P_hyd = ρ·g·Q·H = 1000·9.81·0.050·50 ≈ 24.5 kW.
        var r = CentrifugalPumpSolver.Solve(Goulds3196());
        Assert.InRange(r.HydraulicPower_W, 23_000.0, 26_000.0);
    }

    [Fact]
    public void Goulds3196_ShaftPowerEqualsHydraulicOverEfficiency()
    {
        var d = Goulds3196();
        var r = CentrifugalPumpSolver.Solve(d);
        Assert.Equal(r.HydraulicPower_W / d.OverallEfficiency,
                     r.ShaftPowerInput_W, precision: 4);
    }

    [Fact]
    public void Goulds3196_SpecificSpeedInRadialFlowBand()
    {
        // For Q=0.05 m³/s, H=50 m, N=3600 rpm → N_s ≈ 0.81.
        // Radial-flow centrifugal cluster band [0.2, 1.0].
        var r = CentrifugalPumpSolver.Solve(Goulds3196());
        Assert.InRange(r.SpecificSpeedSI, 0.5, 1.2);
    }

    [Fact]
    public void Goulds3196_CavitationMarginPositive_AtFloodedSuction()
    {
        // P_in = 1 atm, z_lift = 0, h_f = 0, fresh water → NPSH_a ≈
        // 10 m. NPSH_r at H=50, N_s=0.81 → ~ 4.8 m. Margin ~ 5 m.
        var r = CentrifugalPumpSolver.Solve(Goulds3196());
        Assert.True(r.CavitationMargin_m > 1.0,
            $"Cavitation margin ({r.CavitationMargin_m:F2} m) expected > 1 m "
          + "for flooded suction at atmospheric inlet.");
    }

    [Fact]
    public void Goulds3196_CavitationMarginNegative_AtLargeLiftPlusHeadLoss()
    {
        // High vertical lift + suction-line friction drives NPSH_a below
        // NPSH_r → cavitation imminent.
        var d = Goulds3196() with
        {
            InletElevationLift_m = 6.0,
            InletFrictionLoss_m  = 3.0,
        };
        var r = CentrifugalPumpSolver.Solve(d);
        Assert.True(r.CavitationMargin_m < 0,
            $"Cavitation margin ({r.CavitationMargin_m:F2} m) expected < 0 "
          + "for hard-lift + lossy-suction layout.");
    }

    // ── Scaling sanity ──────────────────────────────────────────────────

    [Fact]
    public void HydraulicPower_LinearInFlow_AtConstantHead()
    {
        var lo = CentrifugalPumpSolver.Solve(Goulds3196() with { VolumetricFlowRate_m3s = 0.025 });
        var hi = CentrifugalPumpSolver.Solve(Goulds3196() with { VolumetricFlowRate_m3s = 0.050 });
        Assert.Equal(2.0, hi.HydraulicPower_W / lo.HydraulicPower_W, precision: 6);
    }

    [Fact]
    public void HydraulicPower_LinearInHead_AtConstantFlow()
    {
        var lo = CentrifugalPumpSolver.Solve(Goulds3196() with { HeadRise_m = 25.0 });
        var hi = CentrifugalPumpSolver.Solve(Goulds3196() with { HeadRise_m = 50.0 });
        Assert.Equal(2.0, hi.HydraulicPower_W / lo.HydraulicPower_W, precision: 6);
    }

    [Fact]
    public void NPSH_DecreasesWithLiftHeight()
    {
        // Each metre of suction lift drops NPSH_a by exactly 1 m.
        var flooded = CentrifugalPumpSolver.Solve(Goulds3196() with { InletElevationLift_m = 0.0 });
        var lifted  = CentrifugalPumpSolver.Solve(Goulds3196() with { InletElevationLift_m = 3.0 });
        Assert.Equal(3.0,
            flooded.NetPositiveSuctionHeadAvailable_m
          - lifted.NetPositiveSuctionHeadAvailable_m,
            precision: 6);
    }

    // ── Affinity laws ───────────────────────────────────────────────────

    [Fact]
    public void AffinityLaws_DoubleSpeed_DoubleQ_QuadrupleH_OctupleP()
    {
        var (Q2, H2, P2) = CentrifugalPumpSolver.ApplyAffinityLaws(
            Q1: 0.10, H1: 25.0, P1: 10_000.0, N1: 1800, N2: 3600);
        Assert.Equal(0.20,    Q2, precision: 6);
        Assert.Equal(100.0,   H2, precision: 4);
        Assert.Equal(80_000.0, P2, precision: 1);
    }

    [Fact]
    public void AffinityLaws_RejectsNonPositiveSpeed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CentrifugalPumpSolver.ApplyAffinityLaws(0.1, 25, 10000, 0, 1800));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CentrifugalPumpSolver.ApplyAffinityLaws(0.1, 25, 10000, 1800, -100));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Goulds 3196 process-pump-class baseline (Q ≈ 0.05 m³/s ≈ 800 GPM,
    // H = 50 m, η = 0.75, 3600 rpm). Flooded-suction, fresh-water, 20 °C.
    private static CentrifugalPumpDesign Goulds3196() => new(
        Kind:                    PumpKind.Centrifugal,
        VolumetricFlowRate_m3s:  0.050,
        HeadRise_m:              50.0,
        RotationSpeed_rpm:       3600,
        OverallEfficiency:       0.75);
}
