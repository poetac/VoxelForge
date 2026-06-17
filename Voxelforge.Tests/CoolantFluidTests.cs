// CoolantFluidTests.cs — Contract tests for every registered coolant
// fluid. Ensures methane remains identical to its pre-refactor behaviour,
// and that the new hydrogen / RP-1 modules return sane numbers in the
// service band.

using Voxelforge.Coolant;

namespace Voxelforge.Tests;

public class CoolantFluidTests
{
    [Fact]
    public void Methane_State_MatchesLegacyCall()
    {
        var legacy = CoolantProperties.Methane(300, 10e6);
        var viaFluid = MethaneFluid.Instance.GetState(300, 10e6);
        Assert.Equal(legacy.Density_kgm3, viaFluid.Density_kgm3, precision: 2);
        Assert.Equal(legacy.Cp_Jkg, viaFluid.Cp_Jkg, precision: 2);
        Assert.Equal(legacy.Viscosity_PaS, viaFluid.Viscosity_PaS, precision: 10);
        Assert.Equal(legacy.Conductivity_WmK, viaFluid.Conductivity_WmK, precision: 4);
    }

    [Fact]
    public void Methane_EnthalpyInverse_RoundTrips()
    {
        var s = MethaneFluid.Instance.GetState(400, 10e6);
        double T = MethaneFluid.Instance.TemperatureFromEnthalpy(s.Enthalpy_Jkg);
        Assert.InRange(T, 399, 401);
    }

    [Fact]
    public void Methane_PseudocriticalFlag_HitsNearTpc()
    {
        Assert.True(MethaneFluid.Instance.IsInPseudocriticalRegion(210, 10e6));
        Assert.False(MethaneFluid.Instance.IsInPseudocriticalRegion(500, 10e6));
        Assert.False(MethaneFluid.Instance.IsInPseudocriticalRegion(210, 3e6));   // P < P_crit
    }

    [Fact]
    public void Hydrogen_HasHighCp_AndLowViscosity()
    {
        var s = HydrogenFluid.Instance.GetState(300, 10e6);
        Assert.InRange(s.Cp_Jkg, 12000, 16000);
        Assert.InRange(s.Viscosity_PaS, 5e-6, 20e-6);
        Assert.InRange(s.Density_kgm3, 3, 15);
        Assert.InRange(s.Conductivity_WmK, 0.15, 0.35);
    }

    [Fact]
    public void Hydrogen_EnthalpyMonotonicInT()
    {
        var lo = HydrogenFluid.Instance.GetState(100, 10e6);
        var hi = HydrogenFluid.Instance.GetState(500, 10e6);
        Assert.True(hi.Enthalpy_Jkg > lo.Enthalpy_Jkg);
    }

    [Fact]
    public void RP1_IsLiquidLike_At298K()
    {
        var s = RP1Fluid.Instance.GetState(298, 10e6);
        Assert.InRange(s.Density_kgm3, 770, 860);
        Assert.InRange(s.Viscosity_PaS, 1e-3, 3e-3);   // 1000–3000 μPa·s
        Assert.InRange(s.Cp_Jkg, 1800, 2200);
    }

    [Fact]
    public void RP1_NotInPseudocriticalRegionAtAnyChamberP()
    {
        Assert.False(RP1Fluid.Instance.IsInPseudocriticalRegion(298, 5e6));
        Assert.False(RP1Fluid.Instance.IsInPseudocriticalRegion(500, 15e6));
    }

    [Fact]
    public void Registry_LooksUpAllKnownKeys()
    {
        Assert.Same(MethaneFluid.Instance, CoolantRegistry.Get("CH4"));
        Assert.Same(HydrogenFluid.Instance, CoolantRegistry.Get("H2"));
        Assert.Same(RP1Fluid.Instance, CoolantRegistry.Get("RP-1"));
        // Unknown keys throw instead of silently falling back to methane.
        // Prevents optimising under a different physics model than intended.
        // The hard-fail behaviour is also locked in PropellantValidationTests.
        Assert.Throws<InvalidOperationException>(() => CoolantRegistry.Get("unknown"));
    }

    [Fact]
    public void Registry_IsKnown_DiscriminatesRealFromFallback()
    {
        Assert.True(CoolantRegistry.IsKnown("CH4"));
        Assert.True(CoolantRegistry.IsKnown("H2"));
        Assert.False(CoolantRegistry.IsKnown("MMH"));
    }

    [Fact]
    public void AllFluids_DeclareMetadata()
    {
        foreach (var f in CoolantRegistry.All)
        {
            Assert.False(string.IsNullOrEmpty(f.Metadata.Key));
            Assert.True(f.Metadata.CriticalT_K > 0);
            Assert.True(f.Metadata.MaxBulkT_K > 100);
            Assert.False(string.IsNullOrEmpty(f.Metadata.ServiceLimitNote));
        }
    }
}
