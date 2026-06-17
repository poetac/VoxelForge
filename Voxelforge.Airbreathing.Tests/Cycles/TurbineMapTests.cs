// TurbineMapTests.cs — Sprint A7 acceptance for the parametric
// constant-η turbine map.

using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class TurbineMapTests
{
    [Fact]
    public void DefaultMap_HasMattinglyEfficiency()
    {
        Assert.Equal(0.90, ConstantEfficiencyTurbineMap.Default.IsentropicEfficiency, 6);
    }

    [Fact]
    public void Operate_DropsTemperaturePerEnergyBalance()
    {
        var map = ConstantEfficiencyTurbineMap.Default;
        // Required work 270 kJ/kg → ΔT = 270e3 / 1004.7 ≈ 268.7 K
        var p = map.Operate(inletStagnationT_K: 1175.0,
                            inletStagnationP_Pa: 750_000,
                            requiredSpecificWork_J_kg_total: 270_000);
        Assert.InRange(p.OutletStagnationT_K, 904.0, 909.0);
    }

    [Fact]
    public void Operate_DropsPressurePerIsentropicEfficiency()
    {
        var map = ConstantEfficiencyTurbineMap.Default;
        var p = map.Operate(1175.0, 750_000, 270_000);
        // Pressure drops well below the inlet — turbine extracts work.
        Assert.True(p.OutletStagnationP_Pa < 750_000);
        Assert.True(p.OutletStagnationP_Pa > 0);
    }

    [Fact]
    public void Operate_AtZeroWork_LeavesStateUnchanged()
    {
        var map = ConstantEfficiencyTurbineMap.Default;
        var p = map.Operate(1175.0, 750_000, 0.0);
        Assert.Equal(1175.0, p.OutletStagnationT_K, 6);
        Assert.Equal(750_000, p.OutletStagnationP_Pa, 0);
    }

    [Fact]
    public void Operate_TooMuchWork_Throws()
    {
        var map = ConstantEfficiencyTurbineMap.Default;
        // Asking for 10 MJ/kg from 1175 K gas — would require T_t5 ≪ 0.
        Assert.Throws<System.InvalidOperationException>(
            () => map.Operate(1175.0, 750_000, 10_000_000));
    }

    [Fact]
    public void LowerEfficiency_DropsOutletPressureMore()
    {
        var hi = new ConstantEfficiencyTurbineMap(0.95);
        var lo = new ConstantEfficiencyTurbineMap(0.70);
        var pHi = hi.Operate(1175.0, 750_000, 270_000);
        var pLo = lo.Operate(1175.0, 750_000, 270_000);
        // Lower-η turbine produces same shaft work but at the cost of
        // a larger pressure drop (more entropy generated).
        Assert.True(pLo.OutletStagnationP_Pa < pHi.OutletStagnationP_Pa);
    }
}
