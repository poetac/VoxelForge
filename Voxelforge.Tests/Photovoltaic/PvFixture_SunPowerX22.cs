// PvFixture_SunPowerX22.cs — Sprint B.10 published-product validation
// fixture for the monocrystalline silicon path through the Photovoltaic
// pillar.
//
// Anchors the model to **SunPower X-Series X22-360**, a 96-cell
// monocrystalline back-contact panel widely deployed in residential +
// commercial PV from ~ 2015 onward. Public datasheet
// (https://www.sunpower.com/sites/default/files/x-series-residential-solar-panels-x22-360-ds-en-mc4.pdf):
//   - 360 W STC nameplate (G = 1000 W/m², T_cell = 25 °C)
//   - 96 series-connected mono-Si back-contact cells
//   - 1.559 m × 1.046 m = 1.631 m² aperture
//   - 22.1 % rated efficiency
//   - V_oc = 69.5 V, I_sc = 6.49 A, V_mp = 57.3 V, I_mp = 6.28 A
//   - α_I (temp coeff of I_sc) = +0.05 %/K
//   - β_V (temp coeff of V_oc) = -176.6 mV/K (-0.27 %/K of V_oc)
//   - β_P (temp coeff of P_mp) = -0.30 %/K
//
// Phase-3 coverage backfill (companion to Sprint B.9 Tesla Megapack 2 XL
// fixture on the Battery pillar). Validates the monocrystalline cluster
// pathway against a publicly-cited commercial product across STC,
// NOCT-class temperature, and low-irradiance operating points.
//
// Cluster vs SunPower-specific scatter:
//   The PV.W1 `Monocrystalline` cluster (I_sc=6.20 A, V_oc=0.68 V/cell)
//   is anchored to the Markvart & Castañer 2003 generic-silicon mid-
//   band, not to the SunPower X-series specifically. SunPower X22 sits
//   on the premium end of the cluster (~ 22 % vs 18-19 % typical mono).
//   Test bands are sized to accommodate this — pinning to nameplate
//   ± 15 % rather than ± 5 % per-product band.

using Voxelforge.Photovoltaic;
using Xunit;

namespace Voxelforge.Tests.Photovoltaic;

public sealed class PvFixture_SunPowerX22
{
    // ── Nameplate at STC ──────────────────────────────────────────────

    [Fact]
    public void SunPowerX22_AtStc_MaxPowerInClusterBand()
    {
        // X22-360 nameplate: 360 W at STC. Cluster band [280, 400] W
        // — accommodates cluster-vs-premium-class scatter.
        var r = PvPanelSolver.Solve(SunPowerX22Class());
        Assert.InRange(r.MaxPower_W, 280.0, 400.0);
    }

    [Fact]
    public void SunPowerX22_AtStc_OpenCircuitVoltageInClusterBand()
    {
        // X22-360 datasheet: V_oc = 69.5 V at STC for the full panel.
        // Cluster band: 96 × 0.68 = 65.3 V (cluster mid) ± ~ 8 V
        // for premium-class scatter → [60, 75] V.
        var r = PvPanelSolver.Solve(SunPowerX22Class());
        Assert.InRange(r.OpenCircuitVoltage_V, 60.0, 75.0);
    }

    [Fact]
    public void SunPowerX22_AtStc_ShortCircuitCurrentInClusterBand()
    {
        // X22-360 datasheet: I_sc = 6.49 A. Cluster mid (6.20 A) within
        // ± 0.5 A of nameplate. Band [5.5, 7.0] A.
        var r = PvPanelSolver.Solve(SunPowerX22Class());
        Assert.InRange(r.ShortCircuitCurrent_A, 5.5, 7.0);
    }

    [Fact]
    public void SunPowerX22_AtStc_EfficiencyInPremiumMonoBand()
    {
        // Premium mono-Si cluster: 19-23 % efficiency at STC. The
        // cluster-fit model lands ~ 22 % for a 96-cell × 153 cm² panel
        // (1.469 m²), matching X22-360's 22.1 % nameplate.
        var r = PvPanelSolver.Solve(SunPowerX22Class());
        Assert.InRange(r.ConversionEfficiency, 0.19, 0.23);
    }

    [Fact]
    public void SunPowerX22_AtStc_IncidentPowerMatchesPanelArea()
    {
        // At G_STC = 1000 W/m², incident power = panel area in W.
        // 96 × 153 cm² = 1.469 m² → 1469 W incident.
        var r = PvPanelSolver.Solve(SunPowerX22Class());
        Assert.Equal(1000.0 * (96 * 153.0 * 1e-4), r.IncidentSolarPower_W, precision: 3);
    }

    // ── Temperature derating (NOCT-class operating point) ────────────

    [Fact]
    public void SunPowerX22_AtNoctTemperature_PowerDropsButRemainsPositive()
    {
        // NOCT for X22 ≈ 41.5 °C cell temperature. With β_V = -2.3 mV/K,
        // ΔT = 16.5 K → V drop per cell = 38 mV. P_mp drops by ~ 5-8 %
        // vs STC. Test that NOCT P < STC P AND NOCT P > 0.8 × STC P.
        var stc  = PvPanelSolver.Solve(SunPowerX22Class());
        var noct = PvPanelSolver.Solve(SunPowerX22Class() with { CellTemperature_C = 41.5 });
        Assert.True(noct.MaxPower_W < stc.MaxPower_W);
        Assert.True(noct.MaxPower_W > 0.80 * stc.MaxPower_W,
            $"NOCT P_mp ({noct.MaxPower_W:F1} W) should be > 80 % of STC P_mp "
          + $"({stc.MaxPower_W:F1} W).");
    }

    [Fact]
    public void SunPowerX22_AtHighTemperature_VoltageCoefficientMatchesCluster()
    {
        // Per-cell β_V cluster mid-band = -2.3 mV/K. At T = 65 °C
        // (ΔT = 40 K), V_oc drops by 96 × 0.0023 × 40 = 8.83 V from STC.
        var stc = PvPanelSolver.Solve(SunPowerX22Class());
        var hot = PvPanelSolver.Solve(SunPowerX22Class() with { CellTemperature_C = 65.0 });
        double expected_drop_V = 96 * 0.0023 * (65.0 - 25.0);
        double actual_drop_V = stc.OpenCircuitVoltage_V - hot.OpenCircuitVoltage_V;
        Assert.Equal(expected_drop_V, actual_drop_V, precision: 4);
    }

    // ── Low-irradiance scaling ───────────────────────────────────────

    [Fact]
    public void SunPowerX22_AtLowIrradiance_CurrentScalesLinearlyWithG()
    {
        // I_sc scales linearly with G. At G = 200 W/m² (cloudy / low-sun),
        // I_sc should be exactly 20 % of STC value.
        var stc      = PvPanelSolver.Solve(SunPowerX22Class());
        var lowLight = PvPanelSolver.Solve(SunPowerX22Class() with { Irradiance_W_m2 = 200.0 });
        Assert.Equal(0.2, lowLight.ShortCircuitCurrent_A / stc.ShortCircuitCurrent_A,
                     precision: 6);
    }

    [Fact]
    public void SunPowerX22_AtZeroIrradiance_NoPowerOutput()
    {
        // Night-time / fully-shaded panel — zero incident G → zero P_mp.
        var night = PvPanelSolver.Solve(SunPowerX22Class() with { Irradiance_W_m2 = 0.0 });
        Assert.Equal(0.0, night.ShortCircuitCurrent_A, precision: 9);
        Assert.Equal(0.0, night.MaxPower_W,            precision: 9);
        Assert.Equal(0.0, night.IncidentSolarPower_W,  precision: 9);
        // V_oc remains positive — open-circuit voltage doesn't require
        // illumination in the cluster-fit model (real V_oc DOES drop
        // sharply at very low G, but that's the single-diode regime).
    }

    // ── Topology + chemistry pathway validation ──────────────────────

    [Fact]
    public void SunPowerX22_UsesMonocrystallineCellType()
    {
        // X-series cells are SunPower's IBC (Interdigitated Back-Contact)
        // monocrystalline silicon — must instantiate the Monocrystalline
        // kind, not Polycrystalline.
        Assert.Equal(PhotovoltaicCellType.Monocrystalline, SunPowerX22Class().CellType);
    }

    [Fact]
    public void SunPowerX22_HasNinetySixCellsInSeries()
    {
        // X22-360 is a 96-cell panel (vs 60-cell residential or 72-cell
        // commercial). The 96-cell architecture sets the V_oc ceiling.
        Assert.Equal(96, SunPowerX22Class().CellsInSeries);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    // SunPower X-Series X22-360 — premium 96-cell mono-Si residential
    // panel. Cell area chosen to give 1.469 m² total aperture — close
    // to the 1.631 m² physical X22 (the registry's 6.20 A/cell I_sc is
    // anchored to a slightly smaller cell footprint than X22's 153 cm²
    // pseudo-square layout, so the model uses a representative 153 cm²
    // per cell that produces ~ 360 W output at STC under the cluster
    // ratios).
    private static PvPanelDesign SunPowerX22Class() => new(
        CellType:          PhotovoltaicCellType.Monocrystalline,
        CellsInSeries:     96,
        StringsInParallel: 1,
        CellArea_cm2:      153.0,
        Irradiance_W_m2:   1000.0,
        CellTemperature_C: 25.0);
}
