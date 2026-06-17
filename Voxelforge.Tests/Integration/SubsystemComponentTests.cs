// SubsystemComponentTests.cs — Sprint SI.W19 unit tests for the
// hierarchical SubsystemComponent.

using System;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Voxelforge.Photovoltaic;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class SubsystemComponentTests
{
    [Fact]
    public void Subsystem_WrapsSingleAccumulator_RoundTripsInputAndOutput()
    {
        // Inner network: one AccumulatorComponent (stateful — needs
        // allowStatefulInner per B.8b. This test never registers the
        // subsystem with a TimeStepIntegrator, so the algebraic-only
        // contract is the intended behaviour.)
        // Subsystem exposes: parent input "Rate" → acc.Input_rate,
        //                    parent output "Total" ← acc.Accumulated_total.
        var subnet = new ComponentNetwork();
        subnet.Add(new AccumulatorComponent("acc", initial: 7.0));
        var sub = new SubsystemComponent(
            name: "box",
            subnet: subnet,
            inputBindings:  new[] { ("Rate",  "acc", "Input_rate") },
            outputBindings: new[] { ("Total", "acc", "Accumulated_total") },
            allowStatefulInner: true);

        var parent = new ComponentNetwork();
        parent.Add(sub);
        parent.SetExternalInput("box", "Rate", 0.0);
        var r = parent.Solve();
        Assert.Equal(7.0, r["box"]["Total"], precision: 6);
    }

    [Fact]
    public void Subsystem_Composes_TwoComponents_Internally()
    {
        // Inner network: PV → Accumulator, connected. Subsystem
        // exposes parent input Irradiance + Temp; output TotalEnergy.
        var subnet = new ComponentNetwork();
        subnet.Add(new PhotovoltaicComponent("pv", DefaultPanelDesign()));
        subnet.Add(new AccumulatorComponent("acc", initial: 0.0));
        subnet.Connect("pv", "MaxPower_W", "acc", "Input_rate");
        var sub = new SubsystemComponent(
            name: "harvester",
            subnet: subnet,
            inputBindings:  new[]
            {
                ("Irradiance_W_m2",   "pv",  "Irradiance_W_m2"),
                ("CellTemperature_C", "pv",  "CellTemperature_C"),
            },
            outputBindings: new[]
            {
                ("InstantPower_W",  "pv",  "MaxPower_W"),
                ("CumulativeEnergy_J", "acc", "Accumulated_total"),
            },
            allowStatefulInner: true);  // Accumulator is IStatefulComponent

        var parent = new ComponentNetwork();
        parent.Add(sub);
        parent.SetExternalInput("harvester", "Irradiance_W_m2",   1000.0);
        parent.SetExternalInput("harvester", "CellTemperature_C", 25.0);
        var r = parent.Solve();
        Assert.True(r["harvester"]["InstantPower_W"] > 0);
        Assert.Equal(0.0, r["harvester"]["CumulativeEnergy_J"], precision: 6);
    }

    [Fact]
    public void Subsystem_OnlyExposesBoundPorts()
    {
        var subnet = new ComponentNetwork();
        subnet.Add(new AccumulatorComponent("a"));
        var sub = new SubsystemComponent(
            name: "x",
            subnet: subnet,
            inputBindings:  new[] { ("MyInput", "a", "Input_rate") },
            outputBindings: new[] { ("MyOutput", "a", "Accumulated_total") },
            allowStatefulInner: true);  // Accumulator is IStatefulComponent
        Assert.Single(sub.InputPorts);
        Assert.Equal("MyInput", sub.InputPorts[0]);
        Assert.Single(sub.OutputPorts);
        Assert.Equal("MyOutput", sub.OutputPorts[0]);
    }

    [Fact]
    public void Subsystem_DownstreamPropagationThroughHierarchy()
    {
        // Parent: subsystem.OutputPower_W → accumulator.Input_rate.
        // Asserts a parent-level wire correctly carries a value from
        // inside the subsystem.
        var subnet = new ComponentNetwork();
        subnet.Add(new PhotovoltaicComponent("pv", DefaultPanelDesign()));
        var sub = new SubsystemComponent(
            name: "harvester",
            subnet: subnet,
            inputBindings:  new[]
            {
                ("Irradiance_W_m2",   "pv", "Irradiance_W_m2"),
                ("CellTemperature_C", "pv", "CellTemperature_C"),
            },
            outputBindings: new[]
            {
                ("Power_W",  "pv", "MaxPower_W"),
            });

        var parent = new ComponentNetwork();
        parent.Add(sub);
        parent.Add(new AccumulatorComponent("totals"));
        parent.Connect("harvester", "Power_W", "totals", "Input_rate");
        parent.SetExternalInput("harvester", "Irradiance_W_m2", 1000.0);
        parent.SetExternalInput("harvester", "CellTemperature_C", 25.0);
        var r = parent.Solve();
        Assert.True(r["harvester"]["Power_W"] > 0);
        Assert.Equal(0.0, r["totals"]["Accumulated_total"], precision: 6);
    }

    [Fact]
    public void Subsystem_RejectsStatefulInnerByDefault()
    {
        // B.8b / issue #493 defensive — without the explicit opt-in,
        // the constructor must refuse a subnet containing any
        // IStatefulComponent. Catches the foot-gun at construction
        // instead of letting silent state-non-evolution corrupt a
        // transient simulation.
        var subnet = new ComponentNetwork();
        subnet.Add(new AccumulatorComponent("acc"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new SubsystemComponent(
                name: "box",
                subnet: subnet,
                inputBindings:  new[] { ("Rate",  "acc", "Input_rate") },
                outputBindings: new[] { ("Total", "acc", "Accumulated_total") }));
        Assert.Contains("IStatefulComponent", ex.Message);
        Assert.Contains("allowStatefulInner", ex.Message);
    }

    [Fact]
    public void Subsystem_AcceptsAlgebraicOnlyInner_WithoutOptIn()
    {
        // The guard fires only on IStatefulComponent in the subnet.
        // A pure-algebraic subnet (e.g. only PhotovoltaicComponent)
        // constructs cleanly without the opt-in flag — the historical
        // Wave-1 behaviour for stateless transfer-function subsystems.
        var subnet = new ComponentNetwork();
        subnet.Add(new PhotovoltaicComponent("pv", DefaultPanelDesign()));
        var sub = new SubsystemComponent(
            name: "harvester",
            subnet: subnet,
            inputBindings:  new[]
            {
                ("Irradiance_W_m2",   "pv", "Irradiance_W_m2"),
                ("CellTemperature_C", "pv", "CellTemperature_C"),
            },
            outputBindings: new[] { ("Power_W", "pv", "MaxPower_W") });
        Assert.Equal("harvester", sub.Name);
    }

    private static PvPanelDesign DefaultPanelDesign() => new(
        CellType:           PhotovoltaicCellType.Monocrystalline,
        CellsInSeries:      60,
        StringsInParallel:  1,
        CellArea_cm2:       243.0,
        Irradiance_W_m2:    1000.0,
        CellTemperature_C:  25.0);
}
