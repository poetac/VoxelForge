// J85ClassCompressorMapTests.cs — acceptance for the table-based
// off-design J85-class compressor map. Sibling to CompressorMapTests
// (which tests the constant-η stand-in).

using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class J85ClassCompressorMapTests
{
    [Fact]
    public void AtDesignPoint_ReturnsExpectedPiAndEta()
    {
        // Design point: π_c = 8.0 at SLS inlet (T_in = 288.15 K,
        // P_in = 101_325 Pa). Expected η ≈ 0.85, ṁ_corr ≈ 20 kg/s.
        var p = J85ClassCompressorMap.Default.Operate(288.15, 101_325, 8.0);

        Assert.InRange(p.IsentropicEfficiency, 0.83, 0.86);
        Assert.NotNull(p.Diagnostics);
        Assert.InRange(p.Diagnostics!.CorrectedMassFlow_kg_s, 19.0, 21.0);
    }

    [Fact]
    public void AtDesignPoint_HasComfortableSurgeMargin()
    {
        // Industry preliminary-design floor for surge margin is 10 %;
        // J85 mil-dry design point sits at ~22 %.
        var p = J85ClassCompressorMap.Default.Operate(288.15, 101_325, 8.0);
        Assert.NotNull(p.Diagnostics);
        Assert.True(p.Diagnostics!.SurgeMargin > 0.10,
            $"Expected surge margin > 10 % at design point; got {p.Diagnostics.SurgeMargin:P1}");
    }

    [Fact]
    public void NearSurge_SurgeMarginShrinks()
    {
        // π = 9.0 sits closer to the surge-line peak (9.4 at ṁ=18).
        // Surge margin should shrink toward zero.
        var p = J85ClassCompressorMap.Default.Operate(288.15, 101_325, 9.0);
        Assert.NotNull(p.Diagnostics);
        Assert.True(p.Diagnostics!.SurgeMargin < 0.15,
            $"Expected surge margin < 15 % near surge; got {p.Diagnostics.SurgeMargin:P1}");
        Assert.True(p.Diagnostics.SurgeMargin >= 0.0,
            $"Surge margin at π=9.0 should still be non-negative (below the surge line peak 9.4); got {p.Diagnostics.SurgeMargin:P1}");
    }

    [Fact]
    public void AboveSurgeLine_DiagnosticsFlagOutOfMap()
    {
        // π = 10.0 is above the 100 % N surge peak (9.4). The map
        // edge-clamps to the surge edge but flags out-of-map via
        // negative SurgeMargin.
        var p = J85ClassCompressorMap.Default.Operate(288.15, 101_325, 10.0);
        Assert.NotNull(p.Diagnostics);
        Assert.True(p.Diagnostics!.SurgeMargin < 0.0,
            $"Above surge: SurgeMargin should be negative; got {p.Diagnostics.SurgeMargin:P1}");
    }

    [Fact]
    public void NearChoke_ChokeMarginApproachesUnity()
    {
        // π = 4.0 sits near the choke edge (right end of speed line).
        // ChokeMarginRel = ṁ_op / ṁ_choke approaches 1.0.
        var p = J85ClassCompressorMap.Default.Operate(288.15, 101_325, 4.0);
        Assert.NotNull(p.Diagnostics);
        Assert.True(p.Diagnostics!.ChokeMarginRel > 0.95,
            $"Near choke: ChokeMarginRel should approach 1.0; got {p.Diagnostics.ChokeMarginRel:F3}");
    }

    [Fact]
    public void BelowChokeLine_DiagnosticsFlagOutOfMap()
    {
        // π = 3.0 falls below the right edge (4.0). Out of map.
        var p = J85ClassCompressorMap.Default.Operate(288.15, 101_325, 3.0);
        Assert.NotNull(p.Diagnostics);
        Assert.True(p.Diagnostics!.ChokeMarginRel > 1.0,
            $"Past choke: ChokeMarginRel should exceed 1.0; got {p.Diagnostics.ChokeMarginRel:F3}");
    }

    [Fact]
    public void Operate_RaisesPressureByPi()
    {
        var p = J85ClassCompressorMap.Default.Operate(288.15, 100_000, 8.0);
        Assert.Equal(800_000, p.OutletStagnationP_Pa, 0);
    }

    [Fact]
    public void Operate_RaisesTemperatureViaIsentropicAndEnthalpyBalance()
    {
        // T_isen = 288.15 · 8^0.286 ≈ 521.7 K
        // η = 0.85 → ΔT_actual ≈ ΔT_isen / 0.85 ≈ 274.7 K
        // T_actual ≈ 562.9 K (constant-cp); cp_air(T) drift is small (~5 K)
        var p = J85ClassCompressorMap.Default.Operate(288.15, 100_000, 8.0);
        Assert.InRange(p.OutletStagnationT_K, 555.0, 580.0);
    }

    [Fact]
    public void Operate_RejectsSubUnityPressureRatio()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => J85ClassCompressorMap.Default.Operate(288.15, 101_325, 0.5));
    }

    [Fact]
    public void HigherPressureRatio_GivesMoreSpecificWork()
    {
        var lo = J85ClassCompressorMap.Default.Operate(288.15, 101_325, 6.0);
        var hi = J85ClassCompressorMap.Default.Operate(288.15, 101_325, 8.0);
        Assert.True(hi.SpecificWork_J_kg > lo.SpecificWork_J_kg);
    }

    [Fact]
    public void Operate_DeterministicAcrossCalls()
    {
        var a = J85ClassCompressorMap.Default.Operate(288.15, 101_325, 8.0);
        var b = J85ClassCompressorMap.Default.Operate(288.15, 101_325, 8.0);
        Assert.Equal(a.OutletStagnationT_K, b.OutletStagnationT_K, 12);
        Assert.Equal(a.OutletStagnationP_Pa, b.OutletStagnationP_Pa, 12);
        Assert.Equal(a.IsentropicEfficiency, b.IsentropicEfficiency, 12);
        Assert.Equal(a.SpecificWork_J_kg, b.SpecificWork_J_kg, 6);
    }
}
