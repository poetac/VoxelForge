// StirlingSolverTests — Issue #84 (STR.W2): pins the closed-form West's-
// number Stirling math (see StirlingSolver.cs file header for the full
// derivation + GPU-3 calibration cross-check): η_Carnot = 1 - T_c/T_h,
// τ = (T_h-T_c)/(T_h+T_c), MEP = Wn·P_mean·τ·fluidFactor, W = MEP·V_swept,
// P = W·f, Q_hot = P/η_indicated, Q_cold = Q_hot - P. Pure closed form →
// exact assertions, hand-derived (Python mirror of the exact formula
// chain) rather than read off a test run. This is the first Linux fixture
// for the Stirling family (previously the only Wave-1 pillar without
// one — see ROADMAP "Now" criterion 2), unblocked by this MEP-model fix.
//
// Fail-on-old context (STR.W1's flat "MEP = 0.5·P_mean" fit, now
// replaced): it had no τ-dependence at all, so a design with T_hot=310 K,
// T_cold=300 K (τ ≈ 0.016 -- barely warm) would have produced the SAME
// MEP as a T_hot=900 K/T_cold=300 K design (τ = 0.5) at the same
// pressure -- physically wrong, since a near-zero temperature
// differential should yield near-zero net power. Solve_LowDeltaTau_
// PowerShrinksTowardZero below pins the corrected behaviour directly;
// it would fail against the old constant-MEP model (which doesn't read
// T_hot/T_cold into the MEP calculation at all).

using System;
using Voxelforge.Stirling;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class StirlingSolverTests
{
    private static StirlingDesign HeliumAnchor() => new(
        Configuration:          StirlingConfiguration.Beta,
        HotSideTemperature_K:   900.0,
        ColdSideTemperature_K:  300.0,
        MeanPressure_Pa:        5.0e6,
        SweptVolume_m3:         1.0e-4,
        OperatingFrequency_Hz:  50.0,
        SecondLawEfficiency:    0.5);

    [Fact]
    public void Solve_HeliumAnchor_MatchesHandDerivedClosedForm()
    {
        // eta_carnot = 1 - 300/900 = 2/3; tau = 600/1200 = 0.5.
        // mep = 0.25 * 5e6 * 0.5 * 1.0 = 625,000 Pa (exact in double).
        // W = 625,000 * 1e-4 = 62.5 J; P = 62.5 * 50 = 3125 W (both exact).
        // eta_indicated = 0.5*1.0*eta_carnot = 1/3
        // (neither eta_carnot nor eta_indicated is exactly representable in
        // binary -- expected values below use
        // the identical expression/operation-order the solver itself uses, so
        // the comparison is bit-exact rather than relying on precision-digit
        // rounding to paper over a divergent rounding path).
        // Q_hot = 3125/eta_indicated ~= 9375 W; Q_cold = Q_hot - 3125 ~= 6250 W.
        var r = StirlingSolver.Solve(HeliumAnchor());

        double etaCarnotExact = 1.0 - 300.0 / 900.0;
        double etaIndicatedExact = 0.5 * 1.0 * etaCarnotExact;
        double qHotExact = 3125.0 / etaIndicatedExact;

        Assert.Equal(etaCarnotExact, r.CarnotEfficiency, 15);
        Assert.Equal(etaIndicatedExact, r.IndicatedEfficiency, 15);
        Assert.Equal(625_000.0, r.MeanEffectivePressure_Pa, 6);
        Assert.Equal(62.5, r.WorkPerCycle_J, 9);
        Assert.Equal(3125.0, r.IndicatedPower_W, 9);
        Assert.Equal(qHotExact, r.HeatInputRate_W, 9);
        Assert.Equal(qHotExact - 3125.0, r.HeatRejectionRate_W, 9);
    }

    [Fact]
    public void Solve_AirWorkingFluid_AppliesFluidFactorToMepAndEfficiency()
    {
        // Air's 0.85 fluidFactor multiplies both MEP (via the West's-number
        // term) and eta_indicated (via GetWorkingFluidEfficiencyFactor),
        // but NOT eta_carnot (a pure temperature-ratio quantity). Expected
        // values use the same expression/operation-order as the solver for
        // a bit-exact comparison (see the anchor test above for why).
        var r = StirlingSolver.Solve(HeliumAnchor() with { WorkingFluid = StirlingWorkingFluid.Air });

        double etaCarnotExact = 1.0 - 300.0 / 900.0;
        Assert.Equal(etaCarnotExact, r.CarnotEfficiency, 15);            // unaffected by fluid
        Assert.Equal(0.5 * 0.85 * etaCarnotExact, r.IndicatedEfficiency, 15);
        Assert.Equal(0.25 * 5.0e6 * 0.5 * 0.85, r.MeanEffectivePressure_Pa, 6);
        Assert.True(r.IndicatedPower_W < 3125.0, "Air's derating must reduce power vs the Helium anchor");
    }

    [Fact]
    public void Solve_LowDeltaTau_PowerShrinksTowardZero()
    {
        // Fail-on-old proof: T_hot=310/T_cold=300 (tau ~ 0.0164) at the SAME
        // pressure/volume/frequency as the Helium anchor (tau = 0.5) must
        // produce dramatically less power under West's number -- the old
        // flat "MEP = 0.5*P_mean" model had no tau term at all, so it would
        // have produced IDENTICAL power regardless of how close T_hot is to
        // T_cold, which is not physically possible (a vanishing temperature
        // differential must yield vanishing net Stirling power).
        var lowDeltaT = HeliumAnchor() with { HotSideTemperature_K = 310.0, ColdSideTemperature_K = 300.0 };
        var lowResult = StirlingSolver.Solve(lowDeltaT);
        var anchorResult = StirlingSolver.Solve(HeliumAnchor());

        Assert.True(lowResult.IndicatedPower_W < anchorResult.IndicatedPower_W / 20.0,
            $"low-tau power {lowResult.IndicatedPower_W:F2} W should be a small fraction of " +
            $"the high-tau anchor's {anchorResult.IndicatedPower_W:F2} W");
        Assert.True(lowResult.IndicatedPower_W > 0.0);
    }

    [Fact]
    public void Solve_IndicatedPower_ScalesLinearlyWithMeanPressureAndFrequency()
    {
        var baseline = StirlingSolver.Solve(HeliumAnchor());
        var doublePressure = StirlingSolver.Solve(HeliumAnchor() with { MeanPressure_Pa = 10.0e6 });
        var doubleFrequency = StirlingSolver.Solve(HeliumAnchor() with { OperatingFrequency_Hz = 100.0 });

        Assert.Equal(2.0 * baseline.IndicatedPower_W, doublePressure.IndicatedPower_W, 6);
        Assert.Equal(2.0 * baseline.IndicatedPower_W, doubleFrequency.IndicatedPower_W, 6);
    }

    [Fact]
    public void Solve_IndicatedEfficiency_NeverExceedsCarnotEfficiency()
    {
        // eta_indicated = eta_2nd * fluidFactor * eta_carnot with both
        // multipliers in (0, 1], so eta_indicated <= eta_carnot always --
        // the 2nd-law bound the whole model is built to respect.
        foreach (var fluid in new[] { StirlingWorkingFluid.Helium, StirlingWorkingFluid.Hydrogen, StirlingWorkingFluid.Air })
        {
            var r = StirlingSolver.Solve(HeliumAnchor() with { WorkingFluid = fluid, SecondLawEfficiency = 0.65 });
            Assert.True(r.IndicatedEfficiency <= r.CarnotEfficiency,
                $"fluid {fluid}: eta_indicated {r.IndicatedEfficiency} exceeded eta_carnot {r.CarnotEfficiency}");
        }
    }

    [Fact]
    public void Solve_NullDesign_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StirlingSolver.Solve(null!));
    }

    [Fact]
    public void Solve_HotSideNotAboveColdSide_Throws()
    {
        var bad = HeliumAnchor() with { HotSideTemperature_K = 300.0, ColdSideTemperature_K = 300.0 };
        Assert.Throws<ArgumentException>(() => StirlingSolver.Solve(bad));
    }
}
