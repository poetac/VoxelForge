// StreamingHistoryTests.cs — Sprint SI.W30 tests for the streaming
// variant of TimeStepIntegrator.Run.

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class StreamingHistoryTests
{
    private sealed class ExponentialDecay : SystemComponent, IStatefulComponent
    {
        private readonly double _lambda;
        private readonly double _initial;
        private double _y;

        public ExponentialDecay(string name, double lambda, double initialY)
            : base(name)
        {
            _lambda = lambda;
            _initial = initialY;
            _y = initialY;
        }

        public override IReadOnlyList<string> InputPorts { get; } = Array.Empty<string>();
        public override IReadOnlyList<string> OutputPorts { get; } = new[] { "y" };
        public override void Evaluate(
            IReadOnlyDictionary<string, double> _,
            IDictionary<string, double> outputs) => outputs["y"] = _y;

        public IReadOnlyList<string> StateVariables { get; } = new[] { "y" };
        public void ComputeDerivatives(
            ReadOnlySpan<double> state,
            IReadOnlyDictionary<string, double> _,
            IReadOnlyDictionary<string, double> __,
            Span<double> derivatives)
            => derivatives[0] = -_lambda * state[0];
        public void GetInitialState(Span<double> destination) => destination[0] = _initial;
        public void GetCurrentState(Span<double> destination) => destination[0] = _y;
        public void SetState(ReadOnlySpan<double> state) => _y = state[0];
    }

    private static (ComponentNetwork net, TimeStepIntegrator integrator)
        BuildHarness(double lambda = 1.0, double y0 = 1.0)
    {
        var net = new ComponentNetwork();
        var d = new ExponentialDecay("d", lambda, y0);
        net.Add(d);
        var integ = new TimeStepIntegrator(net);
        integ.RegisterStateful("d", d);
        return (net, integ);
    }

    [Fact]
    public void Streaming_InvokesCallbackPerTick()
    {
        var (_, integ) = BuildHarness();
        int callCount = 0;
        var emitted = integ.RunStreaming(
            t0_s:       0.0,
            tEnd_s:     1.0,
            dt_s:       0.1,
            onSnapshot: snap => callCount++);

        // 11 ticks at dt=0.1 across closed [0, 1] — same count as Run()
        // emits. Under #553 the integer-tick loop emits N+1 snapshots
        // (round((tEnd-t0)/dt)+1 = 11) at t = 0.0, 0.1, ..., 1.0.
        Assert.Equal(11, callCount);
        Assert.Equal(11, emitted);
    }

    [Fact]
    public void Streaming_SnapshotMatchesRunVariant_PerTick()
    {
        // Run() vs RunStreaming() must produce identical snapshot
        // sequences on the same network + same dt.
        var (_, runInteg) = BuildHarness();
        var runHistory = runInteg.Run(0.0, 1.0, 0.1, method: IntegrationMethod.Rk4);

        var (_, streamInteg) = BuildHarness();
        var streamed = new List<TimeHistorySnapshot>();
        streamInteg.RunStreaming(0.0, 1.0, 0.1, streamed.Add, method: IntegrationMethod.Rk4);

        Assert.Equal(runHistory.Count, streamed.Count);
        for (int i = 0; i < runHistory.Count; i++)
        {
            Assert.Equal(runHistory[i].Time_s,
                         streamed[i].Time_s);
            Assert.Equal(runHistory[i].PortValues["d"]["y"],
                         streamed[i].PortValues["d"]["y"]);
        }
    }

    [Fact]
    public void Streaming_NoHistoryRetained_OnCallerSide()
    {
        // The "drop the snapshot after use" pattern — caller emits to a
        // counter only. Memory usage stays O(1) regardless of horizon.
        var (_, integ) = BuildHarness();
        double finalY = double.NaN;
        int callCount = 0;
        var emitted = integ.RunStreaming(0.0, 10.0, 0.01,
            snap =>
            {
                callCount++;
                finalY = snap.PortValues["d"]["y"];
            },
            method: IntegrationMethod.Rk4);

        Assert.Equal(1001, callCount);
        Assert.Equal(1001, emitted);
        // Final y should approach analytical e^-10 = ~4.5e-5.
        Assert.InRange(finalY, 1e-5, 1e-4);
    }

    [Fact]
    public void Streaming_TerminalEventStopsLoop()
    {
        // Wire a terminal event at y < 0.5 — the streaming run should
        // stop early, mirroring Run()'s SI.W23 behaviour.
        var (_, integ) = BuildHarness();
        integ.RegisterEvent(new EventDefinition(
            Name:      "terminate_at_half",
            Predicate: ports => ports["d"]["y"] - 0.5,
            Direction: EventDirection.Falling,
            Terminal:  true));

        int callCount = 0;
        double lastT = double.NaN;
        var emitted = integ.RunStreaming(0.0, 5.0, 0.1,
            snap => { callCount++; lastT = snap.Time_s; },
            method: IntegrationMethod.Rk4);

        Assert.True(lastT < 1.0,
            $"Terminal event should have stopped the streaming run before t=1.0. "
          + $"Last tick at t={lastT:F3}.");
        Assert.Equal(callCount, emitted);
        Assert.Single(integ.LastDetectedEvents);
    }

    [Fact]
    public void Streaming_RejectsNullCallback()
    {
        var (_, integ) = BuildHarness();
        Assert.Throws<ArgumentNullException>(
            () => integ.RunStreaming(0.0, 1.0, 0.1, onSnapshot: null!));
    }

    [Fact]
    public void Streaming_RejectsBadDt()
    {
        var (_, integ) = BuildHarness();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integ.RunStreaming(0.0, 1.0, dt_s: 0.0, onSnapshot: _ => { }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integ.RunStreaming(0.0, 1.0, dt_s: -0.1, onSnapshot: _ => { }));
    }

    [Fact]
    public void Streaming_RejectsInvertedHorizon()
    {
        var (_, integ) = BuildHarness();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integ.RunStreaming(t0_s: 1.0, tEnd_s: 0.0, dt_s: 0.1, onSnapshot: _ => { }));
    }

    [Fact]
    public void Streaming_Deterministic_AcrossRepeatedRuns()
    {
        var (_, int1) = BuildHarness();
        var c1 = new List<double>();
        int1.RunStreaming(0.0, 1.0, 0.1, s => c1.Add(s.PortValues["d"]["y"]),
            method: IntegrationMethod.Rk4);

        var (_, int2) = BuildHarness();
        var c2 = new List<double>();
        int2.RunStreaming(0.0, 1.0, 0.1, s => c2.Add(s.PortValues["d"]["y"]),
            method: IntegrationMethod.Rk4);

        Assert.Equal(c1.Count, c2.Count);
        for (int i = 0; i < c1.Count; i++)
            Assert.Equal(c1[i], c2[i]);
    }

    [Fact]
    public void Streaming_RingBufferPattern_KeepsLastN()
    {
        // Demo of the "keep only the last 100 snapshots" pattern that
        // the streaming variant enables. The full 10 000-tick history
        // never materialises in memory.
        var (_, integ) = BuildHarness();
        var ring = new Queue<TimeHistorySnapshot>(100);
        var emitted = integ.RunStreaming(0.0, 10.0, 0.001,
            snap =>
            {
                if (ring.Count >= 100) ring.Dequeue();
                ring.Enqueue(snap);
            },
            method: IntegrationMethod.Rk4);

        Assert.Equal(10_001, emitted);
        Assert.Equal(100, ring.Count);             // ring stayed bounded
    }
}
