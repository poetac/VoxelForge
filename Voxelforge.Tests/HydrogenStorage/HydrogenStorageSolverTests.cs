// HydrogenStorageSolverTests.cs — Sprint H2T.W1 unit tests for the
// closed-form hydrogen-storage tank performance snapshot.

using System;
using Voxelforge.HydrogenStorage;
using Xunit;

namespace Voxelforge.Tests.HydrogenStorage;

public sealed class HydrogenStorageSolverTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneKind()
    {
        var d = MiraiTank700bar() with { Kind = HydrogenStorageKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroVolume()
    {
        var d = MiraiTank700bar() with { InternalVolume_m3 = 0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_CompressedRejectsZeroPressure()
    {
        var d = MiraiTank700bar() with { OperatingPressure_bar = 0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNegativeHeatLeak()
    {
        var d = LH2Tank() with { HeatLeakRate_W = -1.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Mirai 700 bar Type-IV cluster ────────────────────────────────────

    [Fact]
    public void Mirai700bar_HydrogenDensityInClusterBand()
    {
        // ρ_H₂ at 700 bar, 298 K, Z ≈ 1.42 → ~ 40 kg/m³ (NIST cluster).
        var r = HydrogenStorageSolver.Solve(MiraiTank700bar());
        Assert.InRange(r.HydrogenDensity_kgm3, 38.0, 42.0);
    }

    [Fact]
    public void Mirai700bar_StoredMassInClusterBand()
    {
        // 122 L × ~ 40 kg/m³ → ~ 4.9 kg. Cluster band [4.0, 5.5] kg.
        var r = HydrogenStorageSolver.Solve(MiraiTank700bar());
        Assert.InRange(r.StoredHydrogenMass_kg, 4.0, 5.5);
    }

    [Fact]
    public void Mirai700bar_StoredEnergyInClusterBand()
    {
        // ~ 4.9 kg · 33.3 kWh/kg = ~ 163 kWh.
        var r = HydrogenStorageSolver.Solve(MiraiTank700bar());
        Assert.InRange(r.StoredHydrogenEnergy_kWh, 130.0, 185.0);
    }

    [Fact]
    public void Mirai700bar_GravimetricEfficiencyInClusterBand()
    {
        // ~ 4.9 / (4.9 + 95) = ~ 4.9 % — current Type-IV cluster
        // mid-band; below DOE 2025 6.5 % target.
        var r = HydrogenStorageSolver.Solve(MiraiTank700bar());
        Assert.InRange(r.GravimetricEfficiency, 0.04, 0.06);
    }

    [Fact]
    public void Mirai700bar_NoBoilOff()
    {
        // Compressed gas → zero boil-off (no phase change).
        var r = HydrogenStorageSolver.Solve(MiraiTank700bar());
        Assert.Equal(0.0, r.BoilOffRate_kgs, precision: 9);
    }

    [Fact]
    public void Compressed_DensityScalesRoughlyWithPressure()
    {
        // Doubling P from 350 to 700 bar should roughly double density
        // (modulo Z correction: Z(350)=1.21, Z(700)=1.42 → ratio ~ 1.71).
        var lo = HydrogenStorageSolver.Solve(MiraiTank700bar() with { OperatingPressure_bar = 350 });
        var hi = HydrogenStorageSolver.Solve(MiraiTank700bar());
        double ratio = hi.HydrogenDensity_kgm3 / lo.HydrogenDensity_kgm3;
        Assert.InRange(ratio, 1.5, 1.9);
    }

    // ── LH₂ cryogenic baseline ───────────────────────────────────────────

    [Fact]
    public void LH2_HydrogenDensityIsCanonicalValue()
    {
        // ρ_LH₂ = 70.85 kg/m³ exactly (NBP anchor).
        var r = HydrogenStorageSolver.Solve(LH2Tank());
        Assert.Equal(HydrogenStorageSolver.Lh2Density_kgm3, r.HydrogenDensity_kgm3, precision: 6);
    }

    [Fact]
    public void LH2_StoredMassExceedsCompressedGasAtSameVolume()
    {
        // LH₂ density (70.85 kg/m³) > 700-bar compressed (~ 40 kg/m³)
        // at the same tank volume.
        var compressed = HydrogenStorageSolver.Solve(MiraiTank700bar());
        var liquid     = HydrogenStorageSolver.Solve(LH2Tank());
        Assert.True(liquid.StoredHydrogenMass_kg > compressed.StoredHydrogenMass_kg,
            $"LH₂ stored mass ({liquid.StoredHydrogenMass_kg:F2} kg) expected > "
          + $"compressed ({compressed.StoredHydrogenMass_kg:F2} kg) at same V.");
    }

    [Fact]
    public void LH2_VolumetricEnergyDensityExceedsCompressed()
    {
        // LH₂ volumetric energy density ≈ 2.36 kWh/L vs 700-bar
        // compressed ≈ 1.33 kWh/L. Both well below DOE 2025 1.7 kWh/L
        // system-level target (which includes tank dry mass).
        var compressed = HydrogenStorageSolver.Solve(MiraiTank700bar());
        var liquid     = HydrogenStorageSolver.Solve(LH2Tank());
        Assert.True(liquid.VolumetricEnergyDensity_kWh_L
                  > compressed.VolumetricEnergyDensity_kWh_L);
    }

    [Fact]
    public void LH2_BoilOff_PositiveWhenHeatLeakPositive()
    {
        // Q_leak = 0.5 W → boil-off = 0.5 / 446_000 ≈ 1.12e-6 kg/s
        // ≈ 0.097 kg/day. About 1.1 % per day for the 8.6 kg tank —
        // typical NASA cryo cluster.
        var r = HydrogenStorageSolver.Solve(LH2Tank() with { HeatLeakRate_W = 0.5 });
        Assert.True(r.BoilOffRate_kgs > 0);
        Assert.InRange(r.BoilOffRate_kgs, 0.5e-6, 2.0e-6);
    }

    [Fact]
    public void LH2_BoilOff_ZeroWhenHeatLeakZero()
    {
        var r = HydrogenStorageSolver.Solve(LH2Tank() with { HeatLeakRate_W = 0.0 });
        Assert.Equal(0.0, r.BoilOffRate_kgs, precision: 9);
    }

    [Fact]
    public void LH2_BoilOff_LinearInHeatLeak()
    {
        var lo = HydrogenStorageSolver.Solve(LH2Tank() with { HeatLeakRate_W = 1.0 });
        var hi = HydrogenStorageSolver.Solve(LH2Tank() with { HeatLeakRate_W = 4.0 });
        Assert.Equal(4.0, hi.BoilOffRate_kgs / lo.BoilOffRate_kgs, precision: 6);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Toyota Mirai-class single 700 bar Type-IV composite tank.
    // Real Mirai uses 3 tanks totaling 142 L / 5.6 kg H₂; this is a
    // representative single-tank size.
    private static HydrogenStorageDesign MiraiTank700bar() => new(
        Kind:                    HydrogenStorageKind.CompressedGas,
        InternalVolume_m3:        0.122,         // 122 L
        OperatingPressure_bar:  700.0,
        OperatingTemperature_K: 298.15,
        DryMass_kg:              95.0);

    // Cryogenic LH₂ tank — small NASA / industrial spec.
    private static HydrogenStorageDesign LH2Tank() => new(
        Kind:                    HydrogenStorageKind.LiquidCryogenic,
        InternalVolume_m3:        0.122,
        OperatingPressure_bar:    1.0,
        OperatingTemperature_K:  20.3,
        DryMass_kg:              50.0,
        HeatLeakRate_W:           0.5);
}
