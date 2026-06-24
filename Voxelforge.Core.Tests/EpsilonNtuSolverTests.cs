// EpsilonNtuSolverTests — pins the closed-form heat-exchanger math: the
// counterflow ε-NTU effectiveness and the 1-D fin efficiency η = tanh(mL)/(mL).
// Pure closed form → exact assertions. Backfills coverage onto the Linux 'core'
// CI leg (the full Solve geometry/correlation path lives in Voxelforge.Tests,
// net9.0-windows, which only runs on the offline self-hosted runner).

using System;
using Voxelforge.HeatExchanger;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class EpsilonNtuSolverTests
{
    [Fact]
    public void Effectiveness_ZeroNtu_IsZero()
        => Assert.Equal(0.0, EpsilonNtuSolver.ComputeCounterflowEffectiveness(0.0, 0.5), 12);

    [Fact]
    public void Effectiveness_BalancedFlow_UsesNtuOverOnePlusNtu()
        // C_r = 1 (balanced) asymptote: ε = NTU/(1+NTU); NTU=1 → 0.5.
        => Assert.Equal(0.5, EpsilonNtuSolver.ComputeCounterflowEffectiveness(1.0, 1.0), 12);

    [Fact]
    public void Effectiveness_CapacityRatioZero_IsOneMinusExpNegNtu()
        // C_r = 0 (one stream phase-changing): ε = 1 − e^(−NTU).
        => Assert.Equal(1.0 - Math.Exp(-2.0),
            EpsilonNtuSolver.ComputeCounterflowEffectiveness(2.0, 0.0), 12);

    [Fact]
    public void Effectiveness_GeneralCase_MatchesCounterflowClosedForm()
    {
        // ε = (1 − e^(−NTU(1−Cr))) / (1 − Cr·e^(−NTU(1−Cr))) for NTU=1, Cr=0.5.
        double e = Math.Exp(-1.0 * (1.0 - 0.5));
        double expected = (1.0 - e) / (1.0 - 0.5 * e);
        Assert.Equal(expected, EpsilonNtuSolver.ComputeCounterflowEffectiveness(1.0, 0.5), 12);
    }

    [Fact]
    public void Effectiveness_RejectsOutOfRangeInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EpsilonNtuSolver.ComputeCounterflowEffectiveness(-0.1, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EpsilonNtuSolver.ComputeCounterflowEffectiveness(1.0, 1.5));
    }

    [Fact]
    public void FinEfficiency_UnitML_IsTanhOne()
    {
        // m = √(2h/(k·t)) = √(2·1/(2·1)) = 1; L = 1 → mL = 1 → η = tanh(1)/1.
        double eta = EpsilonNtuSolver.ComputeFinEfficiency(
            heatTransferCoefficient_W_m2K: 1.0, finThermalConductivity_WmK: 2.0,
            finThickness_m: 1.0, finHalfHeight_m: 1.0);
        Assert.Equal(Math.Tanh(1.0), eta, 12);
    }

    [Fact]
    public void FinEfficiency_ApproachesUnity_ForShortStiffFin()
    {
        // Very small mL (short, thick, high-k fin) → near-isothermal → η ≈ 1.
        double eta = EpsilonNtuSolver.ComputeFinEfficiency(
            heatTransferCoefficient_W_m2K: 1.0, finThermalConductivity_WmK: 1.0e6,
            finThickness_m: 0.01, finHalfHeight_m: 1.0e-4);
        Assert.True(eta > 0.999999 && eta <= 1.0, $"η_fin should be ~1 for a stiff fin; got {eta}");
    }
}
