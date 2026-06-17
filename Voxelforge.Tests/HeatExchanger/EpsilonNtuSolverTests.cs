// EpsilonNtuSolverTests.cs — Sprint HX.W1 unit tests for the closed-
// form ε-NTU plate-fin heat exchanger solver.

using System;
using Voxelforge.HeatExchanger;
using Xunit;

namespace Voxelforge.Tests.HeatExchanger;

public sealed class EpsilonNtuSolverTests
{
    // ── ComputeCounterflowEffectiveness — pure unit tests ────────────────

    [Fact]
    public void Effectiveness_ZeroNTU_IsZero()
        => Assert.Equal(0.0,
            EpsilonNtuSolver.ComputeCounterflowEffectiveness(ntu: 0.0, capacityRateRatio: 0.5),
            precision: 9);

    [Fact]
    public void Effectiveness_BalancedFlow_AsymptoteIsNtuOverOnePlusNtu()
    {
        // At C_r = 1 the counterflow ε reduces to NTU/(NTU+1).
        double ntu = 3.0;
        double e = EpsilonNtuSolver.ComputeCounterflowEffectiveness(ntu, capacityRateRatio: 1.0);
        Assert.Equal(ntu / (ntu + 1.0), e, precision: 9);
    }

    [Fact]
    public void Effectiveness_ZeroCapacityRateRatio_ApproachesOneMinusExpMinusNtu()
    {
        // C_r → 0 (one side has infinite capacity rate): ε → 1 − exp(−NTU).
        double ntu = 2.0;
        double e = EpsilonNtuSolver.ComputeCounterflowEffectiveness(ntu, capacityRateRatio: 0.0);
        Assert.Equal(1.0 - Math.Exp(-ntu), e, precision: 6);
    }

    [Fact]
    public void Effectiveness_MonotonicInNTU()
    {
        double[] ntus = { 0.5, 1.0, 2.0, 5.0, 10.0, 20.0 };
        double prev = EpsilonNtuSolver.ComputeCounterflowEffectiveness(ntus[0], 0.8);
        for (int i = 1; i < ntus.Length; i++)
        {
            double cur = EpsilonNtuSolver.ComputeCounterflowEffectiveness(ntus[i], 0.8);
            Assert.True(cur > prev,
                $"ε at NTU={ntus[i]} ({cur:F4}) expected > ε at NTU={ntus[i - 1]} ({prev:F4}).");
            prev = cur;
        }
    }

    [Fact]
    public void Effectiveness_AlwaysInUnitInterval()
    {
        foreach (double ntu in new[] { 0.1, 1.0, 5.0, 20.0, 100.0 })
        foreach (double cr in new[] { 0.0, 0.25, 0.5, 0.75, 0.99, 1.0 })
        {
            double e = EpsilonNtuSolver.ComputeCounterflowEffectiveness(ntu, cr);
            Assert.InRange(e, 0.0, 1.0);
        }
    }

    [Fact]
    public void Effectiveness_RejectsNegativeNtu()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EpsilonNtuSolver.ComputeCounterflowEffectiveness(ntu: -0.1, capacityRateRatio: 0.5));
    }

    [Fact]
    public void Effectiveness_RejectsCapacityRateRatioOutOfBand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EpsilonNtuSolver.ComputeCounterflowEffectiveness(ntu: 1.0, capacityRateRatio: -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EpsilonNtuSolver.ComputeCounterflowEffectiveness(ntu: 1.0, capacityRateRatio: 1.5));
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsInvertedInletTemperatures()
    {
        var d = Recuperator() with
        {
            HotInletTemperature_K  = 300.0,
            ColdInletTemperature_K = 500.0,
        };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsFinThicknessAboveFinPitch()
    {
        var d = Recuperator() with { FinThickness_m = Recuperator().FinPitch_m };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsPlateSpacingAboveCoreHeight()
    {
        var d = Recuperator() with { PlateSpacing_m = Recuperator().CoreHeight_m * 2.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Recuperator baseline ─────────────────────────────────────────────

    [Fact]
    public void Recuperator_DesignPoint_EffectivenessInClusterBand()
    {
        // Air-air recuperator-class (hand-calc): NTU ≈ 8, C_r ≈ 0.955 →
        // ε ≈ 0.90-0.91. Wide cluster band to swallow floating-point +
        // geometric-rounding (passes count rounds down).
        var r = EpsilonNtuSolver.Solve(Recuperator());
        Assert.InRange(r.Effectiveness, 0.80, 0.97);
    }

    [Fact]
    public void Recuperator_DesignPoint_EnergyBalancePreserved()
    {
        // Q_hot = C_hot · (T_hot_in − T_hot_out) must equal
        // Q_cold = C_cold · (T_cold_out − T_cold_in) — both equal HeatDuty_W.
        var d = Recuperator();
        var r = EpsilonNtuSolver.Solve(d);
        double Q_hot  = d.HotMassFlow_kgs  * d.HotCp_JkgK
                      * (d.HotInletTemperature_K - r.HotOutletTemperature_K);
        double Q_cold = d.ColdMassFlow_kgs * d.ColdCp_JkgK
                      * (r.ColdOutletTemperature_K - d.ColdInletTemperature_K);
        Assert.Equal(Q_hot,  r.HeatDuty_W, precision: 3);
        Assert.Equal(Q_cold, r.HeatDuty_W, precision: 3);
    }

    [Fact]
    public void Recuperator_DesignPoint_OutletsBoundedByInlets()
    {
        var d = Recuperator();
        var r = EpsilonNtuSolver.Solve(d);
        // 2nd-law: hot can't drop below cold inlet, cold can't rise above hot inlet.
        Assert.True(r.HotOutletTemperature_K  >= d.ColdInletTemperature_K);
        Assert.True(r.ColdOutletTemperature_K <= d.HotInletTemperature_K);
        // And both outlets must lie strictly between the inlet bounds.
        Assert.True(r.HotOutletTemperature_K  < d.HotInletTemperature_K);
        Assert.True(r.ColdOutletTemperature_K > d.ColdInletTemperature_K);
    }

    [Fact]
    public void Recuperator_DesignPoint_OverallUBetweenSideHTCs()
    {
        // 1/U = 1/h_hot + 1/h_cold → U lies between min and max of the
        // two h's (specifically: U < min(h_hot, h_cold)).
        var r = EpsilonNtuSolver.Solve(Recuperator());
        double h_min = Math.Min(r.HotSideHTC_W_m2K, r.ColdSideHTC_W_m2K);
        Assert.True(r.OverallHeatTransferCoefficient_W_m2K < h_min,
            $"U ({r.OverallHeatTransferCoefficient_W_m2K:F1}) expected < min(h_hot, h_cold) "
          + $"= {h_min:F1}.");
        Assert.True(r.OverallHeatTransferCoefficient_W_m2K > 0);
    }

    [Fact]
    public void Recuperator_DesignPoint_HeatDutyInClusterBand()
    {
        // Hand-calc: Q ≈ 19 kW. Cluster band [10 kW, 25 kW] swallows
        // geometry rounding.
        var r = EpsilonNtuSolver.Solve(Recuperator());
        Assert.InRange(r.HeatDuty_W, 10_000.0, 25_000.0);
    }

    [Fact]
    public void Recuperator_DesignPoint_PressureDropsPositive()
    {
        var r = EpsilonNtuSolver.Solve(Recuperator());
        Assert.True(r.HotPressureDrop_Pa  > 0);
        Assert.True(r.ColdPressureDrop_Pa > 0);
    }

    [Fact]
    public void Recuperator_DesignPoint_ReynoldsInLaminarTransitionBand()
    {
        // Plate-fin HX typically operates in Re ~ 500-2000 — exactly
        // where the Kays-London offset-strip-fin correlations apply.
        var r = EpsilonNtuSolver.Solve(Recuperator());
        Assert.InRange(r.HotReynolds,  100.0, 5000.0);
        Assert.InRange(r.ColdReynolds, 100.0, 5000.0);
    }

    // ── Capacity-rate framework ──────────────────────────────────────────

    [Fact]
    public void CapacityRateRatio_AlwaysAtMostOne()
    {
        var d = Recuperator() with
        {
            HotMassFlow_kgs  = 0.10,
            ColdMassFlow_kgs = 0.05,
        };
        var r = EpsilonNtuSolver.Solve(d);
        Assert.True(r.CapacityRateRatio > 0 && r.CapacityRateRatio <= 1.0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Small air-air recuperator-class baseline. Cluster-anchored to land
    // ε ~ 0.91 / Q ~ 19 kW / NTU ~ 8 at the design point.
    private static PlateFinDesign Recuperator() => new(
        Kind:                    HeatExchangerKind.PlateFinCounterflow,
        CoreLength_m:            0.10,
        CoreWidth_m:             0.15,
        CoreHeight_m:            0.10,
        PlateSpacing_m:          0.006,
        FinPitch_m:              0.002,
        FinThickness_m:          0.0004,
        HotMassFlow_kgs:         0.05,
        ColdMassFlow_kgs:        0.05,
        HotInletTemperature_K:   700.0,
        ColdInletTemperature_K:  300.0,
        HotCp_JkgK:              1100.0,
        ColdCp_JkgK:             1050.0,
        HotDensity_kgm3:         0.5,
        ColdDensity_kgm3:        1.0,
        HotViscosity_PaS:        3.5e-5,
        ColdViscosity_PaS:       2.0e-5);
}
