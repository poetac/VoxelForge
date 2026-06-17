// TimeIntegrationTests.cs — Sprint SI.W5 unit tests for the
// TimeStepIntegrator + IStatefulComponent pair.

using System;
using System.Collections.Generic;
using System.Linq;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class TimeIntegrationTests
{
    // ── A minimal stateful component: y(t) = y_0 · exp(−k·t).
    //    dy/dt = −k·y. Exponential-decay ODE — the standard sanity check.
    private sealed class ExponentialDecay : SystemComponent, IStatefulComponent
    {
        private readonly double _decayRate;
        private double _y;
        private readonly double _initialY;

        public ExponentialDecay(string name, double decayRate, double initialY)
            : base(name)
        {
            _decayRate = decayRate;
            _initialY  = initialY;
            _y         = initialY;
        }

        public override IReadOnlyList<string> InputPorts { get; } = Array.Empty<string>();

        public override IReadOnlyList<string> OutputPorts { get; } = new[] { "y" };

        public override void Evaluate(
            IReadOnlyDictionary<string, double> inputs,
            IDictionary<string, double> outputs)
        {
            outputs["y"] = _y;
        }

        // IStatefulComponent surface ──────────────────────────────────

        public IReadOnlyList<string> StateVariables { get; } = new[] { "y" };

        public void ComputeDerivatives(
            ReadOnlySpan<double> state,
            IReadOnlyDictionary<string, double> portInputs,
            IReadOnlyDictionary<string, double> portOutputs,
            Span<double> derivatives)
        {
            derivatives[0] = -_decayRate * state[0];
        }

        public void GetInitialState(Span<double> destination) => destination[0] = _initialY;
        public void GetCurrentState(Span<double> destination) => destination[0] = _y;
        public void SetState(ReadOnlySpan<double> state) => _y = state[0];
    }

    // ── Headline test: integrate exponential decay over [0, 1) ─────────

    [Fact]
    public void Integrate_ExponentialDecay_ConvergesToAnalyticalSolution()
    {
        // Analytical: y(t) = y_0 · exp(−k·t). With y_0 = 100, k = 1.0:
        //   y(1.0) = 100 · exp(-1) = 36.7879
        // Explicit Euler with dt = 0.01 → ~ 5 % overshoot at the end
        // (cumulative numerical error). dt = 0.001 → < 0.5 % error.
        var network = new ComponentNetwork();
        var decay = new ExponentialDecay("decay", decayRate: 1.0, initialY: 100.0);
        network.Add(decay);

        var integrator = new TimeStepIntegrator(network);
        integrator.RegisterStateful("decay", decay);

        var history = integrator.Run(t0_s: 0.0, tEnd_s: 1.0, dt_s: 0.001);

        // At t = 0, the FIRST snapshot should hold y = 100 exactly.
        Assert.Equal(100.0, history[0].PortValues["decay"]["y"], precision: 6);

        // At the LAST snapshot (just before t = 1.0): y ≈ 100·exp(-0.999)
        // = 36.81. Cluster band [36.0, 37.5].
        double y_end = history[^1].PortValues["decay"]["y"];
        Assert.InRange(y_end, 36.0, 37.5);
    }

    [Fact]
    public void Integrate_MonotonicDecay_AcrossAllSteps()
    {
        var network = new ComponentNetwork();
        var decay = new ExponentialDecay("d", decayRate: 0.5, initialY: 100.0);
        network.Add(decay);
        var integrator = new TimeStepIntegrator(network);
        integrator.RegisterStateful("d", decay);
        var history = integrator.Run(0.0, 2.0, 0.05);

        // y monotonically decreases across every step.
        for (int k = 1; k < history.Count; k++)
            Assert.True(history[k].PortValues["d"]["y"]
                      < history[k - 1].PortValues["d"]["y"]);
    }

    [Fact]
    public void Integrate_StateAndPortValuesAgree_AcrossSnapshots()
    {
        // Sanity: the recorded port value matches the recorded state.
        var network = new ComponentNetwork();
        var decay = new ExponentialDecay("d", decayRate: 2.0, initialY: 50.0);
        network.Add(decay);
        var integrator = new TimeStepIntegrator(network);
        integrator.RegisterStateful("d", decay);
        var history = integrator.Run(0.0, 0.5, 0.01);
        foreach (var snap in history)
            Assert.Equal(snap.PortValues["d"]["y"],
                         snap.StateValues["d"]["y"], precision: 9);
    }

    [Fact]
    public void Integrate_RejectsZeroOrNegativeStep()
    {
        var network = new ComponentNetwork();
        network.Add(new ExponentialDecay("d", 1.0, 1.0));
        var integrator = new TimeStepIntegrator(network);
        integrator.RegisterStateful("d", new ExponentialDecay("d", 1.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integrator.Run(0.0, 1.0, dt_s: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integrator.Run(0.0, 1.0, dt_s: -0.01));
    }

    [Fact]
    public void Integrate_RejectsBackwardsTimeInterval()
    {
        var network = new ComponentNetwork();
        network.Add(new ExponentialDecay("d", 1.0, 1.0));
        var integrator = new TimeStepIntegrator(network);
        integrator.RegisterStateful("d", new ExponentialDecay("d", 1.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integrator.Run(1.0, 0.5, 0.01));
    }

    [Fact]
    public void Integrate_HistoryLengthMatchesExpectedNumberOfTicks()
    {
        var network = new ComponentNetwork();
        var decay = new ExponentialDecay("d", 0.1, 10.0);
        network.Add(decay);
        var integrator = new TimeStepIntegrator(network);
        integrator.RegisterStateful("d", decay);
        // Run over closed [0, 1] with dt = 0.1 → 11 samples (N+1 semantics:
        // t = 0.0, 0.1, ..., 1.0 inclusive). Per #553 the integer-tick loop
        // emits exactly N+1 = round((tEnd-t0)/dt) + 1 snapshots,
        // independent of FP accumulator drift.
        var history = integrator.Run(0.0, 1.0, 0.1);
        Assert.Equal(11, history.Count);
    }

    [Fact]
    public void Integrate_NetworkWithoutStateful_RunsBackToBackEvaluatesOnly()
    {
        // Network has no stateful components — integrator just re-solves
        // the algebraic network at every tick (used for parameter-sweep
        // / piecewise-time-varying-input studies).
        var network = new ComponentNetwork();
        var decayPort = new ExponentialDecay("d", 1.0, 5.0);
        network.Add(decayPort);   // adapt port-only; no stateful register
        var integrator = new TimeStepIntegrator(network);
        // No RegisterStateful call → no state to integrate.
        // Run over closed [0, 0.5] with dt = 0.1 → 6 samples (#553 closed
        // N+1 semantics: t = 0.0, 0.1, 0.2, 0.3, 0.4, 0.5).
        var history = integrator.Run(0.0, 0.5, 0.1);
        Assert.Equal(6, history.Count);
        // Port values stay constant (no state to evolve).
        Assert.All(history, snap =>
            Assert.Equal(5.0, snap.PortValues["d"]["y"], precision: 9));
    }
}
