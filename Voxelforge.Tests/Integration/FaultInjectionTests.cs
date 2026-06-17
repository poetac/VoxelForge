// FaultInjectionTests.cs — Sprint SI.W17 unit tests for the fault-
// injection capability of ComponentNetwork.

using System;
using System.Linq;
using Voxelforge.Battery;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Voxelforge.Photovoltaic;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class FaultInjectionTests
{
    // ── Immediate fault state ────────────────────────────────────────────

    [Fact]
    public void Faulted_Component_Outputs_AllZero()
    {
        // Manually fault the PV component. All its outputs (MaxPower_W,
        // VoltageAtMaxPower_V, etc.) should read 0.0.
        var net = new ComponentNetwork();
        net.Add(new PhotovoltaicComponent("pv", DefaultPanelDesign()));
        net.SetExternalInput("pv", "Irradiance_W_m2", 1000.0);
        net.SetExternalInput("pv", "CellTemperature_C", 25.0);

        var r0 = net.Solve();
        Assert.True(r0["pv"]["MaxPower_W"] > 0);

        net.SetComponentFaulted("pv", true);
        var r1 = net.Solve();
        Assert.Equal(0.0, r1["pv"]["MaxPower_W"], precision: 9);
        Assert.True(net.IsComponentFaulted("pv"));
    }

    [Fact]
    public void Unfaulting_Restores_Normal_Evaluation()
    {
        var net = new ComponentNetwork();
        net.Add(new PhotovoltaicComponent("pv", DefaultPanelDesign()));
        net.SetExternalInput("pv", "Irradiance_W_m2", 1000.0);
        net.SetExternalInput("pv", "CellTemperature_C", 25.0);

        net.SetComponentFaulted("pv", true);
        var r1 = net.Solve();
        Assert.Equal(0.0, r1["pv"]["MaxPower_W"], precision: 9);

        net.SetComponentFaulted("pv", false);
        var r2 = net.Solve();
        Assert.True(r2["pv"]["MaxPower_W"] > 0);
        Assert.False(net.IsComponentFaulted("pv"));
    }

    [Fact]
    public void SetComponentFaulted_RejectsUnknownComponent()
    {
        var net = new ComponentNetwork();
        Assert.Throws<InvalidOperationException>(
            () => net.SetComponentFaulted("nope", true));
    }

    // ── Scheduled fault injection ────────────────────────────────────────

    [Fact]
    public void Scheduled_Fault_Trips_At_Configured_Time()
    {
        // Battery starts discharging; at t=300 s a fault is scheduled
        // → SoC stops dropping (battery output is forced zero, so
        // dSoC/dt = 0 too — load current is unchanged externally but
        // the stateful component sees the same input via external feed).
        // Actually because the battery is faulted, Evaluate is skipped
        // → outputs zero → but state ALSO can't update via the
        // integrator's ComputeDerivatives if we still call it. The
        // observable effect we test here: PackElectricalPower_W is 0
        // for t ≥ 300, > 0 for t < 300.
        var net = new ComponentNetwork();
        var pack = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 1.0);
        net.Add(pack);
        net.SetExternalInput("pack", "LoadCurrent_A", 100.0);
        net.ScheduleFault(time_s: 300.0, componentName: "pack", faulted: true);

        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", pack);
        var hist = integrator.Run(0.0, 600.0, 60.0);

        // Pre-fault snapshot: t ∈ [0, 240], all P > 0.
        for (int k = 0; k < 5; k++)
            Assert.True(hist[k].PortValues["pack"]["PackElectricalPower_W"] > 0);
        // Post-fault snapshot: t ≥ 300, all P = 0.
        for (int k = 5; k < hist.Count; k++)
            Assert.Equal(0.0,
                hist[k].PortValues["pack"]["PackElectricalPower_W"],
                precision: 9);
    }

    [Fact]
    public void Scheduled_Fault_Recovery_Restores_Output()
    {
        // Fault at t=120, recover at t=300. Battery output: ON [0,120),
        // OFF [120,300), ON [300,…).
        var net = new ComponentNetwork();
        var pack = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 1.0);
        net.Add(pack);
        net.SetExternalInput("pack", "LoadCurrent_A", 100.0);
        net.ScheduleFault(120.0, "pack", faulted: true);
        net.ScheduleFault(300.0, "pack", faulted: false);

        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", pack);
        var hist = integrator.Run(0.0, 600.0, 60.0);

        // First two snapshots (t=0, t=60): ON.
        Assert.True(hist[0].PortValues["pack"]["PackElectricalPower_W"] > 0);
        Assert.True(hist[1].PortValues["pack"]["PackElectricalPower_W"] > 0);
        // t=120 thru t=240: OFF.
        Assert.Equal(0.0, hist[2].PortValues["pack"]["PackElectricalPower_W"],
            precision: 9);
        Assert.Equal(0.0, hist[4].PortValues["pack"]["PackElectricalPower_W"],
            precision: 9);
        // t=300 onwards: ON again.
        Assert.True(hist[5].PortValues["pack"]["PackElectricalPower_W"] > 0);
    }

    // ── Out-of-order schedule entries ────────────────────────────────────

    [Fact]
    public void Scheduled_Faults_RegisteredOutOfChronologicalOrder_StillFireInOrder()
    {
        // Regression test (post-merge audit): a user who schedules a
        // recovery (OFF) BEFORE the corresponding fault (ON) must still
        // see the chronologically-correct final state. Before the
        // OrderBy fix, the iteration order matched insertion order and
        // a late call to ScheduleFault(120, ON) would override the
        // earlier ScheduleFault(300, OFF) on the same tick.
        var net = new ComponentNetwork();
        var pack = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 1.0);
        net.Add(pack);
        net.SetExternalInput("pack", "LoadCurrent_A", 100.0);
        // Intentionally register OFF before ON.
        net.ScheduleFault(300.0, "pack", faulted: false);
        net.ScheduleFault(120.0, "pack", faulted: true);

        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", pack);
        var hist = integrator.Run(0.0, 600.0, 60.0);

        // Same shape as Scheduled_Fault_Recovery_Restores_Output:
        // ON [0,120) — OFF [120,300) — ON [300,…)
        Assert.True(hist[0].PortValues["pack"]["PackElectricalPower_W"] > 0);
        Assert.Equal(0.0, hist[2].PortValues["pack"]["PackElectricalPower_W"],
            precision: 9);
        Assert.True(hist[5].PortValues["pack"]["PackElectricalPower_W"] > 0);
    }

    // ── Fault propagation through connections ────────────────────────────

    [Fact]
    public void Faulted_Source_Zeros_Downstream_Wire()
    {
        // Wire PV.MaxPower_W -> Accumulator.Input_rate. When PV is
        // faulted, the accumulator's growth stops.
        var net = new ComponentNetwork();
        net.Add(new PhotovoltaicComponent("pv", DefaultPanelDesign()));
        var acc = new AccumulatorComponent("energy");
        net.Add(acc);
        net.Connect("pv", "MaxPower_W", "energy", "Input_rate");
        net.SetExternalInput("pv", "Irradiance_W_m2", 1000.0);
        net.SetExternalInput("pv", "CellTemperature_C", 25.0);
        net.ScheduleFault(60.0, "pv", true);

        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("energy", acc);
        var hist = integrator.Run(0.0, 180.0, 10.0);

        // Pre-fault growth.
        double early = hist[5].PortValues["energy"]["Accumulated_total"];
        Assert.True(early > 0);
        // Post-fault flat: t=170 should be very close to t=60 value.
        double atFault   = hist[6].PortValues["energy"]["Accumulated_total"];
        double afterLong = hist[^1].PortValues["energy"]["Accumulated_total"];
        Assert.Equal(atFault, afterLong, precision: 4);
    }

    private static BatteryPackDesign ModelSPack() => new(
        Chemistry:        BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries:    96,
        ParallelStrings:  46,
        StateOfCharge:    1.0,
        LoadCurrent_A:    0.0);

    private static PvPanelDesign DefaultPanelDesign() => new(
        CellType:           PhotovoltaicCellType.Monocrystalline,
        CellsInSeries:      60,
        StringsInParallel:  1,
        CellArea_cm2:       243.0,
        Irradiance_W_m2:    1000.0,
        CellTemperature_C:  25.0);
}
