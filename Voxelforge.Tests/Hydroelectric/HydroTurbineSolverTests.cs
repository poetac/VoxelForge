// HydroTurbineSolverTests.cs — Sprint HE.W1 unit tests for the closed-
// form hydroelectric turbine performance snapshot.

using System;
using Voxelforge.Hydroelectric;
using Xunit;

namespace Voxelforge.Tests.Hydroelectric;

public sealed class HydroTurbineSolverTests
{
    // ── Registry ─────────────────────────────────────────────────────────

    [Fact]
    public void Registry_PeltonHeadEnvelope_HighHeadLowFlow()
    {
        var p = HydroTurbineRegistry.Pelton;
        Assert.Equal(200.0,  p.MinimumHead_m, precision: 6);
        Assert.Equal(2000.0, p.MaximumHead_m, precision: 6);
        Assert.InRange(p.PeakHydraulicEfficiency, 0.85, 0.95);
    }

    [Fact]
    public void Registry_KaplanLowestHeadEnvelope()
    {
        // Kaplan envelope must end strictly below where Francis begins.
        Assert.True(HydroTurbineRegistry.Kaplan.MaximumHead_m
                  < HydroTurbineRegistry.Francis.MaximumHead_m);
        Assert.True(HydroTurbineRegistry.Kaplan.MinimumHead_m
                  < HydroTurbineRegistry.Francis.MinimumHead_m);
    }

    [Fact]
    public void Registry_For_ThrowsOnNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HydroTurbineRegistry.For(HydroTurbineKind.None));
    }

    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNoneKind()
    {
        var d = ThreeGorgesUnit() with { Kind = HydroTurbineKind.None };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsNonPositiveHead()
    {
        var d = ThreeGorgesUnit() with { Head_m = -1.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    [Fact]
    public void Validate_RejectsZeroFlow()
    {
        var d = ThreeGorgesUnit() with { VolumetricFlowRate_m3s = 0.0 };
        Assert.Throws<ArgumentException>(() => d.ValidateSelf());
    }

    // ── Three Gorges Francis baseline ────────────────────────────────────

    [Fact]
    public void ThreeGorges_Francis_HydraulicPowerInClusterBand()
    {
        // P_hydraulic = ρ·g·Q·H = 1000·9.81·850·80 ≈ 667 MW.
        var r = HydroTurbineSolver.Solve(ThreeGorgesUnit());
        Assert.InRange(r.HydraulicPower_W, 600e6, 720e6);
    }

    [Fact]
    public void ThreeGorges_Francis_ElectricalPowerInClusterBand()
    {
        // P_elec = 0.93 · 0.97 · 667 ≈ 602 MW (cluster mid-band; real
        // units output 700 MW at higher flows).
        var r = HydroTurbineSolver.Solve(ThreeGorgesUnit());
        Assert.InRange(r.ElectricalPower_W, 400e6, 800e6);
    }

    [Fact]
    public void ThreeGorges_Francis_HeadInEnvelope()
    {
        // 80 m sits within Francis envelope [10, 700].
        var r = HydroTurbineSolver.Solve(ThreeGorgesUnit());
        Assert.True(r.HeadInValidEnvelope);
    }

    [Fact]
    public void ThreeGorges_OverallEfficiencyEqualsProductOfTurbineAndGenerator()
    {
        var r = HydroTurbineSolver.Solve(ThreeGorgesUnit());
        Assert.Equal(r.HydraulicEfficiency * r.GeneratorEfficiency,
                     r.OverallEfficiency, precision: 9);
    }

    [Fact]
    public void ShaftPowerEqualsHydraulicPowerTimesTurbineEfficiency()
    {
        var r = HydroTurbineSolver.Solve(ThreeGorgesUnit());
        Assert.Equal(r.HydraulicPower_W * r.HydraulicEfficiency,
                     r.ShaftPower_W, precision: 4);
    }

    // ── Off-envelope behaviour ───────────────────────────────────────────

    [Fact]
    public void Pelton_AtKaplanHead_FlagsOutOfEnvelope_AndDeRatesEfficiency()
    {
        // Pelton at H = 10 m (Kaplan territory) is far below the
        // Pelton envelope minimum 200 m.
        var d = ThreeGorgesUnit() with { Kind = HydroTurbineKind.Pelton, Head_m = 10.0 };
        var r = HydroTurbineSolver.Solve(d);
        Assert.False(r.HeadInValidEnvelope);
        // De-rated η must be strictly less than the Pelton peak η.
        Assert.True(r.HydraulicEfficiency < HydroTurbineRegistry.Pelton.PeakHydraulicEfficiency);
    }

    [Fact]
    public void OffEnvelopePenalty_AtEnvelopeEdge_IsNoPenalty()
    {
        // Right at the envelope edge → factor = 1.0.
        var f = HydroTurbineSolver.ComputeOffEnvelopePenalty(
            head_m: 200.0,                       // exactly at Pelton min
            props:  HydroTurbineRegistry.Pelton);
        Assert.Equal(1.0, f, precision: 9);
    }

    [Fact]
    public void OffEnvelopePenalty_BeyondFullEnvelopeWidth_ClampsAtMaxDerating()
    {
        // Pelton envelope width = 1800 m. A design 2000 m below the lower
        // edge clamps at the maximum derating.
        var f = HydroTurbineSolver.ComputeOffEnvelopePenalty(
            head_m: 200.0 - 2000.0,
            props:  HydroTurbineRegistry.Pelton);
        Assert.Equal(1.0 - HydroTurbineSolver.OffEnvelopeMaxDerating, f, precision: 6);
    }

    // ── Per-kind selectivity ─────────────────────────────────────────────

    [Fact]
    public void Francis_AtThreeGorgesHead_HigherEfficiency_ThanKaplan()
    {
        // At H = 80 m, Francis is in-envelope and Kaplan is well above
        // its envelope (max 40 m). Francis must outperform.
        var francis = HydroTurbineSolver.Solve(ThreeGorgesUnit()
            with { Kind = HydroTurbineKind.Francis });
        var kaplan  = HydroTurbineSolver.Solve(ThreeGorgesUnit()
            with { Kind = HydroTurbineKind.Kaplan });
        Assert.True(francis.HydraulicEfficiency > kaplan.HydraulicEfficiency,
            $"Francis η ({francis.HydraulicEfficiency:F4}) at H=80 should beat "
          + $"Kaplan η ({kaplan.HydraulicEfficiency:F4}, off-envelope).");
    }

    [Fact]
    public void HydraulicPowerLinearInFlow_AtConstantHead()
    {
        var lo = HydroTurbineSolver.Solve(ThreeGorgesUnit() with { VolumetricFlowRate_m3s = 425.0 });
        var hi = HydroTurbineSolver.Solve(ThreeGorgesUnit() with { VolumetricFlowRate_m3s = 850.0 });
        Assert.Equal(2.0, hi.HydraulicPower_W / lo.HydraulicPower_W, precision: 6);
    }

    [Fact]
    public void HydraulicPowerLinearInHead_AtConstantFlow()
    {
        // Stay in-envelope: 40 m and 80 m are both within Francis [10, 700].
        var lo = HydroTurbineSolver.Solve(ThreeGorgesUnit() with { Head_m = 40.0 });
        var hi = HydroTurbineSolver.Solve(ThreeGorgesUnit() with { Head_m = 80.0 });
        Assert.Equal(2.0, hi.HydraulicPower_W / lo.HydraulicPower_W, precision: 6);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // Three Gorges Dam single Francis unit — 700 MW at Q ≈ 850 m³/s,
    // H_net ≈ 80 m. The cluster anchor lands at ~ 600 MW; real units
    // exceed 700 MW at higher flow rates during high-water periods.
    private static HydroTurbineDesign ThreeGorgesUnit() => new(
        Kind:                    HydroTurbineKind.Francis,
        Head_m:                  80.0,
        VolumetricFlowRate_m3s:  850.0,
        GeneratorEfficiency:     0.97);
}
