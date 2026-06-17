// HawtWave2Tests.cs — Sprint WT.W2 unit tests for the VAWT extension.

using System;
using Voxelforge.WindTurbine;
using Xunit;

namespace Voxelforge.Tests.WindTurbine;

public sealed class HawtWave2Tests
{
    // ── Per-kind C_p anchors ────────────────────────────────────────────

    [Fact]
    public void Hawt_Cp_AtPeak_EqualsHawtAnchor()
    {
        // Default arg + single-arg call: bit-identical to WT.W1.
        Assert.Equal(HawtSolver.PeakPowerCoefficient,
            HawtSolver.ComputePowerCoefficient(HawtSolver.TipSpeedRatioAtPeakCp),
            precision: 9);
    }

    [Fact]
    public void Vawt_Cp_AtPeak_EqualsVawtAnchor()
    {
        Assert.Equal(HawtSolver.VawtPeakPowerCoefficient,
            HawtSolver.ComputePowerCoefficient(
                HawtSolver.VawtTipSpeedRatioAtPeakCp,
                WindTurbineKind.VerticalAxis),
            precision: 9);
    }

    [Fact]
    public void Vawt_PeakCp_LowerThanHawt()
    {
        Assert.True(HawtSolver.VawtPeakPowerCoefficient < HawtSolver.PeakPowerCoefficient);
    }

    [Fact]
    public void Vawt_PeakLambda_LowerThanHawt()
    {
        Assert.True(HawtSolver.VawtTipSpeedRatioAtPeakCp < HawtSolver.TipSpeedRatioAtPeakCp);
    }

    // ── End-to-end VAWT solve ───────────────────────────────────────────

    [Fact]
    public void Vawt_AtVawtPeakLambda_LandsHigherCpThanAtHawtPeak()
    {
        // Same wind speed, two designs at different λ_design. The VAWT
        // tuned to λ=5 should beat the VAWT tuned to λ=7.5 (off-peak).
        var v_peak    = SmallVawt() with { DesignTipSpeedRatio = 5.0 };
        var v_offpeak = SmallVawt() with { DesignTipSpeedRatio = 7.5 };
        var r_peak    = HawtSolver.Solve(v_peak,    windSpeed_ms: 8.0);
        var r_offpeak = HawtSolver.Solve(v_offpeak, windSpeed_ms: 8.0);
        Assert.True(r_peak.PowerCoefficient > r_offpeak.PowerCoefficient);
    }

    [Fact]
    public void Vawt_AtDesignPoint_DeliversNonZeroElectricalPower()
    {
        var r = HawtSolver.Solve(SmallVawt(), windSpeed_ms: 8.0);
        Assert.True(r.ElectricalPower_W > 0);
    }

    [Fact]
    public void Vawt_PowerLowerThanEquivalentHawt_AtSameDesignParams()
    {
        // Same geometry, same λ, same wind — but Vawt has lower peak C_p.
        var vawt = SmallVawt();
        var hawt = SmallVawt() with
        {
            Kind                = WindTurbineKind.HorizontalAxis,
            DesignTipSpeedRatio = 7.5,
        };
        var r_v = HawtSolver.Solve(vawt, windSpeed_ms: 8.0);
        var r_h = HawtSolver.Solve(hawt, windSpeed_ms: 8.0);
        // The HAWT here is at λ=7.5 which is its peak; the VAWT is at
        // λ=5 which is its peak. Both at their respective peaks, the
        // HAWT still wins because peak C_p_hawt > peak C_p_vawt.
        Assert.True(r_h.ElectricalPower_W > r_v.ElectricalPower_W);
    }

    [Fact]
    public void Validate_AcceptsVerticalAxis()
    {
        var d = SmallVawt();
        d.ValidateSelf();    // must not throw
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Small residential VAWT — 2 m radius, 4 kW class.
    private static HawtDesign SmallVawt() => new(
        Kind:                            WindTurbineKind.VerticalAxis,
        RotorRadius_m:                   2.0,
        BladeCount:                      3,
        HubHeight_m:                     12.0,
        DesignWindSpeed_ms:              8.0,
        DesignTipSpeedRatio:             5.0,
        GearboxAndGeneratorEfficiency:   0.90,
        CutInWindSpeed_ms:               3.0,
        CutOutWindSpeed_ms:              25.0);
}
