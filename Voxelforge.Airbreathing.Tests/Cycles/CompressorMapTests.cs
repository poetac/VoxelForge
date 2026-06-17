// CompressorMapTests.cs — Sprint A7 acceptance for the parametric
// constant-η compressor map.

using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class CompressorMapTests
{
    [Fact]
    public void DefaultMap_HasMattinglyEfficiency()
    {
        Assert.Equal(0.85, ConstantEfficiencyCompressorMap.Default.IsentropicEfficiency, 6);
    }

    [Fact]
    public void Operate_RaisesPressureByPi()
    {
        var map = ConstantEfficiencyCompressorMap.Default;
        var p = map.Operate(inletStagnationT_K: 288.15, inletStagnationP_Pa: 100_000, pressureRatio: 8.0);
        Assert.Equal(800_000, p.OutletStagnationP_Pa, 0);
    }

    [Fact]
    public void Operate_RaisesTemperaturePerIsentropicRelation()
    {
        var map = ConstantEfficiencyCompressorMap.Default;
        var p = map.Operate(288.15, 100_000, 8.0);
        // Isentropic: T = 288.15 · 8^(0.4/1.4) ≈ 521.7 K
        // η_c = 0.85 → T_actual = 288.15 + (521.7 − 288.15)/0.85 ≈ 562.9 K
        Assert.InRange(p.OutletStagnationT_K, 555.0, 570.0);
    }

    [Fact]
    public void Operate_AtUnityPressureRatio_LeavesStateUnchanged()
    {
        var map = ConstantEfficiencyCompressorMap.Default;
        var p = map.Operate(300.0, 50_000, pressureRatio: 1.0);
        Assert.Equal(300.0, p.OutletStagnationT_K, 6);
        Assert.Equal(50_000, p.OutletStagnationP_Pa, 0);
        Assert.Equal(0.0, p.SpecificWork_J_kg, 6);
    }

    [Fact]
    public void Operate_RejectsSubUnityPressureRatio()
    {
        var map = ConstantEfficiencyCompressorMap.Default;
        Assert.Throws<System.ArgumentOutOfRangeException>(() => map.Operate(288.15, 100_000, 0.5));
    }

    [Fact]
    public void HigherPressureRatio_GivesMoreSpecificWork()
    {
        var map = ConstantEfficiencyCompressorMap.Default;
        var low  = map.Operate(288.15, 100_000, 4.0);
        var high = map.Operate(288.15, 100_000, 16.0);
        Assert.True(high.SpecificWork_J_kg > low.SpecificWork_J_kg);
    }

    [Fact]
    public void LowerEfficiency_GivesMoreSpecificWork()
    {
        var loEff = new ConstantEfficiencyCompressorMap(0.70);
        var hiEff = new ConstantEfficiencyCompressorMap(0.95);
        var loP = loEff.Operate(288.15, 100_000, 8.0);
        var hiP = hiEff.Operate(288.15, 100_000, 8.0);
        Assert.True(loP.SpecificWork_J_kg > hiP.SpecificWork_J_kg,
            "Lower-η compressor needs more shaft work for the same pressure ratio");
    }
}
