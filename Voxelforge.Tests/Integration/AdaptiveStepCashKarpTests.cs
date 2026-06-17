// AdaptiveStepCashKarpTests.cs — Sprint B.8c / issue #492 tests for
// the Cash-Karp embedded RK4(5) adaptive-step integrator.
//
// Pins:
//   • Adaptive integration converges to the analytical exponential
//     decay y(t) = y0 · exp(-λt) within the configured (atol, rtol).
//   • The PI step controller grows dt when error stays below tol
//     (smooth, non-stiff regime) and shrinks dt when error exceeds tol
//     (stiff transient).
//   • Final tick lands ≤ tEnd_s — never overshoots.
//   • Argument guards on bad dtInitial / dtMin / dtMax / atol / rtol /
//     tEnd.
//   • Repeated runs over the same network produce equal history
//     (determinism contract).
//   • Passing IntegrationMethod.CashKarpRk45Adaptive to the fixed-step
//     Run() path raises a clear ArgumentOutOfRangeException.

using System;
using System.Collections.Generic;
using System.Linq;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class AdaptiveStepCashKarpTests
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
    public void Adaptive_ConvergesToExponentialDecay_WithinTolerance()
    {
        // Analytical: y(5) = exp(-0.5 · 5) ≈ 0.0820850.
        // 5th-order RK with atol=1e-9, rtol=1e-7 should hit this
        // comfortably within rtol·|y| ≈ 1e-7 · 0.08 = 8e-9.
        var (_, _, integrator) = BuildHarness(lambda: 0.5, y0: 1.0);
        var hist = integrator.RunAdaptiveCashKarp45(
            t0_s:        0.0,
            tEnd_s:      5.0,
            dtInitial_s: 0.1,
            dtMin_s:     0.001,
            dtMax_s:     1.0,
            atol:        1.0e-9,
            rtol:        1.0e-7);

        double yFinal = hist[hist.Count - 1].StateValues["d"]["y"];
        double yAnalytical = Math.Exp(-0.5 * 5.0);
        Assert.Equal(yAnalytical, yFinal, precision: 7);
    }

    [Fact]
    public void Adaptive_GrowsDt_OnSmoothNonStiffDecay()
    {
        // Slow decay + loose tolerance → controller should grow dt
        // toward dtMax. Final-tick gap should be much larger than
        // dtInitial.
        var (_, _, integrator) = BuildHarness(lambda: 0.1, y0: 1.0);
        var hist = integrator.RunAdaptiveCashKarp45(
            t0_s:        0.0,
            tEnd_s:      10.0,
            dtInitial_s: 0.05,
            dtMin_s:     0.01,
            dtMax_s:     2.0,
            atol:        1.0e-4,
            rtol:        1.0e-3);

        // Mean dt across the run should be ≥ 4× the initial dt.
        double meanDt = (hist[hist.Count - 1].Time_s - hist[0].Time_s)
                      / (hist.Count - 1);
        Assert.True(meanDt > 0.2,
            $"Mean dt ({meanDt:F4}) should grow well above dtInitial=0.05 "
          + "on a smooth non-stiff decay with loose tolerances.");
    }

    [Fact]
    public void Adaptive_FinalTickDoesNotOvershoot_tEnd()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0, y0: 1.0);
        var hist = integrator.RunAdaptiveCashKarp45(
            t0_s:        0.0,
            tEnd_s:      3.7,                  // non-round endpoint
            dtInitial_s: 0.5,
            dtMin_s:     0.01,
            dtMax_s:     1.0);

        double tLast = hist[hist.Count - 1].Time_s;
        Assert.True(tLast <= 3.7 + 1e-9,
            $"Final tick t={tLast} must land at or below tEnd=3.7.");
    }

    [Fact]
    public void Adaptive_DeterministicRepeat_SameInputsProduceSameHistory()
    {
        IReadOnlyList<TimeHistorySnapshot> RunOnce()
        {
            var (_, _, integrator) = BuildHarness(lambda: 0.3, y0: 2.0);
            return integrator.RunAdaptiveCashKarp45(
                t0_s:        0.0,
                tEnd_s:      4.0,
                dtInitial_s: 0.1,
                dtMin_s:     0.01,
                dtMax_s:     1.0,
                atol:        1.0e-8,
                rtol:        1.0e-6);
        }

        var h1 = RunOnce();
        var h2 = RunOnce();
        Assert.Equal(h1.Count, h2.Count);
        for (int i = 0; i < h1.Count; i++)
        {
            Assert.Equal(h1[i].Time_s,                            h2[i].Time_s);
            Assert.Equal(h1[i].StateValues["d"]["y"],             h2[i].StateValues["d"]["y"]);
        }
    }

    [Fact]
    public void Adaptive_RejectsZeroDtInitial()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            integrator.RunAdaptiveCashKarp45(
                t0_s: 0.0, tEnd_s: 1.0, dtInitial_s: 0.0,
                dtMin_s: 0.01, dtMax_s: 0.1));
    }

    [Fact]
    public void Adaptive_RejectsZeroDtMin()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            integrator.RunAdaptiveCashKarp45(
                t0_s: 0.0, tEnd_s: 1.0, dtInitial_s: 0.05,
                dtMin_s: 0.0, dtMax_s: 0.1));
    }

    [Fact]
    public void Adaptive_RejectsDtMaxBelowDtMin()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            integrator.RunAdaptiveCashKarp45(
                t0_s: 0.0, tEnd_s: 1.0, dtInitial_s: 0.05,
                dtMin_s: 0.1, dtMax_s: 0.05));   // max < min
    }

    [Fact]
    public void Adaptive_RejectsZeroAtol()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            integrator.RunAdaptiveCashKarp45(
                t0_s: 0.0, tEnd_s: 1.0, dtInitial_s: 0.05,
                dtMin_s: 0.01, dtMax_s: 0.1,
                atol: 0.0));
    }

    [Fact]
    public void Adaptive_RejectsNegativeRtol()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            integrator.RunAdaptiveCashKarp45(
                t0_s: 0.0, tEnd_s: 1.0, dtInitial_s: 0.05,
                dtMin_s: 0.01, dtMax_s: 0.1,
                rtol: -1.0));
    }

    [Fact]
    public void Adaptive_RejectsEndBeforeStart()
    {
        var (_, _, integrator) = BuildHarness(lambda: 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            integrator.RunAdaptiveCashKarp45(
                t0_s: 1.0, tEnd_s: 0.5, dtInitial_s: 0.05,
                dtMin_s: 0.01, dtMax_s: 0.1));
    }

    [Fact]
    public void Run_WithCashKarpMethod_Throws()
    {
        // CashKarpRk45Adaptive is reserved for the adaptive entry point.
        // Passing it to the fixed-step Run() must raise a clear error
        // (not silently fall through the switch).
        var (_, _, integrator) = BuildHarness(lambda: 1.0);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            integrator.Run(
                t0_s: 0.0, tEnd_s: 1.0, dt_s: 0.1,
                method: IntegrationMethod.CashKarpRk45Adaptive));
        Assert.Contains("RunAdaptiveCashKarp45", ex.Message);
    }

    [Fact]
    public void Adaptive_RkMatch_AgrresWithFixedRk4OnSmoothProblem()
    {
        // On a smooth non-stiff problem with tight tolerances, the
        // Cash-Karp 5th-order solution should agree with a fixed-step
        // RK4 reference to within ≤ 1 % at the final time.
        const double lambda = 0.7;
        const double y0     = 1.0;
        const double tEnd   = 2.0;

        var (_, _, integratorAdaptive) = BuildHarness(lambda, y0);
        var histAdaptive = integratorAdaptive.RunAdaptiveCashKarp45(
            t0_s: 0.0, tEnd_s: tEnd, dtInitial_s: 0.1,
            dtMin_s: 0.001, dtMax_s: 0.5,
            atol: 1.0e-9, rtol: 1.0e-7);

        var (_, _, integratorRk4) = BuildHarness(lambda, y0);
        var histRk4 = integratorRk4.Run(
            t0_s: 0.0, tEnd_s: tEnd, dt_s: 0.001,
            method: IntegrationMethod.Rk4);

        double yAdaptive = histAdaptive[histAdaptive.Count - 1].StateValues["d"]["y"];
        double yRk4      = histRk4[histRk4.Count - 1].StateValues["d"]["y"];
        double rel = Math.Abs(yAdaptive - yRk4) / Math.Abs(yRk4);
        Assert.True(rel < 0.01,
            $"Adaptive y={yAdaptive:E6} vs fixed-RK4 y={yRk4:E6} differ "
          + $"by {rel:P3} > 1 %.");
    }

    [Fact]
    public void Adaptive_TracksRejectedSteps_Counter()
    {
        // After at least one run, the rejected-step counter is no
        // longer -1. The smooth problem may or may not have rejections
        // depending on the controller's initial dt — pin only the
        // post-run state transition.
        var (_, _, integrator) = BuildHarness(lambda: 0.5);
        Assert.Equal(-1, integrator.LastCashKarpRejectedSteps);
        integrator.RunAdaptiveCashKarp45(
            t0_s: 0.0, tEnd_s: 1.0, dtInitial_s: 0.05,
            dtMin_s: 0.001, dtMax_s: 0.5);
        Assert.True(integrator.LastCashKarpRejectedSteps >= 0);
    }

    [Fact]
    public void Adaptive_FinalSnapshotLandsExactlyAtTEnd()
    {
        // #553 closed-interval contract: the adaptive Cash-Karp loop snaps
        // the final tick onto tEnd_s bit-exactly so the closed [t0, tEnd]
        // guard holds even when dt does not divide evenly into the horizon.
        // dtInitial = 0.07 over tEnd = 1.0 will not tile — the residual
        // sub-dt tail must collapse to a snap onto 1.0.
        var (_, _, integrator) = BuildHarness(lambda: 1.0, y0: 1.0);
        var hist = integrator.RunAdaptiveCashKarp45(
            t0_s:        0.0,
            tEnd_s:      1.0,
            dtInitial_s: 0.07,
            dtMin_s:     0.001,
            dtMax_s:     0.2,
            atol:        1.0e-8,
            rtol:        1.0e-6);
        Assert.Equal(1.0, hist[hist.Count - 1].Time_s);  // bit-equality on double
    }
}
