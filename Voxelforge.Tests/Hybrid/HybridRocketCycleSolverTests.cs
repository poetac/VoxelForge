// HybridRocketCycleSolverTests.cs — Sprint R.W2 unit tests for the
// closed-form hybrid rocket performance snapshot.
//
// These tests exercise the Marxman regression-rate scaling, the
// per-fuel registry, the validation surface, and an end-to-end
// SPIRIT-class classroom-hybrid baseline.

using System;
using Voxelforge.Hybrid;
using Xunit;

namespace Voxelforge.Tests.Hybrid;

public sealed class HybridRocketCycleSolverTests
{
    // ── HybridFuelRegistry ───────────────────────────────────────────────

    [Fact]
    public void Registry_HTPB_HasKarabeyogluFitConstants()
    {
        var p = HybridFuelRegistry.HTPB;
        Assert.Equal(920.0,   p.Density_kgm3, precision: 6);
        Assert.Equal(1.37e-4, p.MarxmanA,     precision: 9);
        Assert.Equal(0.681,   p.MarxmanN,     precision: 6);
    }

    [Fact]
    public void Registry_Paraffin_HasHigherAThanHTPB()
    {
        // Karabeyoglu entrainment mechanism — paraffin a is ~3× HTPB.
        var htpb = HybridFuelRegistry.HTPB;
        var par  = HybridFuelRegistry.Paraffin;
        Assert.True(par.MarxmanA > htpb.MarxmanA,
            $"Paraffin a={par.MarxmanA} expected > HTPB a={htpb.MarxmanA}.");
    }

    [Fact]
    public void Registry_For_HTPB_ResolvesToHTPB()
        => Assert.Equal(HybridFuelRegistry.HTPB,     HybridFuelRegistry.For(HybridFuel.HTPB));

    [Fact]
    public void Registry_For_Paraffin_ResolvesToParaffin()
        => Assert.Equal(HybridFuelRegistry.Paraffin, HybridFuelRegistry.For(HybridFuel.Paraffin));

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNonPositiveGrainLength()
    {
        var d = SpiritBaseline() with { GrainLength_m = -0.1 };
        // Numeric range failure -> ArgumentOutOfRangeException (#558 PR-F).
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsInitialPortRadiusAtOuterRadius()
    {
        var d = SpiritBaseline() with
        {
            InitialPortRadius_m = 0.075,   // == outer → no fuel web
            OuterGrainRadius_m  = 0.075,
        };
        // Cross-field invariant -> ArgumentException (categorical: the
        // geometry is malformed as a whole, not a single field out of range).
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsExpansionRatioBelow1()
    {
        var d = SpiritBaseline() with { ExpansionRatio = 0.5 };
        // Numeric range failure -> ArgumentOutOfRangeException (#558 PR-F).
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Solve_RejectsPortRadiusBelowInitial()
    {
        var d = SpiritBaseline();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HybridRocketCycleSolver.Solve(d, portRadius_m: 0.020));
    }

    [Fact]
    public void Solve_RejectsPortRadiusAboveOuterGrain()
    {
        var d = SpiritBaseline();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HybridRocketCycleSolver.Solve(d, portRadius_m: 0.100));
    }

    // ── C_F sensitivity to ε ─────────────────────────────────────────────

    [Fact]
    public void ComputeVacuumThrustCoefficient_AtEps10_EqualsAnchor()
    {
        double cf = HybridRocketCycleSolver.ComputeVacuumThrustCoefficient(10.0);
        Assert.Equal(HybridRocketCycleSolver.LoxHtpbVacuumThrustCoeffAtEps10, cf, precision: 6);
    }

    [Fact]
    public void ComputeVacuumThrustCoefficient_MonotonicInEps()
    {
        // log10(ε/10) is monotonic in ε → C_F monotonic in ε.
        double[] eps = { 4.0, 6.0, 8.0, 10.0, 15.0, 20.0 };
        double prev = HybridRocketCycleSolver.ComputeVacuumThrustCoefficient(eps[0]);
        for (int i = 1; i < eps.Length; i++)
        {
            double cur = HybridRocketCycleSolver.ComputeVacuumThrustCoefficient(eps[i]);
            Assert.True(cur > prev,
                $"C_F at ε={eps[i]:F1} ({cur:F4}) expected > C_F at ε={eps[i - 1]:F1} ({prev:F4}).");
            prev = cur;
        }
    }

    // ── Marxman regression-rate scaling ──────────────────────────────────

    [Fact]
    public void RegressionRate_FollowsMarxmanPowerLaw_InG_ox()
    {
        // Doubling G_ox should multiply r_dot by 2^n = 2^0.681 ≈ 1.604.
        // Drive G_ox by halving R_port at constant ṁ_ox.
        var d1 = SpiritBaseline();              // R = 0.025
        var d2 = SpiritBaseline() with { InitialPortRadius_m = 0.025 / Math.Sqrt(2) };
        // R/√2 → A/2 → G doubled.
        var r1 = HybridRocketCycleSolver.SolveInitial(d1);
        var r2 = HybridRocketCycleSolver.SolveInitial(d2);
        Assert.Equal(2.0, r2.OxidiserMassFlux_kgm2s / r1.OxidiserMassFlux_kgm2s, precision: 4);
        double expectedRatio = Math.Pow(2.0, HybridFuelRegistry.HTPB.MarxmanN);
        Assert.Equal(expectedRatio, r2.RegressionRate_ms / r1.RegressionRate_ms, precision: 4);
    }

    [Fact]
    public void RegressionRate_Paraffin_GreaterThanHTPB_AtSameG_ox()
    {
        var htpbDesign = SpiritBaseline() with { Fuel = HybridFuel.HTPB };
        var parDesign  = SpiritBaseline() with { Fuel = HybridFuel.Paraffin };
        var rHtpb = HybridRocketCycleSolver.SolveInitial(htpbDesign);
        var rPar  = HybridRocketCycleSolver.SolveInitial(parDesign);
        Assert.Equal(rHtpb.OxidiserMassFlux_kgm2s,
                     rPar.OxidiserMassFlux_kgm2s, precision: 6);   // same G_ox
        Assert.True(rPar.RegressionRate_ms > rHtpb.RegressionRate_ms,
            $"Paraffin r_dot={rPar.RegressionRate_ms:E3} expected > HTPB r_dot={rHtpb.RegressionRate_ms:E3} at same G_ox.");
    }

    // ── Fuel mass flow + O/F evolution ───────────────────────────────────

    [Fact]
    public void FuelMassFlow_ScalesWithGrainLength()
    {
        var d1 = SpiritBaseline();
        var d2 = SpiritBaseline() with { GrainLength_m = 1.0 };  // 2x length
        var r1 = HybridRocketCycleSolver.SolveInitial(d1);
        var r2 = HybridRocketCycleSolver.SolveInitial(d2);
        // m_fuel = ρ · 2πRL · r_dot. R and r_dot are unchanged; only L
        // doubles → m_fuel must double exactly.
        Assert.Equal(2.0, r2.FuelMassFlow_kgs / r1.FuelMassFlow_kgs, precision: 6);
    }

    [Fact]
    public void OFRatio_IncreasesAsPortGrows()
    {
        // As port radius grows, G_ox drops, r_dot drops, m_fuel drops,
        // O/F rises (constant ṁ_ox).
        var d   = SpiritBaseline();
        var rI  = HybridRocketCycleSolver.Solve(d, d.InitialPortRadius_m);
        var rMid = HybridRocketCycleSolver.Solve(d, 0.050);
        var rFinal = HybridRocketCycleSolver.Solve(d, d.OuterGrainRadius_m);
        Assert.True(rMid.OxidiserFuelRatio  > rI.OxidiserFuelRatio,
            $"O/F at R=0.050 ({rMid.OxidiserFuelRatio:F3}) expected > O/F at R=0.025 ({rI.OxidiserFuelRatio:F3}).");
        Assert.True(rFinal.OxidiserFuelRatio > rMid.OxidiserFuelRatio,
            $"O/F at R=0.075 ({rFinal.OxidiserFuelRatio:F3}) expected > O/F at R=0.050 ({rMid.OxidiserFuelRatio:F3}).");
    }

    [Fact]
    public void TotalMassFlow_EqualsSumOfOxAndFuel()
    {
        var r = HybridRocketCycleSolver.SolveInitial(SpiritBaseline());
        Assert.Equal(r.TotalMassFlow_kgs,
                     r.FuelMassFlow_kgs + 0.5 /* ṁ_ox */, precision: 6);
    }

    // ── SPIRIT-class classroom-hybrid baseline ───────────────────────────

    [Fact]
    public void SpiritBaseline_InitialSnapshot_ProducesClusterAnchoredValues()
    {
        // Hand-calc (HTPB, a=1.37e-4, n=0.681):
        //   G_ox = 0.5 / (π·0.025²) = 254.6 kg/(m²·s)
        //   r_dot = 1.37e-4 · 254.6^0.681 = 5.96 mm/s
        //   A_burn = 2π·0.025·0.5 = 0.0785 m²
        //   m_fuel = 920·0.0785·5.96e-3 = 0.430 kg/s
        //   O/F = 0.5/0.430 = 1.16
        //   m_total = 0.930 kg/s
        // I expect the solver to land within ±2 % of these.
        var r = HybridRocketCycleSolver.SolveInitial(SpiritBaseline());
        Assert.InRange(r.OxidiserMassFlux_kgm2s, 250.0, 260.0);
        Assert.InRange(r.RegressionRate_ms,        5.5e-3, 6.5e-3);
        Assert.InRange(r.FuelMassFlow_kgs,        0.40, 0.46);
        Assert.InRange(r.OxidiserFuelRatio,       1.05, 1.25);
        Assert.InRange(r.TotalMassFlow_kgs,       0.90, 0.96);
        // c* and C_F are cluster anchors; Isp should land in the LOX/HTPB
        // mid-band (260-275 s vacuum at moderate ε).
        Assert.InRange(r.VacuumIsp_s, 265.0, 275.0);
        Assert.True(r.VacuumThrust_N > 2000.0 && r.VacuumThrust_N < 3000.0);
    }

    [Fact]
    public void SpiritBaseline_FinalSnapshot_HasHigherOFThanInitial()
    {
        var d = SpiritBaseline();
        var rI = HybridRocketCycleSolver.SolveInitial(d);
        var rF = HybridRocketCycleSolver.Solve(d, d.OuterGrainRadius_m);
        // At R=0.075 the port area is 9x initial → G_ox drops 9x →
        // r_dot drops 9^0.681 = 4.6x → m_fuel drops (R doubles, r_dot drops 4.6x)
        // 2π·0.075·0.5 = 0.236 m² (3x area). m_fuel = 920·0.236·r_dot_final.
        // Net m_fuel drops by 4.6/3 = 1.5x. So O/F rises 1.5x: from 1.16 to ~1.74.
        Assert.True(rF.OxidiserFuelRatio > rI.OxidiserFuelRatio * 1.3,
            $"Final O/F ({rF.OxidiserFuelRatio:F3}) expected ≫ initial O/F ({rI.OxidiserFuelRatio:F3}).");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Stanford-SPIRIT-class classroom hybrid baseline: 0.5 kg/s LOX into
    // a 25 mm port through a 0.5 m HTPB grain capped at 150 mm outer dia.
    private static HybridRocketDesign SpiritBaseline() => new(
        Fuel:                 HybridFuel.HTPB,
        GrainLength_m:        0.50,
        InitialPortRadius_m:  0.025,
        OuterGrainRadius_m:   0.075,
        OxidiserMassFlow_kgs: 0.50,
        ChamberPressure_bar:  20.0,
        ExpansionRatio:       10.0);
}
