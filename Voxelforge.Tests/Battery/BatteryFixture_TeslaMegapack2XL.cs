// BatteryFixture_TeslaMegapack2XL.cs — Sprint B.9 published-product
// validation fixture for the LFP utility-scale path through the Battery
// pillar.
//
// Anchors the model to **Tesla Megapack 2 XL**, the company's utility-
// scale grid-storage product. Public datasheet
// (https://www.tesla.com/megapack):
//   - 3.916 MWh DC energy capacity per unit
//   - 1.5 MW peak DC power, ~ 0.98 MW continuous at 4-hour duration
//   - 1500 V DC nominal pack architecture
//   - LFP (LiFePO₄) chemistry
//
// Second anchor for the Battery pillar (Wave-1 anchor is the Tesla
// Model 3 Long Range NMC pack). Closes part of the Phase-3 coverage
// backfill — validates the LFP chemistry
// path + the high-voltage (1500 V) architecture + the utility-scale
// (4416 → 250k cells) topology against a publicly-cited commercial
// product.
//
// Cell-level topology is normalised to the 5 Ah BatteryChemistryRegistry
// cluster (not the 280 Ah CATL prismatic cells that Megapack ACTUALLY
// uses) — the registry chose 5 Ah cluster mid-band per ADR-026 to keep
// the cluster fits portable across battery scales. Tests use cluster
// bands wide enough to accommodate this normalisation while still
// pinning to Megapack 2 XL nameplate numbers.

using Voxelforge.Battery;
using Xunit;

namespace Voxelforge.Tests.Battery;

public sealed class BatteryFixture_TeslaMegapack2XL
{
    // ── Nameplate energy capacity ─────────────────────────────────────

    [Fact]
    public void Megapack2XL_FullChargeEnergyCapacity_MatchesNameplate()
    {
        // Tesla Megapack 2 XL nameplate: 3.916 MWh DC at 100 % SoC.
        // Cluster band [3.5, 4.3] MWh — accommodates cluster-level
        // OCV anchor scatter ± 5 % on capacity.
        var r = BatteryPackSolver.Solve(TeslaMegapack2XL() with { StateOfCharge = 1.0 });
        double mWh = r.PackEnergyStored_Wh / 1.0e6;
        Assert.InRange(mWh, 3.5, 4.3);
    }

    [Fact]
    public void Megapack2XL_HalfChargeEnergyIsHalfOfNameplate()
    {
        // Stored energy at 50 % SoC should be ~ half of full-pack
        // nameplate. The OCV(SoC) integral is quadratic, so the exact
        // ratio is ((Vmin·0.5 + 0.5·ΔV·0.25) / (Vmin·1 + 0.5·ΔV·1))
        // for LFP: (1.25 + 0.144) / 3.075 ≈ 0.453, not 0.500.
        // Cluster band [0.40, 0.55] of nameplate.
        var full = BatteryPackSolver.Solve(TeslaMegapack2XL() with { StateOfCharge = 1.0 });
        var half = BatteryPackSolver.Solve(TeslaMegapack2XL() with { StateOfCharge = 0.5 });
        double ratio = half.PackEnergyStored_Wh / full.PackEnergyStored_Wh;
        Assert.InRange(ratio, 0.40, 0.55);
    }

    // ── 1500 V architecture ───────────────────────────────────────────

    [Fact]
    public void Megapack2XL_PackVoltageAtMidpoint_MatchesArchitecture()
    {
        // Tesla Megapack 2 XL is a 1500 V DC nominal architecture
        // (public datasheet). At 50 % SoC the OCV-driven pack voltage
        // should land near 1500 V. Cluster band [1400, 1600] V.
        var r = BatteryPackSolver.Solve(TeslaMegapack2XL() with { StateOfCharge = 0.5 });
        Assert.InRange(r.PackOpenCircuitVoltage_V, 1400.0, 1600.0);
    }

    [Fact]
    public void Megapack2XL_FullChargePackVoltage_DoesNotExceedLfpCellLimit()
    {
        // LFP per-cell V_max = 3.65 V → pack max = 488 × 3.65 = 1781 V.
        // At 100 % SoC the open-circuit pack voltage must not exceed
        // the per-cell × series-count product (Vmax = 3.65 for LFP).
        var r = BatteryPackSolver.Solve(TeslaMegapack2XL() with { StateOfCharge = 1.0 });
        Assert.True(r.PackOpenCircuitVoltage_V <= 488 * 3.65 + 1e-6,
            $"V_pack_oc ({r.PackOpenCircuitVoltage_V:F1} V) must not exceed "
          + $"488 × 3.65 = 1781.2 V (LFP per-cell V_max × series count).");
    }

    // ── Continuous-discharge performance at 4-hour duration ───────────

    [Fact]
    public void Megapack2XL_ContinuousDischarge_LandsNearOneMegawatt()
    {
        // 4-hour duration at 3.916 MWh → continuous P ≈ 0.98 MW.
        // At 50 % SoC with I_pack = 650 A through ~ 1488 V loaded
        // → P ≈ 967 kW. Cluster band [800, 1200] kW.
        var r = BatteryPackSolver.Solve(TeslaMegapack2XL() with { StateOfCharge = 0.5 });
        double kW = r.PackElectricalPower_W / 1.0e3;
        Assert.InRange(kW, 800.0, 1200.0);
    }

    [Fact]
    public void Megapack2XL_ContinuousDischarge_HeatGenerationManageable()
    {
        // At I_pack = 650 A through R_pack ≈ 0.019 Ω → Q_heat ≈ 8 kW.
        // Sanity check that resistive heat is < 5 % of electrical
        // output (utility-cooling envelope). For a 1-MW unit, heat
        // generation in [1, 20] kW is the operating range.
        var r = BatteryPackSolver.Solve(TeslaMegapack2XL() with { StateOfCharge = 0.5 });
        Assert.InRange(r.PackHeatGeneration_W, 1.0e3, 20.0e3);
        Assert.True(r.PackHeatGeneration_W < 0.05 * r.PackElectricalPower_W,
            $"Heat generation ({r.PackHeatGeneration_W:F0} W) should be < 5 % "
          + $"of electrical output ({r.PackElectricalPower_W:F0} W) at continuous duty.");
    }

    [Fact]
    public void Megapack2XL_PackInternalResistance_InUtilityScaleBand()
    {
        // For an LFP pack with R_cell = 0.020 Ω, N_series = 488,
        // N_parallel = 520 → R_pack = 0.0188 Ω. Cluster band [10, 30] mΩ
        // for utility-scale packs (high parallel count keeps R_pack
        // low despite the long series string).
        var r = BatteryPackSolver.Solve(TeslaMegapack2XL());
        double mOhm = r.PackInternalResistance_Ohm * 1000.0;
        Assert.InRange(mOhm, 10.0, 30.0);
    }

    // ── LFP chemistry-specific validation ─────────────────────────────

    [Fact]
    public void Megapack2XL_UsesLfpChemistry()
    {
        // Public datasheet confirms LFP — the fixture must instantiate
        // the LithiumIronPhosphate kind, not NMC.
        Assert.Equal(BatteryChemistry.LithiumIronPhosphate,
                     TeslaMegapack2XL().Chemistry);
    }

    [Fact]
    public void Megapack2XL_CellOcvSpan_MatchesLfpRegistry()
    {
        // Per-cell OCV bounds at 0 % vs 100 % SoC should match the
        // LFP cluster registry (2.5 - 3.65 V), independent of the
        // pack topology.
        var lo = BatteryPackSolver.Solve(TeslaMegapack2XL() with { StateOfCharge = 0.0 });
        var hi = BatteryPackSolver.Solve(TeslaMegapack2XL() with { StateOfCharge = 1.0 });
        Assert.Equal(2.5,  lo.OpenCircuitCellVoltage_V, precision: 6);
        Assert.Equal(3.65, hi.OpenCircuitCellVoltage_V, precision: 6);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    // Tesla Megapack 2 XL — utility-scale grid-storage anchor.
    // Topology (488 × 520 cells, 5 Ah/cell normalised to cluster):
    //   N_series = 488 → V_pack_oc ≈ 1500 V at 50 % SoC LFP midpoint
    //   N_parallel = 520 → E_stored ≈ 3.9 MWh at 100 % SoC
    //   I_pack = 650 A → P ≈ 0.97 MW continuous discharge at 4-h duration
    //
    // Real Megapack 2 XL uses ~ 280 Ah CATL prismatic LFP cells in a
    // shorter series × wider parallel topology; the model normalises
    // to the 5 Ah cluster cell per ADR-026.
    private static BatteryPackDesign TeslaMegapack2XL() => new(
        Chemistry:       BatteryChemistry.LithiumIronPhosphate,
        CellsInSeries:   488,
        ParallelStrings: 520,
        StateOfCharge:   0.5,
        LoadCurrent_A:   650.0);
}
