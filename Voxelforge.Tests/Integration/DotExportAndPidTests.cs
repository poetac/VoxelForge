// DotExportAndPidTests.cs — Sprint SI.W8 + SI.W9 unit tests for the
// DOT graph-export helper + the PidControllerComponent.

using System;
using System.Collections.Generic;
using System.Linq;
using Voxelforge.Battery;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class DotExportAndPidTests
{
    // ── DOT export ───────────────────────────────────────────────────────

    [Fact]
    public void DotExport_RendersSyntacticallyValidGraph()
    {
        var net = new ComponentNetwork();
        net.Add(new BatteryComponent("pack", ModelSPack()));
        net.SetExternalInput("pack", "LoadCurrent_A", 100.0);
        var dot = net.ExportToDot();
        Assert.StartsWith("digraph ComponentNetwork", dot);
        Assert.Contains("rankdir=LR", dot);
        Assert.Contains("\"pack\"", dot);
        Assert.Matches(@"\}\r?\n\z", dot);
    }

    [Fact]
    public void DotExport_IncludesExternalInputNodes()
    {
        var net = new ComponentNetwork();
        net.Add(new BatteryComponent("pack", ModelSPack()));
        net.SetExternalInput("pack", "LoadCurrent_A", 100.0);
        var dot = net.ExportToDot();
        Assert.Contains("ext_pack_LoadCurrent_A", dot);
        Assert.Contains("style=dashed", dot);
    }

    [Fact]
    public void DotExport_RendersConnectionEdges()
    {
        var net = new ComponentNetwork();
        net.Add(new BatteryComponent("a", ModelSPack()));
        net.Add(new BatteryComponent("b", ModelSPack()));
        net.Connect("a", "PackLoadedVoltage_V", "b", "LoadCurrent_A");
        var dot = net.ExportToDot();
        Assert.Contains("\"a\" -> \"b\"", dot);
        Assert.Contains("PackLoadedVoltage_V → LoadCurrent_A", dot);
    }

    [Fact]
    public void DotExport_NodeLabelIncludesPortCounts()
    {
        var net = new ComponentNetwork();
        net.Add(new BatteryComponent("pack", ModelSPack()));
        var dot = net.ExportToDot();
        // BatteryComponent: 1 input, 3 outputs.
        Assert.Contains("[1 in, 3 out]", dot);
    }

    // ── PID controller in isolation ─────────────────────────────────────

    [Fact]
    public void Pid_ProportionalOnlyPid_AppliesGainToError()
    {
        // K_p = 2, K_i = 0, K_d = 0. Error = setpoint - PV = 10 - 5 = 5.
        // u = 2 · 5 = 10.
        var net = new ComponentNetwork();
        net.Add(new PidControllerComponent("pid", proportionalGain: 2.0));
        net.SetExternalInput("pid", "Setpoint",        10.0);
        net.SetExternalInput("pid", "ProcessVariable",  5.0);
        var r = net.Solve();
        Assert.Equal(10.0, r["pid"]["ControlOutput"], precision: 6);
        Assert.Equal(5.0,  r["pid"]["Error"],         precision: 6);
    }

    [Fact]
    public void Pid_IntegralTerm_AccumulatesErrorOverTime()
    {
        // K_p = 1, K_i = 1, K_d = 0. Constant error = 1.0.
        // After t seconds at constant error, integral = t.
        // u = K_p·e + K_i·integral = 1·1 + 1·t.
        var net = new ComponentNetwork();
        var pid = new PidControllerComponent("pid", 1.0, integralGain: 1.0);
        net.Add(pid);
        net.SetExternalInput("pid", "Setpoint",        1.0);
        net.SetExternalInput("pid", "ProcessVariable", 0.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pid", pid);
        var hist = integrator.Run(0.0, 5.0, 0.1);
        // At t = 0: integral = 0, u = 1.
        Assert.Equal(1.0, hist[0].PortValues["pid"]["ControlOutput"], precision: 6);
        // At t = 4.9 (last snapshot): integral ≈ 4.9, u ≈ 5.9.
        // Allow some slack for the Euler step.
        double u_final = hist[^1].PortValues["pid"]["ControlOutput"];
        Assert.InRange(u_final, 5.5, 6.3);
    }

    // ── Closed-loop control demo ────────────────────────────────────────

    [Fact]
    public void ClosedLoop_PidDrivesPlantTowardSetpoint()
    {
        // Closed-loop network:
        //   PID receives Setpoint (external) + PV (from "plant")
        //   PID emits ControlOutput → plant's input
        //   Plant: a simple 1st-order lag (state y; dy/dt = (u - y)/τ).
        //
        // For Sprint SI.W9 we use a tiny inline first-order-lag
        // stateful component as the "plant". The closed-loop must
        // converge — Solve() raises (cycle), SolveIterative() solves.
        var net = new ComponentNetwork();
        var pid = new PidControllerComponent("pid", 1.0, integralGain: 0.5);
        var plant = new FirstOrderLagPlant("plant", timeConstant_s: 2.0, initialY: 0.0);
        net.Add(pid);
        net.Add(plant);
        net.Connect("pid", "ControlOutput", "plant", "u");
        net.Connect("plant", "y", "pid", "ProcessVariable");
        net.SetExternalInput("pid", "Setpoint", 5.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pid", pid);
        integrator.RegisterStateful("plant", plant);
        var hist = integrator.Run(0.0, 30.0, 0.05, useIterativeSolve: true);
        // Plant should track setpoint within 10 % at the end.
        double y_final = hist[^1].PortValues["plant"]["y"];
        Assert.InRange(y_final, 4.5, 5.5);
    }

    private sealed class FirstOrderLagPlant : SystemComponent, IStatefulComponent
    {
        private readonly double _tau;
        private readonly double _initialY;
        private double _y;

        public FirstOrderLagPlant(string name, double timeConstant_s, double initialY)
            : base(name)
        {
            _tau = timeConstant_s;
            _initialY = initialY;
            _y = initialY;
        }

        public override IReadOnlyList<string> InputPorts { get; } = new[] { "u" };
        public override IReadOnlyList<string> OutputPorts { get; } = new[] { "y" };

        public override void Evaluate(
            IReadOnlyDictionary<string, double> inputs,
            IDictionary<string, double> outputs)
        {
            outputs["y"] = _y;
        }

        public IReadOnlyList<string> StateVariables { get; } = new[] { "y" };

        public void ComputeDerivatives(
            ReadOnlySpan<double> state,
            IReadOnlyDictionary<string, double> portInputs,
            IReadOnlyDictionary<string, double> portOutputs,
            Span<double> derivatives)
        {
            double u = portInputs["u"];
            derivatives[0] = (u - state[0]) / _tau;
        }

        public void GetInitialState(Span<double> destination) => destination[0] = _initialY;
        public void GetCurrentState(Span<double> destination) => destination[0] = _y;
        public void SetState(ReadOnlySpan<double> state) => _y = state[0];
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static BatteryPackDesign ModelSPack() => new(
        Chemistry:        BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries:    96,
        ParallelStrings:  46,
        StateOfCharge:    1.0,
        LoadCurrent_A:    0.0);
}
