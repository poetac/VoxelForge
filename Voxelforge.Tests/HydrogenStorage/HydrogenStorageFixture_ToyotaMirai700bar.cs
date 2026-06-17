// HydrogenStorageFixture_ToyotaMirai700bar.cs — Sprint B.19 published-
// product validation fixture for the 700-bar compressed-gas H₂ storage
// path through the HydrogenStorage pillar.
//
// Anchors the model to the **Toyota Mirai 2nd-generation** on-board
// hydrogen storage system, the canonical commercial 700-bar Type-IV
// composite-tank deployment (~ 12 000 vehicles shipped through 2024).
// Public anchor (Toyota Mirai 2nd-gen service manual + SAE 2020-01-
// 0855 + DOE EERE Fuel Cell Technologies Office annual reports):
//   - 3 × Type-IV carbon-fiber-wrapped composite tanks
//   - Total internal volume ~ 142 L = 0.142 m³
//   - Total dry mass (composite + liner + valves + bracket) ~ 87 kg
//   - Storage pressure: 700 bar nominal (875 bar burst rated)
//   - Storage temperature: 25 °C nominal (298.15 K)
//   - Total H₂ capacity: ~ 5.6 kg published rating
//
// Phase-3 coverage backfill — twelfth second-anchor sprint in the
// framing-B Phase 3 pattern after B.3 through B.18. The pillar's
// Wave-1 solver explicitly cites this anchor in its header. First
// fixture to exercise the CompressedGas storage path (vs cryo or
// metal-hydride).
//
// DOE 2025 hydrogen-storage technical targets (for context):
//   - Gravimetric efficiency ≥ 6.5 wt%
//   - Volumetric energy density ≥ 1.7 kWh/L
// Toyota Mirai approaches but does not quite meet these targets;
// fixture asserts the published Toyota performance, not the DOE target.

using Voxelforge.HydrogenStorage;
using Xunit;

namespace Voxelforge.Tests.HydrogenStorage;

public sealed class HydrogenStorageFixture_ToyotaMirai700bar
{
    // ── Nameplate at storage conditions ──────────────────────────────

    [Fact]
    public void Mirai_AtStorageConditions_HydrogenDensityMatchesRealGas()
    {
        // Real-gas density: ρ = P · M / (Z · R · T) with Z = 1 + 6e-4·P.
        // At P = 700 bar = 7e7 Pa, T = 298.15 K, Z = 1.42:
        //   ρ = 7e7 × 2.016e-3 / (1.42 × 8.31446 × 298.15) = 40.06 kg/m³.
        // NIST cluster mid-band at 700 bar / 25 °C: 39-42 kg/m³.
        var r = HydrogenStorageSolver.Solve(Mirai700barClass());
        Assert.InRange(r.HydrogenDensity_kgm3, 35.0, 45.0);
    }

    [Fact]
    public void Mirai_AtStorageConditions_StoredMassMatchesNameplate()
    {
        // Toyota Mirai 2nd-gen nameplate: ~ 5.6 kg H₂ across 3 tanks.
        // Model: 40.06 × 0.142 = 5.69 kg. Cluster band [4.5, 7.0] kg —
        // accommodates fill-pressure scatter (700 bar is the nominal
        // service pressure but tanks reach this only after temperature-
        // compensated fast-fill).
        var r = HydrogenStorageSolver.Solve(Mirai700barClass());
        Assert.InRange(r.StoredHydrogenMass_kg, 4.5, 7.0);
    }

    [Fact]
    public void Mirai_AtStorageConditions_StoredEnergyInVehicleRangeBand()
    {
        // E = m_H₂ × LHV_H₂ = 5.69 × 33.3 = 189.5 kWh.
        // Mirai EPA range ~ 400 miles → ~ 0.47 kWh/mile system-level
        // efficiency including 60 %-LHV fuel-cell + drivetrain.
        var r = HydrogenStorageSolver.Solve(Mirai700barClass());
        Assert.InRange(r.StoredHydrogenEnergy_kWh, 150.0, 230.0);
    }

    [Fact]
    public void Mirai_AtStorageConditions_GravimetricEfficiencyInCurrentTechBand()
    {
        // η_grav = m_H₂ / (m_H₂ + m_dry) = 5.69 / 92.69 = 6.14 %.
        // DOE 2025 target ≥ 6.5 %; current Type-IV systems cluster
        // 5-7 % depending on tank-design generation. Mirai 2nd-gen
        // sits at the higher end of this band.
        var r = HydrogenStorageSolver.Solve(Mirai700barClass());
        Assert.InRange(r.GravimetricEfficiency, 0.04, 0.08);
    }

    [Fact]
    public void Mirai_AtStorageConditions_VolumetricEnergyDensityBelowDoeTarget()
    {
        // Volumetric = E_stored / V = 189.5 kWh / 142 L = 1.33 kWh/L.
        // DOE 2025 target ≥ 1.7 kWh/L — 700-bar gas does NOT meet
        // this target (it's why LH₂ + metal hydrides remain on the
        // research roadmap). Fixture validates Toyota performance,
        // not DOE target.
        var r = HydrogenStorageSolver.Solve(Mirai700barClass());
        Assert.InRange(r.VolumetricEnergyDensity_kWh_L, 1.0, 1.6);
    }

    [Fact]
    public void Mirai_AtStorageConditions_NoBoilOffForCompressedGas()
    {
        // CompressedGas mode has zero boil-off (HeatLeakRate ignored).
        // Only LiquidCryogenic mode produces continuous boil-off.
        var r = HydrogenStorageSolver.Solve(Mirai700barClass());
        Assert.Equal(0.0, r.BoilOffRate_kgs, precision: 9);
    }

    // ── Compressibility-factor physics (Sprint H2T.W1 / W2 anchor) ────

    [Fact]
    public void Mirai_AtStorageConditions_RealGasDensityBelowIdealGas()
    {
        // Z > 1 at high P means ρ_real < ρ_ideal (compressed less
        // than ideal-gas would predict). At Z = 1.42 the real-gas
        // density should be ~ 70 % of ideal-gas density.
        var r = HydrogenStorageSolver.Solve(Mirai700barClass());
        const double P_Pa = 700e5;
        const double T_K  = 298.15;
        double rho_ideal = P_Pa * HydrogenStorageSolver.MolarMassH2_kg_mol
                         / (HydrogenStorageSolver.R_J_molK * T_K);
        Assert.True(r.HydrogenDensity_kgm3 < rho_ideal,
            $"Real-gas density ({r.HydrogenDensity_kgm3:F2}) must be < "
          + $"ideal-gas density ({rho_ideal:F2}) at 700 bar / 25 °C.");
        // Ratio sanity: ρ_real / ρ_ideal = 1/Z = 1/1.42 ≈ 0.704.
        double ratio = r.HydrogenDensity_kgm3 / rho_ideal;
        Assert.InRange(ratio, 0.65, 0.75);
    }

    [Fact]
    public void Mirai_AtLowerPressure_DensityScalesNearlyLinearly()
    {
        // At lower P the Z correction is smaller, so density scales
        // closer to linear-in-P. At 350 bar (half), Z ≈ 1.21 (vs 1.42
        // at 700) → ρ at 350 bar ≈ 0.5 × ρ at 700 × (1.42/1.21) = 0.587
        // → ratio 0.59 (not 0.50 ideal-gas).
        var nominal = HydrogenStorageSolver.Solve(Mirai700barClass());
        var halfP = HydrogenStorageSolver.Solve(Mirai700barClass()
            with { OperatingPressure_bar = 350.0 });
        double ratio = halfP.HydrogenDensity_kgm3 / nominal.HydrogenDensity_kgm3;
        // Cluster band [0.50, 0.65] — real-gas effect makes the ratio
        // exceed the 0.50 ideal-gas value.
        Assert.InRange(ratio, 0.50, 0.65);
    }

    // ── Mode + topology validation ────────────────────────────────────

    [Fact]
    public void Mirai_UsesCompressedGasMode()
    {
        // Mirai 2nd-gen uses ambient-temperature high-pressure gas (vs
        // cryo or metal-hydride). The fixture must select
        // CompressedGas.
        Assert.Equal(HydrogenStorageKind.CompressedGas, Mirai700barClass().Kind);
    }

    [Fact]
    public void Mirai_UsesAmbientTemperature()
    {
        // 700 bar Type-IV tanks operate at ambient (298.15 K = 25 °C),
        // unlike LH₂ at 20.3 K.
        Assert.Equal(298.15, Mirai700barClass().OperatingTemperature_K, precision: 3);
    }

    // ── Cross-mode comparison ─────────────────────────────────────────

    [Fact]
    public void Mirai_AsLh2Cryogenic_StoresMoreMassPerVolume()
    {
        // Same tank volume + dry mass, but in LiquidCryogenic mode:
        // ρ_LH₂ = 70.85 kg/m³ (always) > ρ_gas_700bar = 40 kg/m³.
        // LH₂ stores more mass per volume than 700-bar gas — that's
        // why cryogenic storage remains a research target despite the
        // boil-off + insulation complexity.
        var gas = HydrogenStorageSolver.Solve(Mirai700barClass());
        var lh2 = HydrogenStorageSolver.Solve(Mirai700barClass()
            with { Kind = HydrogenStorageKind.LiquidCryogenic,
                   OperatingTemperature_K = 20.3 });
        Assert.True(lh2.StoredHydrogenMass_kg > gas.StoredHydrogenMass_kg,
            $"LH₂ mass ({lh2.StoredHydrogenMass_kg:F2}) must exceed "
          + $"700-bar gas mass ({gas.StoredHydrogenMass_kg:F2}) for the "
          + "same tank volume.");
        // Volumetric energy density also higher.
        Assert.True(lh2.VolumetricEnergyDensity_kWh_L
                  > gas.VolumetricEnergyDensity_kWh_L,
            $"LH₂ volumetric ({lh2.VolumetricEnergyDensity_kWh_L:F2}) > "
          + $"gas ({gas.VolumetricEnergyDensity_kWh_L:F2}) kWh/L.");
    }

    [Fact]
    public void Mirai_AsMetalHydride_StoresMostMassPerVolume()
    {
        // Same tank volume, but in MetalHydride mode: effective ρ = 100
        // kg/m³ (chemisorption locks H₂ at higher density than LH₂).
        // Metal-hydride stores the most mass per volume of all three
        // modes — but at the cost of mass (the metal lattice is heavy).
        var gas = HydrogenStorageSolver.Solve(Mirai700barClass());
        var mh = HydrogenStorageSolver.Solve(Mirai700barClass()
            with { Kind = HydrogenStorageKind.MetalHydride });
        Assert.True(mh.StoredHydrogenMass_kg > gas.StoredHydrogenMass_kg);
        // MH > LH₂ in volumetric density.
        Assert.InRange(mh.HydrogenDensity_kgm3, 95.0, 105.0);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    // Toyota Mirai 2nd-generation H₂ storage system. Public anchors:
    //   3 × Type-IV 700-bar composite tanks
    //   Total internal volume ~ 142 L
    //   Total dry mass (tanks + valves + brackets) ~ 87 kg
    //   25 °C storage temperature
    //   ~ 5.6 kg H₂ published capacity
    private static HydrogenStorageDesign Mirai700barClass() => new(
        Kind:                   HydrogenStorageKind.CompressedGas,
        InternalVolume_m3:      0.142,
        OperatingPressure_bar:  700.0,
        OperatingTemperature_K: 298.15,
        DryMass_kg:             87.0,
        HeatLeakRate_W:         0.0);
}
