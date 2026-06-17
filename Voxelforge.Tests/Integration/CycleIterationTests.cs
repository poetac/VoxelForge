// CycleIterationTests.cs — Sprint SI.W3 unit tests for the Gauss-
// Seidel iterative solver, including closed-loop / feedback cases
// where the acyclic Solve() raises.

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class CycleIterationTests
{
    // A simple "gain" component: out = gain · in.
    private sealed class GainComponent : SystemComponent
    {
        private readonly double _gain;
        public GainComponent(string name, double gain) : base(name) { _gain = gain; }
        public override IReadOnlyList<string> InputPorts { get; } = new[] { "x" };
        public override IReadOnlyList<string> OutputPorts { get; } = new[] { "y" };
        public override void Evaluate(
            IReadOnlyDictionary<string, double> inputs,
            IDictionary<string, double> outputs)
        {
            outputs["y"] = _gain * inputs["x"];
        }
    }

    // A simple "summing" component: out = a + b.
    private sealed class SumComponent : SystemComponent
    {
        public SumComponent(string name) : base(name) { }
        public override IReadOnlyList<string> InputPorts { get; } = new[] { "a", "b" };
        public override IReadOnlyList<string> OutputPorts { get; } = new[] { "sum" };
        public override void Evaluate(
            IReadOnlyDictionary<string, double> inputs,
            IDictionary<string, double> outputs)
        {
            outputs["sum"] = inputs["a"] + inputs["b"];
        }
    }

    // ── Acyclic case bit-identical to Solve ─────────────────────────────

    [Fact]
    public void SolveIterative_OnAcyclicGraph_ProducesSameResultAsSolve()
    {
        var n = new ComponentNetwork();
        n.Add(new GainComponent("g1", 2.0));
        n.Add(new GainComponent("g2", 3.0));
        n.Connect("g1", "y", "g2", "x");
        n.SetExternalInput("g1", "x", 5.0);
        var r_acyclic  = n.Solve();
        var r_iterative = n.SolveIterative();
        Assert.Equal(r_acyclic["g2"]["y"], r_iterative["g2"]["y"], precision: 9);
    }

    // ── Cycle-closure case (the headline) ────────────────────────────────

    [Fact]
    public void SolveIterative_OnClosedLoop_ConvergesToFixedPoint()
    {
        // Closed-loop: output = gain · input, where input = setpoint + 0.1·output.
        // Algebraically: y = K·(setpoint + 0.1·y) → y = K·setpoint / (1 - 0.1·K).
        // With K = 2, setpoint = 10: y = 20 / 0.8 = 25.0.
        var n = new ComponentNetwork();
        n.Add(new GainComponent("plant",  2.0));     // y = 2·x
        n.Add(new GainComponent("decay",  0.1));     // back = 0.1·y
        n.Add(new SumComponent("sum"));               // x = setpoint + back

        n.Connect("plant", "y", "decay", "x");        // y → decay
        n.Connect("decay", "y", "sum",   "b");        // decay output → sum's b
        n.Connect("sum",   "sum", "plant", "x");      // sum → plant input

        n.SetExternalInput("sum", "a", 10.0);         // setpoint

        // Acyclic Solve must raise (cycle: plant → decay → sum → plant).
        // ThrowsAny<> rather than Throws<>: per #490, the concrete exception is
        // CyclicComponentNetworkException : InvalidOperationException, and
        // xUnit's strict Throws<T> rejects subclasses.
        Assert.ThrowsAny<InvalidOperationException>(() => n.Solve());

        // Iterative Solve converges to the analytical fixed point.
        var r = n.SolveIterative(maxIterations: 100, tolerance: 1e-9);
        Assert.Equal(25.0, r["plant"]["y"], precision: 6);
    }

    // ── Convergence-control surface ──────────────────────────────────────

    [Fact]
    public void SolveIterative_RaisesIfNotConverged()
    {
        // Unstable loop: K > 1 / feedback_gain → diverges.
        // Use K_plant = 2, K_back = 1.0 → y = 2·(setpoint + y) ⇒ infinite.
        var n = new ComponentNetwork();
        n.Add(new GainComponent("plant", 2.0));
        n.Add(new GainComponent("back",  1.0));
        n.Add(new SumComponent("sum"));
        n.Connect("plant", "y", "back", "x");
        n.Connect("back",  "y", "sum",  "b");
        n.Connect("sum",   "sum", "plant", "x");
        n.SetExternalInput("sum", "a", 1.0);
        // Won't converge — must raise.
        Assert.Throws<InvalidOperationException>(
            () => n.SolveIterative(maxIterations: 20, tolerance: 1e-9));
    }

    [Fact]
    public void SolveIterative_RejectsInvalidArgs()
    {
        var n = new ComponentNetwork();
        n.Add(new GainComponent("g", 2.0));
        n.SetExternalInput("g", "x", 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => n.SolveIterative(maxIterations: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => n.SolveIterative(tolerance: 0.0));
    }

    [Fact]
    public void SolveIterative_OnSingleComponent_ConvergesInOneIteration()
    {
        var n = new ComponentNetwork();
        n.Add(new GainComponent("g", 7.0));
        n.SetExternalInput("g", "x", 6.0);
        var r = n.SolveIterative();
        Assert.Equal(42.0, r["g"]["y"], precision: 9);
    }

    [Fact]
    public void SolveIterative_MaxIterationsRespected_WhenConvergent()
    {
        // The simple 2-gain DAG converges in 2 iterations; passing
        // maxIterations: 1 forces it to raise.
        var n = new ComponentNetwork();
        n.Add(new GainComponent("g1", 2.0));
        n.Add(new GainComponent("g2", 3.0));
        n.Connect("g1", "y", "g2", "x");
        n.SetExternalInput("g1", "x", 5.0);
        Assert.Throws<InvalidOperationException>(
            () => n.SolveIterative(maxIterations: 1, tolerance: 1e-9));
    }
}
