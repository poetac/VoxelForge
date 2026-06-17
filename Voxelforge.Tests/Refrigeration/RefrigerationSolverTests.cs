// RefrigerationSolverTests.cs — Sprint RFG.W1 unit tests for the
// closed-form vapor-compression refrigeration / heat-pump solver.

using System;
using Voxelforge.Refrigeration;
using Xunit;

namespace Voxelforge.Tests.Refrigeration;

public sealed class RefrigerationSolverTests
{
    // ── Registry ─────────────────────────────────────────────────────────

    [Fact]
    public void Registry_R134a_AndR410A_HaveExpectedClusterAnchors()
    {
        Assert.InRange(RefrigerantRegistry.R134a.SecondLawEfficiency, 0.45, 0.65);
        Assert.InRange(RefrigerantRegistry.R410A.SecondLawEfficiency, 0.45, 0.65);
        // R-134a GWP is high (~ 1430) — climate-treaty target for phase-out.
        Assert.True(RefrigerantRegistry.R134a.GlobalWarmingPotential > 1000);
        // R-1234yf and R-744 have GWP < 5 (natural / low-GWP cluster).
        Assert.True(RefrigerantRegistry.R1234yf.GlobalWarmingPotential < 5.0);
        Assert.True(RefrigerantRegistry.R744.GlobalWarmingPotential    < 5.0);
    }

    [Fact]
    public void Registry_For_ThrowsOnNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RefrigerantRegistry.For(Refrigerant.None));
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneMode()
    {
        var d = ResidentialAc() with { Mode = RefrigerationMode.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNoneRefrigerant()
    {
        var d = ResidentialAc() with { Refrigerant = Refrigerant.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsTHotAtOrBelowTCold()
    {
        var d = ResidentialAc() with
        {
            ColdReservoirTemperature_K = 308.0,
            HotReservoirTemperature_K  = 300.0,
        };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveCompressorPower()
    {
        var d = ResidentialAc() with { CompressorPowerInput_W = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Residential AC baseline ──────────────────────────────────────────

    [Fact]
    public void ResidentialAc_CoolingCopInClusterBand()
    {
        // R-410A, T_c = 283 K, T_h = 308 K → COP_Carnot = 11.3 →
        // real COP = 0.58 · 11.3 = 6.57. Cluster band [4.0, 8.0] for
        // residential split AC at design conditions.
        var r = RefrigerationSolver.Solve(ResidentialAc());
        Assert.InRange(r.CoolingCop, 4.0, 8.0);
    }

    [Fact]
    public void ResidentialAc_CarnotBoundsExceeded_NotByReal()
    {
        // Real COP must always be below Carnot.
        var r = RefrigerationSolver.Solve(ResidentialAc());
        Assert.True(r.CoolingCop < r.CarnotCoolingCop);
        Assert.True(r.HeatingCop < r.CarnotHeatingCop);
    }

    [Fact]
    public void ResidentialAc_HeatingCopEqualsCoolingPlusOne()
    {
        // Energy balance: Q_hot = Q_cold + W → COP_heating = COP_cooling + 1.
        var r = RefrigerationSolver.Solve(ResidentialAc());
        Assert.Equal(r.CoolingCop + 1.0, r.HeatingCop, precision: 9);
    }

    [Fact]
    public void ResidentialAc_EnergyBalance_QhotEqualsQcoldPlusW()
    {
        var d = ResidentialAc();
        var r = RefrigerationSolver.Solve(d);
        Assert.Equal(r.ColdSideHeatRemoval_W + d.CompressorPowerInput_W,
                     r.HotSideHeatDelivery_W, precision: 4);
    }

    [Fact]
    public void ResidentialAc_CoolingCapacityInClusterBand()
    {
        // COP ≈ 6.57 · 3500 W ≈ 23 kW. Standard residential 3-ton AC
        // (12 kBtu/hr ≈ 3.5 kW thermal) under-sized for this — my
        // anchor is more like a 5-ton unit. Cluster band [15, 30] kW.
        var r = RefrigerationSolver.Solve(ResidentialAc());
        Assert.InRange(r.ColdSideHeatRemoval_W, 15_000.0, 30_000.0);
    }

    // ── Scaling sanity ──────────────────────────────────────────────────

    [Fact]
    public void Cop_DecreasesAsHotColdGapWidens()
    {
        // Wider ΔT (e.g. summer noon at 40 °C ambient with 10 °C evap)
        // gives lower COP than mild ΔT (20 °C ambient).
        var mild   = ResidentialAc() with { HotReservoirTemperature_K = 293 };  // 20 °C
        var summer = ResidentialAc() with { HotReservoirTemperature_K = 313 };  // 40 °C
        var rMild   = RefrigerationSolver.Solve(mild);
        var rSummer = RefrigerationSolver.Solve(summer);
        Assert.True(rMild.CoolingCop > rSummer.CoolingCop,
            $"Mild-ΔT COP ({rMild.CoolingCop:F2}) expected > "
          + $"summer-ΔT COP ({rSummer.CoolingCop:F2}).");
    }

    [Fact]
    public void Cooling_LinearInCompressorPower()
    {
        var lo = RefrigerationSolver.Solve(ResidentialAc() with { CompressorPowerInput_W = 1500 });
        var hi = RefrigerationSolver.Solve(ResidentialAc() with { CompressorPowerInput_W = 3000 });
        // Same COP → Q_cold scales linearly with W.
        Assert.Equal(2.0,
            hi.ColdSideHeatRemoval_W / lo.ColdSideHeatRemoval_W,
            precision: 6);
    }

    [Fact]
    public void R744_LowerCop_ThanR410A_AtSameTemperatures()
    {
        // R-744 (CO₂ transcritical) η_2nd = 0.50 vs R-410A 0.58.
        var r410 = RefrigerationSolver.Solve(ResidentialAc()
            with { Refrigerant = Refrigerant.R410A });
        var r744 = RefrigerationSolver.Solve(ResidentialAc()
            with { Refrigerant = Refrigerant.R744 });
        Assert.True(r744.CoolingCop < r410.CoolingCop);
    }

    [Fact]
    public void R1234yf_BetterGWP_ThanR134a()
    {
        // R-1234yf GWP < 1 vs R-134a GWP ≈ 1430.
        Assert.True(RefrigerantRegistry.R1234yf.GlobalWarmingPotential
                  < RefrigerantRegistry.R134a.GlobalWarmingPotential / 100.0);
    }

    [Fact]
    public void CarnotCoolingCopFormulaSanityAtThreeFoldGap()
    {
        // At T_h = 4·T_c → COP_Carnot,cooling = T_c / (3·T_c) = 1/3.
        var d = ResidentialAc() with
        {
            ColdReservoirTemperature_K = 100.0,
            HotReservoirTemperature_K  = 400.0,
        };
        var r = RefrigerationSolver.Solve(d);
        Assert.Equal(1.0 / 3.0, r.CarnotCoolingCop, precision: 6);
        Assert.Equal(4.0 / 3.0, r.CarnotHeatingCop, precision: 6);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Residential split AC baseline — R-410A, T_evap = 10 °C, T_cond
    // = 35 °C, 3.5 kW compressor (~ 5-ton unit).
    private static RefrigerationDesign ResidentialAc() => new(
        Mode:                        RefrigerationMode.Cooling,
        Refrigerant:                 Refrigerant.R410A,
        ColdReservoirTemperature_K:  283.15,
        HotReservoirTemperature_K:   308.15,
        CompressorPowerInput_W:     3500.0);
}
