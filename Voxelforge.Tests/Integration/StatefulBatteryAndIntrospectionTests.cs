// StatefulBatteryAndIntrospectionTests.cs — Sprint SI.W7 unit tests
// for the StatefulBatteryComponent (SoC-over-time Coulomb counting) +
// the new ComponentNetwork introspection helpers.

using System;
using System.Linq;
using Voxelforge.Battery;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class StatefulBatteryAndIntrospectionTests
{
    // ── StatefulBatteryComponent ─────────────────────────────────────────

    [Fact]
    public void StatefulBattery_DischargesAtConstantCurrent_LinearSoCDrop()
    {
        // Tesla 96s46p NMC pack: C_pack = 46·5 = 230 Ah at 100 A discharge:
        //   dSoC/dt = -100/(230·3600) = -1.21e-4 /s.
        //   Over 600 s: ΔSoC = -0.0725.
        var net = new ComponentNetwork();
        var battery = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 1.0);
        net.Add(battery);
        net.SetExternalInput("pack", "LoadCurrent_A", 100.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", battery);
        var hist = integrator.Run(0.0, 600.0, 1.0);
        double initial = hist[0].PortValues["pack"]["StateOfCharge"];
        double final   = hist[^1].PortValues["pack"]["StateOfCharge"];
        Assert.Equal(1.0, initial, precision: 6);
        // Expect ΔSoC ≈ -0.0724 over 599 s (last snapshot at t=599).
        Assert.InRange(initial - final, 0.06, 0.085);
    }

    [Fact]
    public void StatefulBattery_FullDischarge_ClampsAtZero()
    {
        // 230 Ah at 1000 A discharge: takes ~ 828 s to drain. Over 2000 s
        // the SoC should hit zero and stay there.
        var net = new ComponentNetwork();
        var battery = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 1.0);
        net.Add(battery);
        net.SetExternalInput("pack", "LoadCurrent_A", 1000.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", battery);
        var hist = integrator.Run(0.0, 2000.0, 10.0);
        double final = hist[^1].PortValues["pack"]["StateOfCharge"];
        Assert.Equal(0.0, final, precision: 6);
    }

    [Fact]
    public void StatefulBattery_Charging_RaisesSoc()
    {
        // Negative current (charging) raises SoC.
        var net = new ComponentNetwork();
        var battery = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 0.5);
        net.Add(battery);
        net.SetExternalInput("pack", "LoadCurrent_A", -100.0);  // charge
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", battery);
        var hist = integrator.Run(0.0, 600.0, 1.0);
        double final = hist[^1].PortValues["pack"]["StateOfCharge"];
        Assert.True(final > 0.5);
        Assert.True(final < 0.6);   // ~ 0.572
    }

    [Fact]
    public void StatefulBattery_PackVoltage_DropsAsSocDropsUnderDischarge()
    {
        // Under steady discharge, PackLoadedVoltage_V drops because:
        // (a) OCV ∝ SoC (linear OCV fit drops as SoC drops);
        // (b) I·R is fixed at constant I.
        var net = new ComponentNetwork();
        var battery = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 1.0);
        net.Add(battery);
        net.SetExternalInput("pack", "LoadCurrent_A", 200.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", battery);
        var hist = integrator.Run(0.0, 1000.0, 10.0);
        double V_start = hist[0].PortValues["pack"]["PackLoadedVoltage_V"];
        double V_end   = hist[^1].PortValues["pack"]["PackLoadedVoltage_V"];
        Assert.True(V_end < V_start);
    }

    [Fact]
    public void StatefulBattery_RejectsInvalidInitialSoc()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StatefulBatteryComponent("p", ModelSPack(), -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StatefulBatteryComponent("p", ModelSPack(),  1.5));
    }

    // ── Network introspection ───────────────────────────────────────────

    [Fact]
    public void Introspection_ComponentNames_InRegistrationOrder()
    {
        var net = new ComponentNetwork();
        net.Add(new BatteryComponent("a", ModelSPack()));
        net.Add(new BatteryComponent("b", ModelSPack()));
        net.Add(new BatteryComponent("c", ModelSPack()));
        Assert.Equal(new[] { "a", "b", "c" }, net.ComponentNames);
    }

    [Fact]
    public void Introspection_Connections_ReturnedInOrder()
    {
        var net = new ComponentNetwork();
        net.Add(new BatteryComponent("a", ModelSPack()));
        net.Add(new BatteryComponent("b", ModelSPack()));
        net.Connect("a", "PackLoadedVoltage_V", "b", "LoadCurrent_A");
        var conns = net.Connections;
        Assert.Single(conns);
        Assert.Equal("a", conns[0].FromComponent);
        Assert.Equal("PackLoadedVoltage_V", conns[0].FromPort);
        Assert.Equal("b", conns[0].ToComponent);
        Assert.Equal("LoadCurrent_A", conns[0].ToPort);
    }

    [Fact]
    public void Introspection_DescribeTopology_RendersMultilineString()
    {
        var net = new ComponentNetwork();
        net.Add(new BatteryComponent("pack", ModelSPack()));
        var desc = net.DescribeTopology();
        Assert.Contains("ComponentNetwork: 1 component(s)", desc);
        Assert.Contains("- pack", desc);
        Assert.Contains("LoadCurrent_A", desc);
        Assert.Contains("PackLoadedVoltage_V", desc);
    }

    [Fact]
    public void Introspection_TopologicalOrder_RespectsDependencies()
    {
        var net = new ComponentNetwork();
        net.Add(new BatteryComponent("b", ModelSPack()));    // added first
        net.Add(new BatteryComponent("a", ModelSPack()));    // added second
        net.Connect("a", "PackLoadedVoltage_V", "b", "LoadCurrent_A");
        net.SetExternalInput("a", "LoadCurrent_A", 50.0);
        // Topo order: a first (a → b), then b — regardless of
        // registration order.
        var order = net.GetTopologicalOrder();
        Assert.Equal(new[] { "a", "b" }, order);
    }

    [Fact]
    public void Introspection_TopologicalOrder_RaisesOnCycle()
    {
        var net = new ComponentNetwork();
        net.Add(new BatteryComponent("a", ModelSPack()));
        net.Add(new BatteryComponent("b", ModelSPack()));
        net.Connect("a", "PackLoadedVoltage_V", "b", "LoadCurrent_A");
        net.Connect("b", "PackLoadedVoltage_V", "a", "LoadCurrent_A");
        // ThrowsAny<> rather than Throws<>: per #490, the concrete exception is
        // CyclicComponentNetworkException : InvalidOperationException, and xUnit's
        // strict Throws<T> rejects subclasses.
        Assert.ThrowsAny<InvalidOperationException>(() => net.GetTopologicalOrder());
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static BatteryPackDesign ModelSPack() => new(
        Chemistry:        BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries:    96,
        ParallelStrings:  46,
        StateOfCharge:    1.0,
        LoadCurrent_A:    0.0);
}
