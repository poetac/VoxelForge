// AblationDischargeModelTests.cs — Sprint EP.W2.PPT physics tests.
// Mirror of MaeckerKovityaArcModelTests for the Solbes-Vondra ablation-
// discharge model.

using System;
using Voxelforge.ElectricPropulsion.Solvers;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class AblationDischargeModelTests
{
    // EO-1 EP-12 reference inputs.
    private const double Eo1_Ecap_J  = 22.0;
    private const double Eo1_F_Hz    =  5.0;
    private const double Eo1_Gap_mm  = 25.0;
    private const double Eo1_BarL_mm = 25.0;
    private const double Eo1_W_mm    = 15.0;

    [Fact]
    public void Solve_Eo1_ConvergesAndReturnsAllOutputs()
    {
        var r = AblationDischargeModel.Solve(
            Eo1_Ecap_J, Eo1_F_Hz, Eo1_Gap_mm, Eo1_BarL_mm, Eo1_W_mm,
            ispOverride_s: double.NaN);
        Assert.True(r.Converged);
        Assert.True(r.ImpulseBit_Ns > 0);
        Assert.True(r.MassPerPulse_kg > 0);
        Assert.True(r.AverageThrust_N > 0);
        Assert.True(r.AverageIsp_s > 0);
        Assert.True(r.ExitVelocity_ms > 0);
        Assert.True(r.PlumeDivergenceHalfAngle_rad > 0);
    }

    [Fact]
    public void Solve_Eo1_ImpulseBitMatchesSolbesVondraSquareRoot()
    {
        // Cluster-anchor mode: I_bit = K_i · √E_cap.
        var r = AblationDischargeModel.Solve(
            Eo1_Ecap_J, Eo1_F_Hz, Eo1_Gap_mm, Eo1_BarL_mm, Eo1_W_mm,
            ispOverride_s: double.NaN);
        double expected = AblationDischargeModel.ImpulseBitCoefficient * Math.Sqrt(Eo1_Ecap_J);
        Assert.Equal(expected, r.ImpulseBit_Ns, precision: 12);
    }

    [Fact]
    public void Solve_Eo1_MassPerPulseLinearInEnergy()
    {
        // Δm = K_m · E_cap.
        var r = AblationDischargeModel.Solve(
            Eo1_Ecap_J, Eo1_F_Hz, Eo1_Gap_mm, Eo1_BarL_mm, Eo1_W_mm,
            ispOverride_s: double.NaN);
        double expected = AblationDischargeModel.MassPerPulseCoefficient * Eo1_Ecap_J;
        Assert.Equal(expected, r.MassPerPulse_kg, precision: 15);
    }

    [Fact]
    public void Solve_AveragePowerEqualsEcapTimesFpulse()
    {
        var r = AblationDischargeModel.Solve(
            Eo1_Ecap_J, Eo1_F_Hz, Eo1_Gap_mm, Eo1_BarL_mm, Eo1_W_mm,
            ispOverride_s: double.NaN);
        Assert.Equal(Eo1_Ecap_J * Eo1_F_Hz, r.AveragePower_W, precision: 9);
    }

    [Fact]
    public void Solve_IspOverride_ForcesExitVelocity()
    {
        const double targetIsp_s = 600.0;
        var r = AblationDischargeModel.Solve(
            Eo1_Ecap_J, Eo1_F_Hz, Eo1_Gap_mm, Eo1_BarL_mm, Eo1_W_mm,
            ispOverride_s: targetIsp_s);
        // v_exit = Isp · g0
        Assert.Equal(targetIsp_s * AblationDischargeModel.g0, r.ExitVelocity_ms, precision: 9);
        Assert.Equal(targetIsp_s, r.AverageIsp_s, precision: 9);
    }

    [Fact]
    public void Solve_NegativeEcap_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AblationDischargeModel.Solve(
            -1.0, Eo1_F_Hz, Eo1_Gap_mm, Eo1_BarL_mm, Eo1_W_mm, double.NaN));
    }

    [Fact]
    public void Solve_ZeroFrequency_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AblationDischargeModel.Solve(
            Eo1_Ecap_J, 0.0, Eo1_Gap_mm, Eo1_BarL_mm, Eo1_W_mm, double.NaN));
    }

    [Fact]
    public void Solve_ZeroElectrodeGap_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AblationDischargeModel.Solve(
            Eo1_Ecap_J, Eo1_F_Hz, 0.0, Eo1_BarL_mm, Eo1_W_mm, double.NaN));
    }

    [Fact]
    public void Solve_ZeroBarLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AblationDischargeModel.Solve(
            Eo1_Ecap_J, Eo1_F_Hz, Eo1_Gap_mm, 0.0, Eo1_W_mm, double.NaN));
    }

    [Fact]
    public void Solve_ZeroElectrodeWidth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AblationDischargeModel.Solve(
            Eo1_Ecap_J, Eo1_F_Hz, Eo1_Gap_mm, Eo1_BarL_mm, 0.0, double.NaN));
    }

    [Fact]
    public void Solve_NegativeIspOverride_Throws()
    {
        // NaN is allowed (cluster-anchor mode); negative is not.
        Assert.Throws<ArgumentOutOfRangeException>(() => AblationDischargeModel.Solve(
            Eo1_Ecap_J, Eo1_F_Hz, Eo1_Gap_mm, Eo1_BarL_mm, Eo1_W_mm,
            ispOverride_s: -100.0));
    }

    [Fact]
    public void Solve_IspIsExitVelocityOverGravity()
    {
        var r = AblationDischargeModel.Solve(
            Eo1_Ecap_J, Eo1_F_Hz, Eo1_Gap_mm, Eo1_BarL_mm, Eo1_W_mm,
            ispOverride_s: double.NaN);
        Assert.Equal(r.ExitVelocity_ms / AblationDischargeModel.g0, r.AverageIsp_s, precision: 9);
    }
}
