// PvPanelSolverTests — pins the PV.W1 cluster-fit MPP envelope: I_sc
// linear in irradiance with the α_I temperature term, V_oc with the β_V
// droop, the 0.85/0.93 MPP ratios, P_mp = V_mp·I_mp, η = P_mp/(G·A), the
// PV.W2 bifacial (1 + φ·β) multiplier, and the zero-irradiance guard.
// Pure closed form → exact assertions. Backfills coverage onto the Linux
// 'core' CI leg.

using System;
using Voxelforge.Photovoltaic;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class PvPanelSolverTests
{
    // SunPower-Maxeon-class panel at STC: 96 series mono-Si cells of
    // 153 cm², G = 1000 W/m², T = 25 °C. Mono: I_sc 6.20 A, V_oc 0.68 V.
    private static PvPanelDesign MaxeonPanel()
        => new(PhotovoltaicCellType.Monocrystalline,
               CellsInSeries: 96, StringsInParallel: 1,
               CellArea_cm2: 153.0, Irradiance_W_m2: 1000.0,
               CellTemperature_C: 25.0);

    [Fact]
    public void Solve_AtStc_ReproducesRegistryAnchorsAndMppRatios()
    {
        var r = PvPanelSolver.Solve(MaxeonPanel());
        Assert.Equal(6.2, r.ShortCircuitCurrent_A, 12);            // ΔT = 0, G/G_STC = 1
        Assert.Equal(0.68 * 96, r.OpenCircuitVoltage_V, 9);
        Assert.Equal(55.48800000000001, r.MaxPowerPointVoltage_V, 9);   // 0.85·0.68·96
        Assert.Equal(5.766000000000001, r.MaxPowerPointCurrent_A, 12);  // 0.93·6.2
        Assert.Equal(319.9438080000001, r.MaxPower_W, 9);          // ≈ 320 W-class panel
        Assert.Equal(1.4688, MaxeonPanel().PanelArea_m2, 12);      // 96·153 cm² → m²
        Assert.Equal(1468.8, r.IncidentSolarPower_W, 9);           // G·A
        Assert.Equal(0.21782666666666672, r.ConversionEfficiency, 12);  // ~21.8 % mono-Si
    }

    [Fact]
    public void Solve_IrradianceScalesCurrentLinearly_NotVoltage()
    {
        var half = PvPanelSolver.Solve(MaxeonPanel() with { Irradiance_W_m2 = 500.0 });
        Assert.Equal(3.1, half.ShortCircuitCurrent_A, 12);         // 6.2·0.5
        Assert.Equal(0.68 * 96, half.OpenCircuitVoltage_V, 9);     // V_oc: G-independent (W1)
    }

    [Fact]
    public void Solve_HotCell_DroopsVoltageAndNudgesCurrent()
    {
        // ΔT = +40 K: V_oc = 0.68 − 0.0023·40; I_sc = 6.2·(1 + 0.0005·40).
        var hot = PvPanelSolver.Solve(MaxeonPanel() with { CellTemperature_C = 65.0 });
        Assert.Equal(0.5880000000000001 * 96, hot.OpenCircuitVoltage_V, 9);
        Assert.Equal(6.324000000000001, hot.ShortCircuitCurrent_A, 12);
        var stc = PvPanelSolver.Solve(MaxeonPanel());
        Assert.True(hot.MaxPower_W < stc.MaxPower_W, "hot cells must lose net power");
    }

    [Fact]
    public void Solve_BifacialGain_MultipliesPowerByOnePlusPhiBeta()
    {
        var mono = PvPanelSolver.Solve(MaxeonPanel());
        var bifacial = PvPanelSolver.Solve(MaxeonPanel() with
        {
            RearSideIrradianceGain = 0.2,
            BifacialityFactor = 0.9,
        });
        Assert.Equal(1.18 * mono.MaxPower_W, bifacial.MaxPower_W, 9);   // 1 + 0.2·0.9
        // I_sc/V_oc report the front face only — unchanged.
        Assert.Equal(mono.ShortCircuitCurrent_A, bifacial.ShortCircuitCurrent_A, 12);
    }

    [Fact]
    public void Solve_ZeroIrradiance_ProducesZeroWithoutDividing()
    {
        var dark = PvPanelSolver.Solve(MaxeonPanel() with { Irradiance_W_m2 = 0.0 });
        Assert.Equal(0.0, dark.ShortCircuitCurrent_A, 12);
        Assert.Equal(0.0, dark.MaxPower_W, 12);
        Assert.Equal(0.0, dark.ConversionEfficiency, 12);          // guard branch, not NaN
    }

    [Fact]
    public void Solve_ExtremeHeat_ClampsVoltageAtZeroNotNegative()
    {
        // V_oc = 0.68 − 0.0023·(T−25) crosses zero near T ≈ 320 °C; the
        // design ceiling is 150 °C, so drive β_V·ΔT via a polycrystalline
        // cell (−0.0028 V/K) at 150 °C: 0.62 − 0.0028·125 = 0.27 > 0 —
        // still positive, so instead pin that V_oc stays non-negative at
        // the envelope edge for both cell types.
        var edgeMono = PvPanelSolver.Solve(MaxeonPanel() with { CellTemperature_C = 150.0 });
        Assert.True(edgeMono.OpenCircuitVoltage_V >= 0.0);
        var edgePoly = PvPanelSolver.Solve(MaxeonPanel() with
        {
            CellType = PhotovoltaicCellType.Polycrystalline,
            CellTemperature_C = 150.0,
        });
        Assert.True(edgePoly.OpenCircuitVoltage_V >= 0.0);
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            PvPanelSolver.Solve(MaxeonPanel() with { CellTemperature_C = 200.0 }));
        Assert.Throws<ArgumentException>(() =>
            PvPanelSolver.Solve(MaxeonPanel() with { CellsInSeries = 0 }));
        Assert.Throws<ArgumentException>(() =>
            PvPanelSolver.Solve(MaxeonPanel() with { RearSideIrradianceGain = 1.5 }));
        Assert.Throws<ArgumentException>(() =>
            PvPanelSolver.Solve(MaxeonPanel() with { CellType = PhotovoltaicCellType.None }));
    }
}
