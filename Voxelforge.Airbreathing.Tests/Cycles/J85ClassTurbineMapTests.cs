// J85ClassTurbineMapTests.cs — acceptance for the table-based
// off-design J85-class turbine map. Sibling to TurbineMapTests
// (which tests the constant-η stand-in).

using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class J85ClassTurbineMapTests
{
    [Fact]
    public void AtNominalExtraction_ReturnsExpectedEta()
    {
        // Nominal extraction at J85 design ≈ 280 kJ/kg → extraction
        // fraction = 1.0 → η_t ≈ 0.90 per the curve peak.
        var p = J85ClassTurbineMap.Default.Operate(
            inletStagnationT_K:               1175.0,
            inletStagnationP_Pa:              750_000,
            requiredSpecificWork_J_kg_total:  280_000);

        Assert.InRange(p.IsentropicEfficiency, 0.88, 0.91);
    }

    [Fact]
    public void OutletEnthalpy_MatchesEnergyBalance()
    {
        // h_burnt(T_t5) = h_burnt(T_t4) − W_required
        // For T_t4=1175 K: h_burnt(1175) ≈ 1.10 MJ/kg; subtract
        // 270 kJ/kg → h_burnt(T_t5) ≈ 0.83 MJ/kg → T_t5 ≈ 950-960 K
        // (cp_burnt averages ~1100 J/(kg·K) over 200-960 K).
        var p = J85ClassTurbineMap.Default.Operate(1175.0, 750_000, 270_000);
        Assert.InRange(p.OutletStagnationT_K, 930.0, 980.0);
    }

    [Fact]
    public void OutletPressure_DropsBelowInletAndStaysPositive()
    {
        var p = J85ClassTurbineMap.Default.Operate(1175.0, 750_000, 270_000);
        Assert.True(p.OutletStagnationP_Pa < 750_000);
        Assert.True(p.OutletStagnationP_Pa > 0);
    }

    [Fact]
    public void Diagnostics_ReportInfiniteSurgeMargin()
    {
        // Turbines don't surge — diagnostic sentinel is +∞.
        var p = J85ClassTurbineMap.Default.Operate(1175.0, 750_000, 270_000);
        Assert.NotNull(p.Diagnostics);
        Assert.Equal(double.PositiveInfinity, p.Diagnostics!.SurgeMargin);
    }

    [Fact]
    public void Diagnostics_ChokeMarginRelTrackedAgainstWMaxSafe()
    {
        // ChokeMarginRel = W_required / W_max_safe (450 kJ/kg).
        // At nominal extraction (280 kJ/kg): 280/450 = 0.62.
        var nominal = J85ClassTurbineMap.Default.Operate(1175.0, 750_000, 280_000);
        Assert.NotNull(nominal.Diagnostics);
        Assert.InRange(nominal.Diagnostics!.ChokeMarginRel, 0.55, 0.70);

        // Over-extraction (500 kJ/kg) drives the metric above 1.0,
        // firing the hard CORRECTED_MASS_FLOW_OUT_OF_MAP gate.
        var overExtract = J85ClassTurbineMap.Default.Operate(1175.0, 750_000, 500_000);
        Assert.NotNull(overExtract.Diagnostics);
        Assert.True(overExtract.Diagnostics!.ChokeMarginRel > 1.0);
    }

    [Fact]
    public void Operate_TooMuchWork_Throws()
    {
        // Asking for 10 MJ/kg from 1175 K kerosene burnt gas — would
        // require T_t5 to drive enthalpy negative.
        Assert.Throws<System.InvalidOperationException>(
            () => J85ClassTurbineMap.Default.Operate(1175.0, 750_000, 10_000_000));
    }

    [Fact]
    public void Operate_RejectsNegativeInletState()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => J85ClassTurbineMap.Default.Operate(-100.0, 750_000, 270_000));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => J85ClassTurbineMap.Default.Operate(1175.0, -100, 270_000));
    }

    [Fact]
    public void OffNominal_LowerEfficiency()
    {
        // Both deep-throttle (ext=0.5) and over-extract (ext=1.5) sit
        // away from the η peak at 1.0 → η drops.
        var nominal     = J85ClassTurbineMap.Default.Operate(1175.0, 750_000, 270_000);
        var halfThrottle = J85ClassTurbineMap.Default.Operate(1175.0, 750_000, 135_000);
        var overExtract  = J85ClassTurbineMap.Default.Operate(1175.0, 750_000, 405_000);

        Assert.True(nominal.IsentropicEfficiency >= halfThrottle.IsentropicEfficiency);
        Assert.True(nominal.IsentropicEfficiency >= overExtract.IsentropicEfficiency);
    }

    [Fact]
    public void Operate_DeterministicAcrossCalls()
    {
        var a = J85ClassTurbineMap.Default.Operate(1175.0, 750_000, 270_000);
        var b = J85ClassTurbineMap.Default.Operate(1175.0, 750_000, 270_000);
        Assert.Equal(a.OutletStagnationT_K, b.OutletStagnationT_K, 12);
        Assert.Equal(a.OutletStagnationP_Pa, b.OutletStagnationP_Pa, 12);
    }
}
