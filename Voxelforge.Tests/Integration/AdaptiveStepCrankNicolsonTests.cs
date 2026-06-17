// AdaptiveStepCrankNicolsonTests.cs — Sprint SI.W22 tests for the
// CN adaptive-step controller.
//
// Pins:
//   • dt grows when iterations stay below target (non-stiff regime).
//   • dt shrinks when iterations climb above target (stiff regime).
//   • dtMin / dtMax act as floor + ceiling.
//   • Non-overshoot: the final tick lands ≤ tEnd, never above.
//   • Argument guards on bad dt / dtMin / dtMax / target / tEnd.
//   • Determinism: repeated runs produce equal history.

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class AdaptiveStepCrankNicolsonTests
{
    private sealed class ExponentialDecay : SystemComponent, IStatefulComponent
    {
        private readonly double _lambda;
        private readonly double _initial;
        private double _y;

        public ExponentialDecay(string name, double lambda, double initialY)
            : base(name)
        {
            _lambda  = lambda;
            _initial = initialY;
            _y       = initialY;
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

    private static (ComponentNetwork net, ExponentialDecay decay, TimeStepIntegrator integrator)
        BuildHarness(double lambda, double y0 = 1.0)
    {
        var net = new ComponentNetwork();
        var d = new ExponentialDecay("d", lambda, y0);
        net.Add(d);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("d", d);
        return (net, d, integrator);
    }

    [Fact]
    public void Adaptive_GrowsDt_OnNonStiffDecay()
    {
        // Non-stiff system: λ = 0.5, dt_initial = 0.01, dtMax = 0.5.
        // CN should converge in ≤ 2 iterations almost immediately, driving
        // dt upward toward dtMax over the run.
        var (_, _, integrator) = BuildHarness(lambda: 0.5, y0: 1.0);
        var hist = integrator.RunAdaptiveCrankNicolson(
            t0_s:        0.0,
            tEnd_s:      5.0,
            dtInitial_s: 0.01,
            dtMin_s:     0.005,
            dtMax_s:     0.5,
            targetIterations: 5);

        // Final-tick gap must be near dtMax (controller saturated upward).
        // Use second-to-last → last gap as proxy.
        double lastGap = hist[^1].Time_s - hist[^2].Time_s;
        Assert.True(lastGap > 0.05,
            $"Adaptive controller should have grown dt on non-stiff decay. "
          + $"Last gap = {lastGap:F4}; expected > 0.05.");
    }

    [Fact]
    public void Adaptive_RespectsDtMax()
    {
        // Never exceed dtMax even on quiescent stretches.
        var (_, _, integrator) = BuildHarness(lambda: 0.1, y0: 1.0);
        var hist = integrator.RunAdaptiveCrankNicolson(
            t0_s:        0.0,
            tEnd_s:      10.0,
            dtInitial_s: 0.1,
            dtMin_s:     0.05,
            dtMax_s:     0.5);

        // Sample gaps; all must be ≤ dtMax (the last gap can be smaller
        // when t + dt would overshoot tEnd).
        for (int i = 1; i < hist.Count; i++)
        {
            double gap = hist[i].Time_s - hist[i - 1].Time_s;
            Assert.True(gap <= 0.5 + 1e-9,
                $"Gap at index {i} = {gap:F6} exceeds dtMax = 0.5.");
        }
    }

    [Fact]
    public void Adaptive_RespectsDtMin()
    {
        // dt must never drop below dtMin even on aggressively-stiff
        // systems where convergence repeatedly fails.
        var (_, _, integrator) = BuildHarness(lambda: 1000.0, y0: 1.0);
        var hist = integrator.RunAdaptiveCrankNicolson(
            t0_s:        0.0,
            tEnd_s:      0.1,
            dtInitial_s: 0.05,
            dtMin_s:     0.001,
            dtMax_s:     0.1);

        // Smallest internal gap (excluding the optional overshoot tail)
        // must be ≥ dtMin.
        double minGap = double.PositiveInfinity;
        for (int i = 1; i < hist.Count - 1; i++)
        {
            double gap = hist[i].Time_s - hist[i - 1].Time_s;
            if (gap < minGap) minGap = gap;
        }
        Assert.True(minGap >= 0.001 - 1e-9,
            $"Smallest gap {minGap:E3} dropped below dtMin = 0.001.");
    }

    [Fact]
    public void Adaptive_DoesNotOvershootTEnd()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0, y0: 1.0);
        var hist = integrator.RunAdaptiveCrankNicolson(
            t0_s:        0.0,
            tEnd_s:      1.0,
            dtInitial_s: 0.1,
            dtMin_s:     0.01,
            dtMax_s:     0.5);
        Assert.True(hist[^1].Time_s <= 1.0 + 1e-9,
            $"Adaptive run overshot tEnd. Final t = {hist[^1].Time_s:F9}.");
    }

    [Fact]
    public void Adaptive_RejectsNonPositiveDtInitial()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0, y0: 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integrator.RunAdaptiveCrankNicolson(0.0, 1.0, dtInitial_s: 0.0,  dtMin_s: 0.01, dtMax_s: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integrator.RunAdaptiveCrankNicolson(0.0, 1.0, dtInitial_s: -0.1, dtMin_s: 0.01, dtMax_s: 0.5));
    }

    [Fact]
    public void Adaptive_RejectsNonPositiveDtMin()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0, y0: 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integrator.RunAdaptiveCrankNicolson(0.0, 1.0, dtInitial_s: 0.1, dtMin_s: 0.0, dtMax_s: 0.5));
    }

    [Fact]
    public void Adaptive_RejectsDtMaxBelowDtMin()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0, y0: 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integrator.RunAdaptiveCrankNicolson(0.0, 1.0, dtInitial_s: 0.1, dtMin_s: 0.5, dtMax_s: 0.1));
    }

    [Fact]
    public void Adaptive_RejectsBadTargetIterations()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0, y0: 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integrator.RunAdaptiveCrankNicolson(0.0, 1.0, 0.1, 0.01, 0.5, targetIterations: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => integrator.RunAdaptiveCrankNicolson(0.0, 1.0, 0.1, 0.01, 0.5, targetIterations: 25));
    }

    [Fact]
    public void Adaptive_TracksFinalY_OnNonStiffDecay()
    {
        // λ = 1 → analytical y(1) = e^-1 ≈ 0.3679.
        // Newton-Raphson converges in ~2 iterations, which the adaptive
        // controller reads as "cheap step" and grows dt. dtMax is capped
        // at dtInitial so dt stays at 0.05 throughout; CN at dt=0.05 is
        // accurate to ≤ 3 decimals.
        var (_, _, integrator) = BuildHarness(lambda: 1.0, y0: 1.0);
        var hist = integrator.RunAdaptiveCrankNicolson(
            t0_s:        0.0,
            tEnd_s:      1.0,
            dtInitial_s: 0.05,
            dtMin_s:     0.01,
            dtMax_s:     0.05);
        double truth = Math.Exp(-1.0);
        double actual = hist[^1].PortValues["d"]["y"];
        Assert.Equal(truth, actual, precision: 3);
    }

    [Fact]
    public void Adaptive_Deterministic_RepeatedRuns()
    {
        var (_, _, int1) = BuildHarness(lambda: 5.0, y0: 1.0);
        var r1 = int1.RunAdaptiveCrankNicolson(0.0, 1.0, 0.05, 0.005, 0.2);

        var (_, _, int2) = BuildHarness(lambda: 5.0, y0: 1.0);
        var r2 = int2.RunAdaptiveCrankNicolson(0.0, 1.0, 0.05, 0.005, 0.2);

        Assert.Equal(r1.Count, r2.Count);
        for (int i = 0; i < r1.Count; i++)
        {
            Assert.Equal(r1[i].Time_s, r2[i].Time_s);
            Assert.Equal(r1[i].PortValues["d"]["y"],
                         r2[i].PortValues["d"]["y"]);
        }
    }

    [Fact]
    public void Adaptive_LastIterationCount_PopulatedAfterRun()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0, y0: 1.0);
        Assert.Equal(-1, integrator.LastCrankNicolsonIterations);
        integrator.RunAdaptiveCrankNicolson(0.0, 1.0, 0.1, 0.01, 0.5);
        Assert.True(integrator.LastCrankNicolsonIterations > 0,
            "Adaptive run should leave LastCrankNicolsonIterations populated.");
        Assert.True(integrator.LastCrankNicolsonIterations <= TimeStepIntegrator.CrankNicolsonMaxIterations);
    }

    [Fact]
    public void Adaptive_FinalSnapshotLandsExactlyAtTEnd()
    {
        // #553 closed-interval contract: the adaptive CN loop snaps the
        // final tick onto tEnd_s bit-exactly so the closed [t0, tEnd] guard
        // holds even when dt would not divide evenly into the horizon.
        // dtInitial = 0.07 over tEnd = 1.0 deliberately does not tile —
        // any remaining sub-dt tail must collapse to a snap onto 1.0.
        var (_, _, integrator) = BuildHarness(lambda: 1.0, y0: 1.0);
        var hist = integrator.RunAdaptiveCrankNicolson(
            t0_s:        0.0,
            tEnd_s:      1.0,
            dtInitial_s: 0.07,
            dtMin_s:     0.01,
            dtMax_s:     0.2);
        Assert.Equal(1.0, hist[^1].Time_s);  // bit-equality on double
    }
}
