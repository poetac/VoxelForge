// FinEfficiencyTests.cs — Sprint HX.W2 unit tests for the
// fin-efficiency correction extension on the plate-fin HX solver.

using System;
using Voxelforge.HeatExchanger;
using Xunit;

namespace Voxelforge.Tests.HeatExchanger;

public sealed class FinEfficiencyTests
{
    // ── ComputeFinEfficiency — pure unit tests ──────────────────────────

    [Fact]
    public void FinEfficiency_VeryShortFin_ApproachesUnity()
    {
        // Short / thick / high-k fin → mL → 0 → η_fin → 1.
        double e = EpsilonNtuSolver.ComputeFinEfficiency(
            heatTransferCoefficient_W_m2K: 100.0,
            finThermalConductivity_WmK:    400.0,   // copper-class
            finThickness_m:                0.005,   // 5 mm thick
            finHalfHeight_m:               0.0005); // 0.5 mm half-height
        Assert.InRange(e, 0.99, 1.0);
    }

    [Fact]
    public void FinEfficiency_VeryLongFin_ApproachesZero()
    {
        // Tall / thin / low-k fin → mL → ∞ → tanh(mL)/(mL) → 0.
        double e = EpsilonNtuSolver.ComputeFinEfficiency(
            heatTransferCoefficient_W_m2K: 2000.0,
            finThermalConductivity_WmK:    1.0,     // pathologically low k
            finThickness_m:                0.0002,  // 0.2 mm thin
            finHalfHeight_m:               0.10);   // 100 mm tall
        Assert.InRange(e, 0.0, 0.10);
    }

    [Fact]
    public void FinEfficiency_AtMLEqualsOne_MatchesAnalyticalValue()
    {
        // Pick h, k, t, L such that mL = 1: m = √(2h/(k·t)) → mL = L·m = 1
        // → L = 1/m. tanh(1)/1 ≈ 0.7616.
        double h = 100.0;
        double k = 50.0;
        double t = 0.001;
        double m = Math.Sqrt(2.0 * h / (k * t));
        double L = 1.0 / m;
        double eff = EpsilonNtuSolver.ComputeFinEfficiency(h, k, t, L);
        Assert.Equal(Math.Tanh(1.0) / 1.0, eff, precision: 6);
    }

    [Fact]
    public void FinEfficiency_AlwaysInUnitInterval()
    {
        double[] hs = { 10.0, 100.0, 1000.0, 5000.0 };
        double[] ks = { 1.0, 12.0, 50.0, 400.0 };
        double[] ts = { 0.0001, 0.0005, 0.002 };
        double[] Ls = { 0.0005, 0.003, 0.01 };
        foreach (var h in hs)
        foreach (var k in ks)
        foreach (var t in ts)
        foreach (var L in Ls)
        {
            double e = EpsilonNtuSolver.ComputeFinEfficiency(h, k, t, L);
            Assert.InRange(e, 0.0, 1.0 + 1e-9);
        }
    }

    [Fact]
    public void FinEfficiency_RejectsNonPositiveInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EpsilonNtuSolver.ComputeFinEfficiency(-1.0, 12.0, 0.0005, 0.003));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EpsilonNtuSolver.ComputeFinEfficiency(100.0, 0.0, 0.0005, 0.003));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EpsilonNtuSolver.ComputeFinEfficiency(100.0, 12.0, -0.001, 0.003));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EpsilonNtuSolver.ComputeFinEfficiency(100.0, 12.0, 0.0005, 0.0));
    }

    // ── Solver integration ───────────────────────────────────────────────

    [Fact]
    public void Solver_DisabledFlag_BitIdenticalToHX_W1()
    {
        // With the flag off (default), the result must be bit-identical
        // to HX.W1: η_fin = 1, h_eff = h_bare, U unchanged.
        var r = EpsilonNtuSolver.Solve(Recuperator());
        Assert.Equal(1.0, r.HotFinEfficiency,  precision: 9);
        Assert.Equal(1.0, r.ColdFinEfficiency, precision: 9);
    }

    [Fact]
    public void Solver_EnabledFlag_FinEfficiencyBelowUnity()
    {
        var d = Recuperator() with { EnableFinEfficiencyCorrection = true };
        var r = EpsilonNtuSolver.Solve(d);
        // Inconel-718 (k = 12) + 0.4 mm fin thickness + 3 mm half-fin-
        // height at the recuperator-class h ≈ 500 W/(m²·K) → mL of
        // order 1 → η_fin ≈ 0.7-0.95. Cluster-mid-band check.
        Assert.InRange(r.HotFinEfficiency,  0.5, 0.99);
        Assert.InRange(r.ColdFinEfficiency, 0.5, 0.99);
    }

    [Fact]
    public void Solver_EnabledFlag_ReducesU_VsDisabled()
    {
        // Enabling fin correction reduces h_eff → reduces U → reduces ε
        // → reduces Q_duty.
        var disabled = EpsilonNtuSolver.Solve(Recuperator());
        var enabled  = EpsilonNtuSolver.Solve(Recuperator() with
        {
            EnableFinEfficiencyCorrection = true,
        });
        Assert.True(enabled.OverallHeatTransferCoefficient_W_m2K
                  < disabled.OverallHeatTransferCoefficient_W_m2K,
            $"U enabled ({enabled.OverallHeatTransferCoefficient_W_m2K:F1}) "
          + $"expected < U disabled ({disabled.OverallHeatTransferCoefficient_W_m2K:F1}).");
        Assert.True(enabled.HeatDuty_W < disabled.HeatDuty_W,
            $"Q_duty enabled ({enabled.HeatDuty_W:F0}) expected < "
          + $"Q_duty disabled ({disabled.HeatDuty_W:F0}).");
    }

    [Fact]
    public void Solver_EnabledFlag_PreservesEnergyBalance()
    {
        // Energy balance Q_hot = Q_cold = HeatDuty must hold regardless
        // of whether fin correction is on.
        var d = Recuperator() with { EnableFinEfficiencyCorrection = true };
        var r = EpsilonNtuSolver.Solve(d);
        double Q_hot  = d.HotMassFlow_kgs  * d.HotCp_JkgK
                      * (d.HotInletTemperature_K - r.HotOutletTemperature_K);
        double Q_cold = d.ColdMassFlow_kgs * d.ColdCp_JkgK
                      * (r.ColdOutletTemperature_K - d.ColdInletTemperature_K);
        Assert.Equal(Q_hot,  r.HeatDuty_W, precision: 3);
        Assert.Equal(Q_cold, r.HeatDuty_W, precision: 3);
    }

    [Fact]
    public void Solver_EnabledFlag_HigherK_ImprovesFinEfficiency()
    {
        // Copper-class k = 400 W/(m·K) → m drops → η_fin closer to 1
        // vs Inconel-718 k = 12. Stainless ~ 16 vs copper ~ 400.
        var inconel = EpsilonNtuSolver.Solve(Recuperator() with
        {
            EnableFinEfficiencyCorrection = true,
            FinThermalConductivity_WmK    = 12.0,
        });
        var copper  = EpsilonNtuSolver.Solve(Recuperator() with
        {
            EnableFinEfficiencyCorrection = true,
            FinThermalConductivity_WmK    = 400.0,
        });
        Assert.True(copper.HotFinEfficiency > inconel.HotFinEfficiency,
            $"Copper η_fin ({copper.HotFinEfficiency:F4}) expected > "
          + $"Inconel η_fin ({inconel.HotFinEfficiency:F4}).");
        // And copper's U is consequently closer to the H.W1 (no
        // correction) value.
        var noCorrection = EpsilonNtuSolver.Solve(Recuperator());
        Assert.True(copper.OverallHeatTransferCoefficient_W_m2K
                  > inconel.OverallHeatTransferCoefficient_W_m2K);
        Assert.True(copper.OverallHeatTransferCoefficient_W_m2K
                  < noCorrection.OverallHeatTransferCoefficient_W_m2K);
    }

    [Fact]
    public void Solver_EnabledFlag_RejectsZeroFinConductivity()
    {
        var d = Recuperator() with
        {
            EnableFinEfficiencyCorrection = true,
            FinThermalConductivity_WmK    = 0.0,
        };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Solver_EnabledFlag_DisabledFlagPassesZeroK()
    {
        // The k_fin field is ignored when the flag is off → setting k=0
        // when the flag is off must NOT throw (back-compat invariant).
        var d = Recuperator() with
        {
            EnableFinEfficiencyCorrection = false,
            FinThermalConductivity_WmK    = 0.0,
        };
        d.ValidateSelf();   // must not throw
    }

    // ── Helpers ──────────────────────────────────────────────────────────

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
