// MarineDensityGuardRegressionTests.cs — regression guard for the seawater
// density sign-flip bug (red-team finding). WaterDensity_kgm3 is a linear fit
// valid only for T ∈ [270, 290 K]; far outside it goes ≤ 0, which would
// sign-flip buoyancy and drag and invert the HULL_BUOYANCY_NEGATIVE gate. The
// 900 kg/m³ floor keeps it physical. Fails on the unclamped fit, passes now.

using Xunit;

namespace Voxelforge.Marine.Tests;

public sealed class MarineDensityGuardRegressionTests
{
    [Fact]
    public void AbsurdTemperature_DensityStaysPositive()
    {
        var c = new MarineConditions(CruiseSpeed_ms: 2.0, MaxDepth_m: 100.0, WaterTemperature_K: 6000.0);
        Assert.True(c.WaterDensity_kgm3 > 0.0, $"density = {c.WaterDensity_kgm3} kg/m³");
        Assert.True(c.HydrostaticPressure_Pa > 0.0, $"hydrostatic P = {c.HydrostaticPressure_Pa} Pa");
    }

    [Fact]
    public void NominalConditions_DensityUnchangedInValidBand()
    {
        var c = new MarineConditions(CruiseSpeed_ms: 2.0, MaxDepth_m: 100.0); // 277.15 K, 35 ppt
        Assert.InRange(c.WaterDensity_kgm3, 1020.0, 1030.0);
    }
}
