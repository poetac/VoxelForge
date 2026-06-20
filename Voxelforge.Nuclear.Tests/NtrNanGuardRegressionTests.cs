// NtrNanGuardRegressionTests.cs — regression guard for the NTR NaN-Isp bug
// (red-team finding). LH2ThermalProperties.Gamma is a linear fit that crossed
// γ = 1 near 10 300 K (and went negative beyond ~35 000 K). A high-power /
// low-mass-flow design — well within the NTR SA bounds (power ≤ 2000 MW,
// ṁ ≥ 1 kg/s) — drives the core-exit temperature into that range, so √γ and
// the (γ−1) denominators in NtrCycleSolver produced NaN c*/Isp/thrust. The
// γ floor (1.05) keeps the cycle solver finite. These tests fail on the
// unclamped formula and pass with the floor.

using Voxelforge.Combustion;   // LH2ThermalProperties (internal; InternalsVisibleTo Nuclear.Tests)
using Voxelforge.Nuclear;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class NtrNanGuardRegressionTests
{
    private static NuclearThermalDesign ExtremeDesign(double power_MW, double mDot_kgs) => new(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:  power_MW,
        ReactorCoreLength_mm:    1400.0,
        ReactorCoreDiameter_mm:  1400.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  mDot_kgs,
        ChamberPressure_bar:     34.0,
        ThroatRadius_mm:         120.0,
        ExpansionRatio:          100.0,
        NozzleLength_mm:         4000.0,
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       200,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0);

    [Fact]
    public void Gamma_StaysAboveOne_AtExtrapolatedTemperatures()
    {
        // Far above the 300–3000 K fit range the unclamped fit goes ≤ 0;
        // the floor must keep γ physical (> 1) so downstream √γ / (γ−1) are real.
        foreach (double t in new[] { 11_000.0, 50_000.0, 200_000.0 })
        {
            double g = LH2ThermalProperties.Gamma(t);
            Assert.True(double.IsFinite(g) && g >= 1.05,
                $"γ({t} K) = {g} must stay ≥ 1.05");
        }
    }

    [Fact]
    public void ExtremePowerLowMassFlow_ProducesFiniteIsp_NotNaN()
    {
        // power 2000 MW + ṁ 1 kg/s (both at the SA-bound extremes) drives the
        // core exit tens of thousands of K — the regime that used to NaN.
        var cond = new NuclearThermalConditions(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);
        var result = NuclearOptimization.GenerateWith(ExtremeDesign(power_MW: 2000.0, mDot_kgs: 1.0), cond);

        Assert.True(double.IsFinite(result.CoreExitTemp_K), $"T_exit = {result.CoreExitTemp_K}");
        Assert.True(double.IsFinite(result.IspVacuum_s) && result.IspVacuum_s > 0.0,
            $"Isp must be finite & positive, got {result.IspVacuum_s}");
        Assert.True(double.IsFinite(result.ThrustVacuum_N) && result.ThrustVacuum_N > 0.0,
            $"thrust must be finite & positive, got {result.ThrustVacuum_N}");
    }
}
