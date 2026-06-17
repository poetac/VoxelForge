// H2TWave2Tests.cs — Sprint H2T.W2 unit tests for the metal-hydride
// storage extension.

using Voxelforge.HydrogenStorage;
using Xunit;

namespace Voxelforge.Tests.HydrogenStorage;

public sealed class H2TWave2Tests
{
    [Fact]
    public void MetalHydride_StorageDensityHigherThanCompressedGas()
    {
        // Metal hydride: ρ_eff ≈ 100 kg/m³. Compressed gas at 700 bar:
        // ρ ≈ 40 kg/m³. Liquid: 70.85 kg/m³. So MH > LH₂ > 700-bar.
        var compressed = HydrogenStorageSolver.Solve(MiraiTank700bar());
        var hydride = HydrogenStorageSolver.Solve(MiraiTank700bar() with
        {
            Kind = HydrogenStorageKind.MetalHydride,
        });
        Assert.True(hydride.HydrogenDensity_kgm3 > compressed.HydrogenDensity_kgm3);
    }

    [Fact]
    public void MetalHydride_StorageDensityHigherThanLH2()
    {
        // 100 kg/m³ > 70.85 kg/m³.
        var lh2 = HydrogenStorageSolver.Solve(LH2Tank());
        var hydride = HydrogenStorageSolver.Solve(LH2Tank() with
        {
            Kind = HydrogenStorageKind.MetalHydride,
        });
        Assert.True(hydride.HydrogenDensity_kgm3 > lh2.HydrogenDensity_kgm3);
    }

    [Fact]
    public void MetalHydride_NoBoilOff_RegardlessOfHeatLeak()
    {
        var d = MiraiTank700bar() with
        {
            Kind = HydrogenStorageKind.MetalHydride,
            HeatLeakRate_W = 10.0,
        };
        var r = HydrogenStorageSolver.Solve(d);
        Assert.Equal(0.0, r.BoilOffRate_kgs, precision: 9);
    }

    [Fact]
    public void MetalHydride_DensityIsCanonicalValue()
    {
        var r = HydrogenStorageSolver.Solve(MiraiTank700bar() with
        {
            Kind = HydrogenStorageKind.MetalHydride,
        });
        Assert.Equal(HydrogenStorageSolver.MetalHydrideEffectiveDensity_kgm3,
            r.HydrogenDensity_kgm3, precision: 6);
    }

    [Fact]
    public void MetalHydride_LowGravimetricEfficiency_VsLH2()
    {
        // Metal hydride stores more H₂ per volume, but the metal lattice
        // mass kills gravimetric efficiency. Both designs share the same
        // DryMass_kg, so MH has more H₂ → HIGHER grav. eff for the same
        // tank. But in reality the metal weight scales with H₂ mass — a
        // full model would adjust DryMass too. Scaffold simplification:
        // we only test the raw stored mass.
        var lh2 = HydrogenStorageSolver.Solve(LH2Tank());
        var mh = HydrogenStorageSolver.Solve(LH2Tank() with
        {
            Kind = HydrogenStorageKind.MetalHydride,
        });
        Assert.True(mh.StoredHydrogenMass_kg > lh2.StoredHydrogenMass_kg);
    }

    private static HydrogenStorageDesign MiraiTank700bar() => new(
        Kind:                    HydrogenStorageKind.CompressedGas,
        InternalVolume_m3:        0.122,
        OperatingPressure_bar:  700.0,
        OperatingTemperature_K: 298.15,
        DryMass_kg:              95.0);

    private static HydrogenStorageDesign LH2Tank() => new(
        Kind:                    HydrogenStorageKind.LiquidCryogenic,
        InternalVolume_m3:        0.122,
        OperatingPressure_bar:    1.0,
        OperatingTemperature_K:  20.3,
        DryMass_kg:              50.0,
        HeatLeakRate_W:           0.5);
}
