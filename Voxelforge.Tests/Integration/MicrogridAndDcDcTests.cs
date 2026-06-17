// MicrogridAndDcDcTests.cs — Sprint SI.W11 + SI.W12 unit tests for
// the DC-DC converter component + time-varying external inputs +
// headline diurnal-microgrid demo.

using System;
using System.Linq;
using Voxelforge.Battery;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Voxelforge.Photovoltaic;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class MicrogridAndDcDcTests
{
    // ── DC-DC converter ─────────────────────────────────────────────────

    [Fact]
    public void DcDcConverter_PowerConservation_AtIdealEfficiency()
    {
        // η = 1.0: P_in = P_out exactly.
        var net = new ComponentNetwork();
        net.Add(new DcDcConverterComponent("conv", 1.0));
        net.SetExternalInput("conv", "InputVoltage_V",  400.0);
        net.SetExternalInput("conv", "OutputVoltage_V", 100.0);
        net.SetExternalInput("conv", "OutputCurrent_A",  80.0);
        var r = net.Solve();
        // P_out = 100 · 80 = 8000 W. P_in = 8000 W. I_in = 8000/400 = 20 A.
        Assert.Equal(20.0, r["conv"]["InputCurrent_A"], precision: 6);
        Assert.Equal(0.0,  r["conv"]["PowerLoss_W"],    precision: 4);
    }

    [Fact]
    public void DcDcConverter_NonIdealEfficiency_LossesPositive()
    {
        // η = 0.95: P_in > P_out.
        var net = new ComponentNetwork();
        net.Add(new DcDcConverterComponent("conv", 0.95));
        net.SetExternalInput("conv", "InputVoltage_V",  400.0);
        net.SetExternalInput("conv", "OutputVoltage_V", 100.0);
        net.SetExternalInput("conv", "OutputCurrent_A",  80.0);
        var r = net.Solve();
        // P_out = 8000; P_in = 8000/0.95 = 8421.05. Loss = 421.05.
        Assert.Equal(8000.0 / 0.95 / 400.0, r["conv"]["InputCurrent_A"], precision: 4);
        Assert.InRange(r["conv"]["PowerLoss_W"], 400.0, 450.0);
    }

    [Fact]
    public void DcDcConverter_RejectsInvalidEfficiency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DcDcConverterComponent("c", 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DcDcConverterComponent("c", 1.5));
    }

    // ── Time-varying external inputs ────────────────────────────────────

    [Fact]
    public void TimeVarying_ConstantCallback_BehavesLikeFixedExternal()
    {
        // Callback that returns a constant should behave like
        // SetExternalInput(value).
        var net = new ComponentNetwork();
        net.Add(new BatteryComponent("pack", ModelSPack()));
        net.SetTimeVaryingExternalInput("pack", "LoadCurrent_A", t => 100.0);
        var integrator = new TimeStepIntegrator(net);
        var hist = integrator.Run(0.0, 1.0, 0.5);
        // Closed [0, 1] N+1 semantics (#553): round((1-0)/0.5)+1 = 3 ticks
        // at t = 0.0, 0.5, 1.0. Pre-#553 the half-open loop emitted 2.
        Assert.Equal(3, hist.Count);
        Assert.True(hist[0].PortValues["pack"]["PackElectricalPower_W"] > 0);
    }

    [Fact]
    public void TimeVarying_RampCallback_DrivesIncreasingOutput()
    {
        // Battery load current ramps 0 → 200 A over 100 s.
        var net = new ComponentNetwork();
        net.Add(new BatteryComponent("pack", ModelSPack()));
        net.SetTimeVaryingExternalInput("pack", "LoadCurrent_A",
            t => 200.0 * t / 100.0);
        var integrator = new TimeStepIntegrator(net);
        var hist = integrator.Run(0.0, 100.0, 10.0);
        // Power should be monotonically increasing (current rises,
        // V_pack roughly constant at SoC=1).
        for (int k = 1; k < hist.Count; k++)
            Assert.True(hist[k].PortValues["pack"]["PackElectricalPower_W"]
                      > hist[k - 1].PortValues["pack"]["PackElectricalPower_W"]);
    }

    // ── Diurnal-microgrid demo (PV + Battery, 24-hour cycle) ────────────

    [Fact]
    public void Microgrid_DiurnalPvCharging_RaisesBatterySoC()
    {
        // Setup: PV array (1 panel for simplicity) charges a half-full
        // battery pack. Irradiance varies sinusoidally over a day:
        //   G(t) = G_max · max(0, sin(π · t / dayLength))
        // for daytime; 0 at night.
        //
        // The PV's MaxPower_W output is the available solar power; we
        // wire it as the charge power to the StatefulBatteryComponent
        // by converting power → current via the pack's voltage. But
        // our scaffold doesn't have a power→current wire-up
        // automatically; we use the time-varying input callback to
        // compute an equivalent charge current per tick.
        //
        // For Sprint SI.W12 we keep it very simple — PV feeds a
        // time-varying charge current onto the battery (i.e. assume
        // a perfect MPPT + constant-voltage charger upstream).
        var dayLength_s = 12.0 * 3600.0;    // 12-hour daylight period

        var net = new ComponentNetwork();
        var battery = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 0.5);
        net.Add(battery);
        // I_charge = -50 A during peak sun (negative = charging),
        // tapering with sin(π·t/12h).
        net.SetTimeVaryingExternalInput("pack", "LoadCurrent_A",
            t =>
            {
                double phase = Math.PI * t / dayLength_s;
                double sunFactor = Math.Max(0.0, Math.Sin(phase));
                return -50.0 * sunFactor;    // negative = charging
            });

        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", battery);
        var hist = integrator.Run(0.0, dayLength_s, 600.0);   // 12 hours, 10-min steps

        // SoC starts at 0.5 and increases monotonically (no discharge
        // load — only charge from PV).
        double soc_start = hist[0].PortValues["pack"]["StateOfCharge"];
        double soc_end   = hist[^1].PortValues["pack"]["StateOfCharge"];
        Assert.Equal(0.5, soc_start, precision: 6);
        Assert.True(soc_end > 0.5);
        // Total charge over 12 hours of sinusoidal current peaking at
        // 50 A: integral of 50·sin(πt/12h) dt from 0 to 12h is
        // 50·(12h)·(2/π) = 50·12·3600·0.637 ≈ 1.375e6 As → 1.375e6/3600
        // = 382 Ah. Pack capacity 230 Ah → in theory enough to fully
        // charge twice over. SoC should clamp at 1.0.
        Assert.True(soc_end > 0.9);
    }

    [Fact]
    public void Microgrid_AllNight_NoCharge_SoCStable()
    {
        // Pure night case: no PV at all → no charge → SoC stays put
        // (no load either).
        var net = new ComponentNetwork();
        var battery = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 0.3);
        net.Add(battery);
        net.SetTimeVaryingExternalInput("pack", "LoadCurrent_A", t => 0.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", battery);
        var hist = integrator.Run(0.0, 3600.0, 60.0);
        // 1-hour simulation, zero load, zero charge → SoC stays at 0.3.
        Assert.Equal(0.3, hist[^1].PortValues["pack"]["StateOfCharge"], precision: 6);
    }

    private static BatteryPackDesign ModelSPack() => new(
        Chemistry:        BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries:    96,
        ParallelStrings:  46,
        StateOfCharge:    1.0,
        LoadCurrent_A:    0.0);
}
