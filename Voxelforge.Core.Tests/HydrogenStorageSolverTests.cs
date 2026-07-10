// HydrogenStorageSolverTests — pins the closed-form hydrogen tank math:
// compressed-gas real-gas density ρ = P·M/(Z·R·T) with the linear H₂
// compressibility fit Z = 1 + 6e-4·P[bar], the fixed LH₂ / metal-hydride
// densities, LHV energy content, gravimetric / volumetric figures of merit,
// and cryo boil-off ṁ = Q/h_fg. Pure closed form → exact assertions.
// Backfills coverage onto the Linux 'core' CI leg.

using System;
using Voxelforge.HydrogenStorage;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class HydrogenStorageSolverTests
{
    // Mirai-class 700-bar Type-IV system: V = 122 L, 25 °C, 87 kg dry.
    private static HydrogenStorageDesign Mirai700Bar()
        => new(HydrogenStorageKind.CompressedGas,
               InternalVolume_m3: 0.122, OperatingPressure_bar: 700.0,
               OperatingTemperature_K: 298.15, DryMass_kg: 87.0);

    [Fact]
    public void Solve_CompressedGas_RealGasDensityAndDerivedFigures()
    {
        var r = HydrogenStorageSolver.Solve(Mirai700Bar());
        // Z = 1 + 6e-4·700 = 1.42; ρ = 7e7·2.016e-3/(1.42·8.31446·298.15).
        Assert.Equal(40.08956661910851, r.HydrogenDensity_kgm3, 9);
        Assert.Equal(4.8909271275312385, r.StoredHydrogenMass_kg, 9);     // ρ·V
        Assert.Equal(162.86787334679022, r.StoredHydrogenEnergy_kWh, 9);  // m·33.3
        Assert.Equal(0.053225354019372804, r.GravimetricEfficiency, 12);  // m/(m+87)
        Assert.Equal(1.3349825684163132, r.VolumetricEnergyDensity_kWh_L, 12);
        Assert.Equal(0.0, r.BoilOffRate_kgs, 12);   // no boil-off for gas storage
    }

    [Fact]
    public void Solve_LiquidCryogenic_FixedDensityAndHeatLeakBoilOff()
    {
        var r = HydrogenStorageSolver.Solve(new HydrogenStorageDesign(
            HydrogenStorageKind.LiquidCryogenic,
            InternalVolume_m3: 0.05, OperatingPressure_bar: 1.0,
            OperatingTemperature_K: 20.3, DryMass_kg: 30.0, HeatLeakRate_W: 5.0));
        Assert.Equal(70.85, r.HydrogenDensity_kgm3, 12);        // LH₂ at NBP
        Assert.Equal(3.5425, r.StoredHydrogenMass_kg, 12);      // 70.85·0.05
        Assert.Equal(117.96524999999998, r.StoredHydrogenEnergy_kWh, 9);
        // ṁ_boil-off = Q/h_fg = 5/446 000 ≈ 1.121e-5 kg/s (≈ 0.97 kg/day).
        Assert.Equal(5.0 / 446_000.0, r.BoilOffRate_kgs, 15);
    }

    [Fact]
    public void Solve_MetalHydride_FixedEffectiveDensity_NoBoilOff()
    {
        var r = HydrogenStorageSolver.Solve(new HydrogenStorageDesign(
            HydrogenStorageKind.MetalHydride,
            InternalVolume_m3: 0.01, OperatingPressure_bar: 10.0,
            OperatingTemperature_K: 298.15, DryMass_kg: 99.0));
        Assert.Equal(100.0, r.HydrogenDensity_kgm3, 12);   // lattice-locked ρ_eff
        Assert.Equal(1.0, r.StoredHydrogenMass_kg, 12);
        // Gravimetric: bulk metal dominates → 1/(1+99) = 1 % exactly.
        Assert.Equal(0.01, r.GravimetricEfficiency, 12);
        Assert.Equal(0.0, r.BoilOffRate_kgs, 12);
    }

    [Fact]
    public void Solve_CompressedDensityGrowsSubLinearlyWithPressure()
    {
        // Real-gas Z > 1 penalises high P: doubling 350 → 700 bar must yield
        // LESS than double the density (Z rises 1.21 → 1.42).
        var r350 = HydrogenStorageSolver.Solve(Mirai700Bar() with { OperatingPressure_bar = 350.0 });
        var r700 = HydrogenStorageSolver.Solve(Mirai700Bar());
        Assert.True(r700.HydrogenDensity_kgm3 < 2.0 * r350.HydrogenDensity_kgm3,
            $"Real-gas density must be sub-linear in P; got {r350.HydrogenDensity_kgm3} → {r700.HydrogenDensity_kgm3}");
        Assert.True(r700.HydrogenDensity_kgm3 > r350.HydrogenDensity_kgm3);
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() => HydrogenStorageSolver.Solve(
            Mirai700Bar() with { OperatingPressure_bar = 0.0 }));   // gas needs P > 0
        Assert.Throws<ArgumentException>(() => HydrogenStorageSolver.Solve(
            Mirai700Bar() with { InternalVolume_m3 = 0.0 }));
        Assert.Throws<ArgumentException>(() => HydrogenStorageSolver.Solve(
            Mirai700Bar() with { Kind = HydrogenStorageKind.None }));
        Assert.Throws<ArgumentException>(() => HydrogenStorageSolver.Solve(
            Mirai700Bar() with { HeatLeakRate_W = -1.0 }));
    }
}
